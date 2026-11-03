using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BluetoothBatteryMonitor.App.Models;
using BluetoothBatteryMonitor.App.Services.Audio;
using Debug = global::System.Diagnostics.Debug;

namespace BluetoothBatteryMonitor.App.Services.Api;

public class LocalRestApiServer : IDisposable
{
    public const int DefaultPort = 23253;
    public const int MinPort = 1024;
    public const int MaxPort = 65535;

    private readonly object _lock = new();
    private HttpListener? _listener;
    private readonly Func<IReadOnlyList<BluetoothDeviceModel>> _getDevices;
    private readonly IAudioEndpointManager? _audioManager;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;
    private bool _isDisposed;

    public bool IsRunning { get; private set; }
    public int Port { get; private set; }
    public string? LastError { get; private set; }

    public event EventHandler<bool>? StateChanged;

    public static bool IsValidPort(int port) => port >= MinPort && port <= MaxPort;
    public static int ClampPort(int port) => IsValidPort(port) ? port : DefaultPort;

    /// <summary>
    /// True when the query string asks for connected devices only. Parsed as exact
    /// key=value pairs — a raw Contains("connected=true") would also match
    /// "?lastconnected=true" and silently filter the list.
    /// </summary>
    internal static bool ShouldFilterConnected(string? query)
        => (query ?? string.Empty)
            .TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Any(pair => string.Equals(pair, "connected=true", StringComparison.OrdinalIgnoreCase));

    public LocalRestApiServer(
        Func<IReadOnlyList<BluetoothDeviceModel>> getDevices,
        IAudioEndpointManager? audioManager = null,
        int port = DefaultPort)
    {
        _getDevices = getDevices ?? throw new ArgumentNullException(nameof(getDevices));
        _audioManager = audioManager;
        Port = ClampPort(port);
    }

    public void Start()
    {
        lock (_lock)
        {
            if (_isDisposed || IsRunning) return;

            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
                try
                {
                    _listener.Prefixes.Add($"http://localhost:{Port}/");
                }
                catch
                {
                    // Continue if localhost prefix cannot be added
                }

                try
                {
                    _listener.Start();
                }
                catch (HttpListenerException) when (_listener.Prefixes.Count > 1)
                {
                    // If Start() fails due to localhost prefix, fallback to 127.0.0.1 only
                    _listener.Prefixes.Clear();
                    _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
                    _listener.Start();
                }

                IsRunning = true;
                LastError = null;

                _cts = new CancellationTokenSource();
                var currentListener = _listener;
                _listenTask = Task.Run(() => ListenLoopAsync(currentListener, _cts.Token));
                StateChanged?.Invoke(this, true);
            }
            catch (Exception ex)
            {
                // Prevent crash if port is occupied, access denied, or HttpListener unsupported
                IsRunning = false;
                LastError = ex.Message;
                Debug.WriteLine($"[LocalRestApiServer] Failed to start (Port: {Port}): {ex.Message}");
                try
                {
                    _listener?.Close();
                }
                catch { }
                _listener = null;
                StateChanged?.Invoke(this, false);
            }
        }
    }

    public void Restart(int newPort)
    {
        lock (_lock)
        {
            Stop();
            Port = ClampPort(newPort);
            Start();
        }
    }

    private async Task ListenLoopAsync(HttpListener listener, CancellationToken token)
    {
        while (!token.IsCancellationRequested && listener.IsListening)
        {
            try
            {
                var context = await listener.GetContextAsync();
                _ = ProcessRequestAsync(context, token);
            }
            catch (HttpListenerException) when (token.IsCancellationRequested || !listener.IsListening)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LocalRestApiServer] Listener loop error: {ex.Message}");
            }
        }
    }

    private async Task ProcessRequestAsync(HttpListenerContext context, CancellationToken token)
    {
        try
        {
            var req = context.Request;
            var res = context.Response;

            // No CORS headers on purpose: this API is for local tools (curl, scripts). A wildcard
            // would let any website open in any browser read the user's device list — names,
            // ids and battery — via a drive-by fetch to 127.0.0.1. Same-origin pages have none,
            // and non-browser clients ignore CORS entirely.
            if (req.HttpMethod.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
            {
                res.StatusCode = (int)HttpStatusCode.NoContent;
                res.Close();
                return;
            }

            if (!req.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase))
            {
                res.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
                await WriteJsonResponseAsync(res, new { error = "Method Not Allowed" }, token);
                return;
            }

            string rawPath = req.Url?.AbsolutePath.TrimEnd('/') ?? string.Empty;

            if (string.Equals(rawPath, "/devices", StringComparison.OrdinalIgnoreCase))
            {
                await HandleDevicesRequestAsync(req, res, token);
            }
            else if (string.Equals(rawPath, "/devices/summary", StringComparison.OrdinalIgnoreCase))
            {
                await HandleSummaryRequestAsync(res, token);
            }
            else if (string.IsNullOrEmpty(rawPath) || string.Equals(rawPath, "/health", StringComparison.OrdinalIgnoreCase))
            {
                res.StatusCode = (int)HttpStatusCode.OK;
                await WriteJsonResponseAsync(res, new
                {
                    status = "ok",
                    service = "BluetoothBatteryMonitor",
                    // Was hardcoded "1.0.0" and always lied about the running version.
                    version = Update.UpdateCheckService.GetCurrentVersion(),
                    timestamp = DateTime.UtcNow.ToString("o")
                }, token);
            }
            else
            {
                res.StatusCode = (int)HttpStatusCode.NotFound;
                await WriteJsonResponseAsync(res, new { error = "Not Found", path = rawPath }, token);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LocalRestApiServer] İstek işleme hatası: {ex.Message}");
            try
            {
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                context.Response.Close();
            }
            catch { }
        }
    }

    private async Task HandleDevicesRequestAsync(HttpListenerRequest req, HttpListenerResponse res, CancellationToken token)
    {
        var devices = _getDevices() ?? Array.Empty<BluetoothDeviceModel>();

        // Exact key=value match on parsed pairs. A raw Contains() also matched values like
        // "?lastconnected=true" and silently filtered the list.
        if (ShouldFilterConnected(req.Url?.Query))
        {
            devices = devices.Where(d => d.IsConnected).ToList();
        }

        var data = devices.Select(d =>
        {
            AudioDeviceInfo? endpoint = null;
            if (_audioManager != null && d.IsConnected && (d.Type is DeviceType.Headphones or DeviceType.Earbuds or DeviceType.Speaker))
            {
                endpoint = _audioManager.FindEndpointForBluetoothDevice(d);
            }

            return new
            {
                id = d.Id,
                name = d.Name,
                modelName = d.ModelName,
                type = d.Type.ToString(),
                audioCodec = d.AudioCodec,
                batteryLevel = d.BatteryLevel,
                effectiveBatteryLevel = d.EffectiveBatteryLevel,
                isCharging = d.IsCharging,
                isConnected = d.IsConnected,
                providerSource = d.ProviderSource,
                isAggregated = d.IsAggregated,
                isTws = d.IsTws,
                tws = d.IsTws ? new
                {
                    isTws = true,
                    leftBatteryLevel = d.LeftBatteryLevel,
                    isLeftCharging = d.IsLeftCharging,
                    rightBatteryLevel = d.RightBatteryLevel,
                    isRightCharging = d.IsRightCharging,
                    caseBatteryLevel = d.CaseBatteryLevel,
                    isCaseCharging = d.IsCaseCharging
                } : null,
                volume = new
                {
                    hasAudioEndpoint = endpoint != null,
                    audioEndpointId = endpoint?.Id,
                    audioEndpointName = endpoint?.Name,
                    volumePercent = endpoint != null ? (int)Math.Round(endpoint.VolumePercent * 100) : 0,
                    isMuted = endpoint?.IsMuted ?? false
                },
                lastUpdated = d.LastUpdated.ToString("o")
            };
        }).ToList();

        res.StatusCode = (int)HttpStatusCode.OK;
        await WriteJsonResponseAsync(res, data, token);
    }

    private async Task HandleSummaryRequestAsync(HttpListenerResponse res, CancellationToken token)
    {
        var devices = _getDevices() ?? Array.Empty<BluetoothDeviceModel>();
        var connected = devices.Where(d => d.IsConnected).ToList();
        var withBattery = connected.Where(d => d.EffectiveBatteryLevel.HasValue).ToList();

        int? lowest = withBattery.Count > 0 ? withBattery.Min(d => d.EffectiveBatteryLevel) : null;
        bool isLowestCharging = withBattery.Any(d => d.EffectiveBatteryLevel == lowest && d.IsCharging);

        var summary = new
        {
            totalDevices = devices.Count,
            connectedDevices = connected.Count,
            lowestBatteryLevel = lowest,
            isLowestCharging = isLowestCharging,
            devices = connected.Select(d => new
            {
                id = d.Id,
                name = d.Name,
                type = d.Type.ToString(),
                audioCodec = d.AudioCodec,
                batteryLevel = d.EffectiveBatteryLevel,
                isCharging = d.IsCharging,
                isTws = d.IsTws,
                isAggregated = d.IsAggregated
            }).ToList(),
            timestamp = DateTime.UtcNow.ToString("o")
        };

        res.StatusCode = (int)HttpStatusCode.OK;
        await WriteJsonResponseAsync(res, summary, token);
    }

    private static async Task WriteJsonResponseAsync<T>(HttpListenerResponse res, T data, CancellationToken token)
    {
        try
        {
            res.ContentType = "application/json; charset=utf-8";
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            byte[] buffer = JsonSerializer.SerializeToUtf8Bytes(data, options);
            res.ContentLength64 = buffer.Length;

            using var stream = res.OutputStream;
            await stream.WriteAsync(buffer, token);
        }
        finally
        {
            try
            {
                res.Close();
            }
            catch { }
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (!IsRunning && _listener == null) return;

            try
            {
                _cts?.Cancel();
                if (_listener?.IsListening == true)
                {
                    _listener.Stop();
                }
                _listener?.Close();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LocalRestApiServer] Stop hatası: {ex.Message}");
            }
            finally
            {
                _listener = null;
                IsRunning = false;
                StateChanged?.Invoke(this, false);
            }
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        Stop();
        try
        {
            _cts?.Dispose();
        }
        catch
        {
            // Sessizce kapat
        }
    }
}
