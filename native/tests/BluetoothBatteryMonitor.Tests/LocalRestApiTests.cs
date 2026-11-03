extern alias MonitorApp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using Xunit;
using ApiServer = MonitorApp::BluetoothBatteryMonitor.App.Services.Api.LocalRestApiServer;
using AppDevice = MonitorApp::BluetoothBatteryMonitor.App.Models.BluetoothDeviceModel;
using UpdateCheckService = MonitorApp::BluetoothBatteryMonitor.App.Services.Update.UpdateCheckService;

public class LocalRestApiTests
{
    [Theory]
    [InlineData("?connected=true", true)]
    [InlineData("?connected=true&fresh=1", true)]
    [InlineData("?fresh=1&connected=true", true)]
    [InlineData("?lastconnected=true", false)]
    [InlineData("?connected=false", false)]
    [InlineData("?connected=", false)]
    [InlineData("", false)]
    public void ShouldFilterConnected_MatchesExactPairsOnly(string query, bool expected)
        => Assert.Equal(expected, ApiServer.ShouldFilterConnected(query));

    [Fact]
    public void ShouldFilterConnected_ToleratesANullQuery()
        => Assert.False(ApiServer.ShouldFilterConnected(null));

    [Theory]
    [InlineData(23253, 23253)]
    [InlineData(1024, 1024)]
    [InlineData(65535, 65535)]
    [InlineData(0, ApiServer.DefaultPort)]
    [InlineData(80, ApiServer.DefaultPort)]
    [InlineData(70000, ApiServer.DefaultPort)]
    [InlineData(-1, ApiServer.DefaultPort)]
    public void ClampPort_KeepsValidPortsAndClampsTheRest(int input, int expected)
        => Assert.Equal(expected, ApiServer.ClampPort(input));

    [Fact]
    public async Task HealthEndpoint_ReportsTheRealVersionAndSendsNoCorsHeader()
    {
        int port = FindFreePort();
        using var server = new ApiServer(() => new List<AppDevice>(), audioManager: null, port);
        server.Start();
        if (!server.IsRunning)
        {
            // http.sys may deny the URL reservation in locked-down environments; the
            // pure-function coverage above still holds.
            return;
        }

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            using var res = await http.GetAsync($"http://127.0.0.1:{port}/health");
            res.EnsureSuccessStatusCode();

            string body = await res.Content.ReadAsStringAsync();
            Assert.Contains("\"status\": \"ok\"", body);
            Assert.Contains(UpdateCheckService.GetCurrentVersion(), body);

            // The whole point of dropping the wildcard: a browser page on any website must
            // not be able to read this response via CORS.
            Assert.False(res.Headers.Contains("Access-Control-Allow-Origin"));
        }
        finally
        {
            server.Stop();
        }
    }

    [Fact]
    public async Task DevicesEndpoint_OnlyFiltersConnectedOnExactMatch()
    {
        int port = FindFreePort();
        var devices = new List<AppDevice>
        {
            new() { Id = "d1", Name = "Mouse", Type = MonitorApp::BluetoothBatteryMonitor.App.Models.DeviceType.Mouse, BatteryLevel = 76, IsConnected = true },
            new() { Id = "d2", Name = "Headset", Type = MonitorApp::BluetoothBatteryMonitor.App.Models.DeviceType.Headphones, BatteryLevel = 40, IsConnected = false }
        };
        using var server = new ApiServer(() => devices, audioManager: null, port);
        server.Start();
        if (!server.IsRunning) return;

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

            using var all = await http.GetAsync($"http://127.0.0.1:{port}/devices");
            string allBody = await all.Content.ReadAsStringAsync();
            Assert.Contains("Mouse", allBody);
            Assert.Contains("Headset", allBody);

            using var filtered = await http.GetAsync($"http://127.0.0.1:{port}/devices?connected=true");
            string filteredBody = await filtered.Content.ReadAsStringAsync();
            Assert.Contains("Mouse", filteredBody);
            Assert.DoesNotContain("Headset", filteredBody);

            // Regression: "?lastconnected=true" used to filter too (raw Contains match).
            using var tricky = await http.GetAsync($"http://127.0.0.1:{port}/devices?lastconnected=true");
            string trickyBody = await tricky.Content.ReadAsStringAsync();
            Assert.Contains("Headset", trickyBody);
        }
        finally
        {
            server.Stop();
        }
    }

    private static int FindFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try { return ((IPEndPoint)listener.LocalEndpoint).Port; }
        finally { listener.Stop(); }
    }
}
