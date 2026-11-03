extern alias MonitorApp;
using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using UpdateInstaller = MonitorApp::BluetoothBatteryMonitor.App.Services.Update.UpdateInstaller;
using UpdateCheckService = MonitorApp::BluetoothBatteryMonitor.App.Services.Update.UpdateCheckService;

public class UpdateHardeningTests
{
    // ---------------------------------------------------------------- install dir

    [Fact]
    public void ResolveInstallDirectory_PrefersAValidRegistryValue()
    {
        using var temp = new TempDirectory();
        string resolved = UpdateInstaller.ResolveInstallDirectory(temp.Path, @"C:\fallback");
        Assert.Equal(temp.Path, resolved, ignoreCase: true);
    }

    [Fact]
    public void ResolveInstallDirectory_FallsBackWhenRegistryDirIsMissing()
    {
        string missing = Path.Combine(Path.GetTempPath(), "peripheral-does-not-exist-" + Guid.NewGuid().ToString("N"));
        Assert.Equal(@"C:\fallback", UpdateInstaller.ResolveInstallDirectory(missing, @"C:\fallback"));
    }

    [Fact]
    public void ResolveInstallDirectory_FallsBackOnMalformedValues()
    {
        Assert.Equal(@"C:\fallback", UpdateInstaller.ResolveInstallDirectory(null, @"C:\fallback"));
        Assert.Equal(@"C:\fallback", UpdateInstaller.ResolveInstallDirectory("", @"C:\fallback"));
        Assert.Equal(@"C:\fallback", UpdateInstaller.ResolveInstallDirectory("   ", @"C:\fallback"));
        // Illegal path characters must not throw.
        Assert.Equal(@"C:\fallback", UpdateInstaller.ResolveInstallDirectory("a||b<>:", @"C:\fallback"));
    }

    [Fact]
    public void ReadRegistryInstallDir_ReturnsNullOrAnExistingDirectory()
    {
        // The value only exists after installing with the v0.3.13+ Setup; on a clean machine
        // this is null, and when present it must point at a real directory.
        string? value = UpdateInstaller.ReadRegistryInstallDir();
        Assert.True(value == null || Directory.Exists(value),
            $"registry InstallDir must be null or an existing directory, got '{value}'");
    }

    // ---------------------------------------------------------------- log rotation

    [Fact]
    public void RotateLogIfNeeded_MovesAnOversizedLogToOld()
    {
        using var temp = new TempDirectory();
        string log = Path.Combine(temp.Path, "update.log");
        File.WriteAllText(log, new string('x', 2048));

        UpdateInstaller.RotateLogIfNeeded(log, maxBytes: 1024);

        Assert.False(File.Exists(log));
        Assert.True(File.Exists(log + ".old"));
        Assert.Equal(2048, new FileInfo(log + ".old").Length);
    }

    [Fact]
    public void RotateLogIfNeeded_KeepsASmallLog()
    {
        using var temp = new TempDirectory();
        string log = Path.Combine(temp.Path, "update.log");
        File.WriteAllText(log, "tiny");

        UpdateInstaller.RotateLogIfNeeded(log, maxBytes: 1024);

        Assert.True(File.Exists(log));
        Assert.False(File.Exists(log + ".old"));
    }

    [Fact]
    public void RotateLogIfNeeded_ReplacesThePreviousGeneration()
    {
        using var temp = new TempDirectory();
        string log = Path.Combine(temp.Path, "update.log");
        string old = log + ".old";
        File.WriteAllText(old, "previous generation");
        File.WriteAllText(log, new string('x', 4096));

        UpdateInstaller.RotateLogIfNeeded(log, maxBytes: 1024);

        Assert.False(File.Exists(log));
        Assert.Equal(4096, new FileInfo(old).Length);
    }

    [Fact]
    public void RotateLogIfNeeded_ToleratesAMissingOrLockedFile()
    {
        string missing = Path.Combine(Path.GetTempPath(), "peripheral-no-log-" + Guid.NewGuid().ToString("N") + ".log");
        UpdateInstaller.RotateLogIfNeeded(missing); // must not throw

        using var temp = new TempDirectory();
        string locked = Path.Combine(temp.Path, "locked.log");
        File.WriteAllText(locked, new string('x', 2048));
        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            UpdateInstaller.RotateLogIfNeeded(locked, maxBytes: 1024); // must not throw
        }
    }

    // ---------------------------------------------------------------- rate limit + cache

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;
        public FakeHandler(HttpResponseMessage response) => _response = response;
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_response);
    }

    [Theory]
    [InlineData(403)]
    [InlineData(429)]
    public void DescribeApiError_NamesTheRateLimit(int status)
    {
        string message = UpdateCheckService.DescribeApiError(status);
        Assert.Contains("rate limit", message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(502)]
    public void DescribeApiError_ReportsOtherStatusesPlainly(int status)
    {
        string message = UpdateCheckService.DescribeApiError(status);
        Assert.DoesNotContain("rate limit", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(status.ToString(), message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_RateLimited_YieldsARateLimitMessage()
    {
        UpdateCheckService.ResetCacheForTests();
        var service = new UpdateCheckService(new HttpClient(
            new FakeHandler(new HttpResponseMessage(System.Net.HttpStatusCode.Forbidden))));

        var result = await service.CheckForUpdatesAsync();

        Assert.False(result.HasUpdate);
        Assert.Contains("rate limit", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseReleaseJson_TreatsATaggedNewerReleaseAsAnUpdate()
    {
        const string json = "{\"tag_name\":\"v9.9.9\",\"html_url\":\"https://github.com/XLJFZ/peripheral-battery/releases/tag/v9.9.9\",\"assets\":[]}";
        var result = UpdateCheckService.ParseReleaseJson(json, "0.3.12");
        Assert.True(result.HasUpdate);
        Assert.Equal("9.9.9", result.LatestVersion);
        Assert.Equal("https://github.com/XLJFZ/peripheral-battery/releases/tag/v9.9.9", result.ReleaseUrl);
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "peripheral-hardening-" + Guid.NewGuid().ToString("N"));

        public TempDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }
}
