using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace BluetoothBatteryMonitor.App.Services.Update;

public enum UpdateStage
{
    Downloading,
    Verifying,
    Launching,
    Done,
    Failed
}

public record UpdateProgress(UpdateStage Stage, int Percent);

/// <param name="ErrorCode">Language-neutral code; the UI maps it to a localized message.</param>
public record UpdateInstallResult(bool Success, string? ErrorCode = null, string? InstallerPath = null);

/// <summary>
/// Downloads the release installer, verifies its SHA-256 against the published checksum,
/// strips the mark-of-the-web, then launches it silently and lets the caller exit.
/// Only used for the installed copy; the portable single file has no installer to run.
/// </summary>
public static class UpdateInstaller
{
    private static readonly string[] AllowedHosts =
    {
        "github.com",
        "objects.githubusercontent.com",
        "github-releases.githubusercontent.com"
    };

    private static string ProgramDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Programs", "PeripheralBattery");

    /// <summary>True when this process runs from the installer-managed folder.</summary>
    public static bool IsInstalledCopy()
    {
        try
        {
            string current = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar);
            string expected = Path.GetFullPath(ProgramDir).TrimEnd(Path.DirectorySeparatorChar);
            return string.Equals(current, expected, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Our own state folder (%LOCALAPPDATA%\PeripheralBattery) — logs and the pending marker.</summary>
    public static string StateFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PeripheralBattery");

    public static string UpdateFolder => Path.Combine(StateFolder, "updates");

    public static string InstallLogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PeripheralBattery", "install.log");

    public static string LogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PeripheralBattery", "update.log");

    public static async Task<UpdateInstallResult> DownloadAndLaunchAsync(
        UpdateCheckResult update,
        IProgress<UpdateProgress>? progress,
        CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(update.DownloadUrl) || !IsAllowed(update.DownloadUrl))
                return Fail("blockedHost");

            string fileName = LocalFileName(update.DownloadUrl);
            if (!IsInstallerFileName(fileName))
                return Fail("noInstaller");

            Directory.CreateDirectory(UpdateFolder);
            string target = Path.Combine(UpdateFolder, fileName);

            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            http.DefaultRequestHeaders.Add("User-Agent", "PeripheralBattery-Updater");

            // Reuse a previously downloaded file only when its hash still matches.
            var (expectedHash, checksumError) = await FetchExpectedHashAsync(http, update, fileName, ct).ConfigureAwait(false);
            if (expectedHash == null)
                return Fail(checksumError ?? "noChecksum");

            if (File.Exists(target) && string.Equals(await HashAsync(target, ct).ConfigureAwait(false), expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                Log($"reusing verified installer {target}");
            }
            else
            {
                // A 75 MB download over a flaky link should not cost the user the whole update,
                // so a failed attempt gets one clean retry from zero.
                for (int attempt = 1; ; attempt++)
                {
                    Log($"downloading {update.DownloadUrl} -> {target} (attempt {attempt})");
                    var download = await DownloadAsync(http, update.DownloadUrl!, target, progress, ct).ConfigureAwait(false);
                    if (download.Success) break;
                    if (attempt >= DownloadAttempts || download.ErrorCode == "cancelled") return download;
                    Log($"attempt {attempt} failed: {download.ErrorCode}; retrying");
                    await Task.Delay(TimeSpan.FromSeconds(3 * attempt), ct).ConfigureAwait(false);
                }
            }

            progress?.Report(new UpdateProgress(UpdateStage.Verifying, 100));
            string actual = await HashAsync(target, ct).ConfigureAwait(false);
            if (!string.Equals(actual, expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                TryDelete(target);
                Log($"hash mismatch expected={expectedHash} actual={actual}");
                return Fail("hashMismatch");
            }

            RemoveMarkOfTheWeb(target);

            progress?.Report(new UpdateProgress(UpdateStage.Launching, 100));

            // Written before launching: if Setup never finishes, the next start of the old
            // version sees this marker and can tell the user the upgrade did not land.
            UpdatePendingUpdate.Record(UpdateCheckService.GetCurrentVersion(), update.LatestVersion, target);

            var startInfo = new ProcessStartInfo(target)
            {
                // No shell execute: avoids a SmartScreen prompt in the middle of an update.
                UseShellExecute = false,
                Arguments = BuildArguments()
            };
            Process.Start(startInfo);
            Log("installer launched");
            return new UpdateInstallResult(true, InstallerPath: target);
        }
        catch (OperationCanceledException)
        {
            return Fail("cancelled");
        }
        catch (Exception ex)
        {
            Log("failed: " + ex);
            return Fail("failed");
        }
    }

    /// <summary>Keeps the user's autostart choice instead of resetting it on every upgrade.</summary>
    private static string BuildArguments()
    {
        var settings = Models.AppSettings.Load();
        string tasks = settings.LaunchAtStartup ? "/TASKS=autostart" : "/TASKS=\"\"";
        // FORCECLOSEAPPLICATIONS makes Setup end this process itself; the AppMutex check
        // would otherwise stop a silent upgrade because we are still running.
        // /LOG keeps a record of the silent run next to update.log for support.
        return $"/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /FORCECLOSEAPPLICATIONS {tasks} /LOG=\"{InstallLogPath}\"".Trim();
    }

    private static async Task<UpdateInstallResult> DownloadAsync(
        HttpClient http, string url, string target, IProgress<UpdateProgress>? progress, CancellationToken ct)
    {
        string temp = target + ".part";
        using (var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
        {
            if (!response.IsSuccessStatusCode)
                return Fail("httpError");

            long? total = response.Content.Headers.ContentLength;
            await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var destination = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

            var buffer = new byte[81920];
            long read = 0;
            int lastReported = -1;
            int chunk;
            while ((chunk = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, chunk), ct).ConfigureAwait(false);
                read += chunk;
                if (total is > 0)
                {
                    int percent = (int)(read * 100 / total.Value);
                    if (percent != lastReported)
                    {
                        lastReported = percent;
                        progress?.Report(new UpdateProgress(UpdateStage.Downloading, percent));
                    }
                }
            }
        }

        if (File.Exists(target)) TryDelete(target);
        File.Move(temp, target);
        return new UpdateInstallResult(true, InstallerPath: target);
    }

    private const int DownloadAttempts = 2;
    private const int ChecksumAttempts = 3;

    /// <summary>Fetches the published checksum, retrying transient network failures.</summary>
    private static async Task<(string? Hash, string? ErrorCode)> FetchExpectedHashAsync(
        HttpClient http, UpdateCheckResult update, string fileName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(update.ChecksumUrl) || !IsAllowed(update.ChecksumUrl))
            return (null, "noChecksum");

        for (int attempt = 1; attempt <= ChecksumAttempts; attempt++)
        {
            try
            {
                string text = await http.GetStringAsync(update.ChecksumUrl!, ct).ConfigureAwait(false);
                string? hash = ParseChecksum(text, fileName);
                return hash == null ? (null, "noChecksum") : (hash, null);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log($"checksum fetch attempt {attempt} failed: {ex.Message}");
                if (attempt == ChecksumAttempts) return (null, "checksumFetch");
                await Task.Delay(TimeSpan.FromSeconds(attempt), ct).ConfigureAwait(false);
            }
        }

        return (null, "checksumFetch");
    }

    /// <summary>
    /// Picks the hash for <paramref name="fileName"/> out of a published checksum file.
    /// Handles both "&lt;hash&gt;  &lt;file&gt;" (sha256sum) and a bare single-line hash.
    /// </summary>
    internal static string? ParseChecksum(string text, string fileName)
    {
        foreach (string line in text.Split('\n'))
        {
            // File.WriteAllText on Windows may prepend a UTF-8 BOM; strip it before hashing the line.
            string trimmed = line.Trim().TrimStart('\uFEFF');
            if (trimmed.Length < 64) continue;
            string hash = trimmed[..64];
            if (!IsHex(hash)) continue;
            string rest = trimmed[64..].TrimStart('*', ' ');
            if (rest.Length == 0) return hash;                       // single-line sha256 file
            if (rest.EndsWith(fileName, StringComparison.OrdinalIgnoreCase)) return hash;
        }
        return null;
    }

    private static async Task<string> HashAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        byte[] hash = await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    internal static bool IsAllowed(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
           uri.Scheme == Uri.UriSchemeHttps &&
           Array.Exists(AllowedHosts, h => string.Equals(uri.Host, h, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// File name of a release asset URL, percent-decoded. GitHub encodes non-ASCII asset names, so
    /// comparing the raw URL path against the sha256.txt entries would fail to find the hash and
    /// abort the update with a misleading "noChecksum".
    /// </summary>
    internal static string LocalFileName(string url)
    {
        string raw;
        try { raw = new Uri(url).AbsolutePath; }
        catch { return string.Empty; }

        string decoded;
        try { decoded = Uri.UnescapeDataString(raw); }
        catch { decoded = raw; }

        // Decoding can reveal a separator again; Path.GetFileName strips both '/' and '\'.
        return Path.GetFileName(decoded);
    }

    /// <summary>
    /// Only a Setup executable may be launched as an updater. A release without a Setup asset used
    /// to fall back to the portable single file, which silently ignores /VERYSILENT, starts a
    /// second tray instance that then loses the single-instance race — the user sees "installing"
    /// and ends up with no app at all.
    /// </summary>
    internal static bool IsInstallerFileName(string fileName)
        => !string.IsNullOrEmpty(fileName) &&
           fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
           fileName.Contains("setup", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Drops left-over downloads: interrupted transfers (*.part) right away, and installers older
    /// than <paramref name="keepFor"/>. Each installer is ~72 MB, so a few upgrades otherwise leave
    /// hundreds of megabytes behind in %LOCALAPPDATA%.
    /// </summary>
    internal static int PruneStaleDownloads(TimeSpan? keepFor = null, DateTime? nowUtc = null, string? folder = null)
    {
        folder ??= UpdateFolder;
        if (!Directory.Exists(folder)) return 0;

        var cutoff = (nowUtc ?? DateTime.UtcNow) - (keepFor ?? TimeSpan.FromDays(2));

        string[] files;
        try { files = Directory.GetFiles(folder); }
        catch { return 0; }

        int removed = 0;
        foreach (string file in files)
        {
            try
            {
                string extension = Path.GetExtension(file);
                bool stale = extension.Equals(".part", StringComparison.OrdinalIgnoreCase) ||
                             (extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) &&
                              File.GetLastWriteTimeUtc(file) < cutoff);
                if (!stale) continue;

                File.Delete(file);
                removed++;
            }
            catch
            {
                // Locked by a running installer; the next start tries again.
            }
        }

        if (removed > 0) Log($"pruned {removed} stale update file(s)");
        return removed;
    }

    private static bool IsHex(string value)
    {
        foreach (char c in value)
        {
            if (!Uri.IsHexDigit(c)) return false;
        }
        return true;
    }

    /// <summary>Removes the Zone.Identifier stream so the installer is not treated as downloaded.</summary>
    private static void RemoveMarkOfTheWeb(string path)
    {
        try
        {
            DeleteFile(path + ":Zone.Identifier");
            Log("mark-of-the-web removed");
        }
        catch (Exception ex)
        {
            Log("could not remove MOTW: " + ex.Message);
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool DeleteFile(string name);

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { }
    }

    private static UpdateInstallResult Fail(string code)
    {
        Log("error: " + code);
        return new UpdateInstallResult(false, code);
    }

    internal static void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
        }
        catch { }
    }
}
