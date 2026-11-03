extern alias MonitorApp;
using System.Text.Json;
using Xunit;
using UpdateCheckService = MonitorApp::BluetoothBatteryMonitor.App.Services.Update.UpdateCheckService;

public class UpdateAssetSelectionTests
{
    private static (string? Download, string? Checksum) Select(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return UpdateCheckService.SelectAssets(doc.RootElement);
    }

    // The real v0.3.8 asset order: portable EXE, Setup EXE, checksum last.
    private const string RealOrder = """
    {
      "tag_name": "v0.3.8",
      "assets": [
        { "name": "Peripheral-Battery-0.3.8-x64.exe",   "browser_download_url": "https://github.com/x/portable.exe" },
        { "name": "Peripheral-Battery-Setup-0.3.8.exe", "browser_download_url": "https://github.com/x/setup.exe" },
        { "name": "sha256.txt",                          "browser_download_url": "https://github.com/x/sha256.txt" }
      ]
    }
    """;

    [Fact]
    public void PrefersTheSetupAndStillFindsATrailingChecksum()
    {
        var (download, checksum) = Select(RealOrder);
        Assert.Equal("https://github.com/x/setup.exe", download);
        Assert.Equal("https://github.com/x/sha256.txt", checksum);
    }

    [Fact]
    public void FindsTheChecksumWhenItComesFirst()
    {
        var (download, checksum) = Select("""
        {
          "assets": [
            { "name": "sha256.txt",                          "browser_download_url": "https://github.com/x/sha256.txt" },
            { "name": "Peripheral-Battery-Setup-0.3.8.exe",  "browser_download_url": "https://github.com/x/setup.exe" }
          ]
        }
        """);
        Assert.Equal("https://github.com/x/setup.exe", download);
        Assert.Equal("https://github.com/x/sha256.txt", checksum);
    }

    [Fact]
    public void AcceptsADotSha256Asset()
    {
        var (_, checksum) = Select("""
        { "assets": [ { "name": "Setup-0.3.8.exe.sha256", "browser_download_url": "https://github.com/x/s" } ] }
        """);
        Assert.Equal("https://github.com/x/s", checksum);
    }

    [Fact]
    public void PrefersTheCombinedChecksumOverPerFileOnes()
    {
        // Real v0.3.9 layout. The portable build's own .sha256 comes first and does not list the
        // Setup, so picking it made every update abort with a misleading "no checksum published".
        var (_, checksum) = Select("""
        {
          "assets": [
            { "name": "Peripheral-Battery-0.3.9-x64.exe",       "browser_download_url": "https://github.com/x/portable.exe" },
            { "name": "Peripheral-Battery-0.3.9-x64.sha256",    "browser_download_url": "https://github.com/x/portable.sha256" },
            { "name": "Peripheral-Battery-Setup-0.3.9.exe",     "browser_download_url": "https://github.com/x/setup.exe" },
            { "name": "Peripheral-Battery-Setup-0.3.9.sha256",  "browser_download_url": "https://github.com/x/setup.sha256" },
            { "name": "sha256.txt",                              "browser_download_url": "https://github.com/x/sha256.txt" }
          ]
        }
        """);
        Assert.Equal("https://github.com/x/sha256.txt", checksum);
    }

    [Fact]
    public void UsesTheSetupChecksumWhenThereIsNoCombinedFile()
    {
        var (_, checksum) = Select("""
        {
          "assets": [
            { "name": "Peripheral-Battery-0.3.9-x64.exe",      "browser_download_url": "https://github.com/x/portable.exe" },
            { "name": "Peripheral-Battery-0.3.9-x64.sha256",   "browser_download_url": "https://github.com/x/portable.sha256" },
            { "name": "Peripheral-Battery-Setup-0.3.9.exe",    "browser_download_url": "https://github.com/x/setup.exe" },
            { "name": "Peripheral-Battery-Setup-0.3.9.sha256", "browser_download_url": "https://github.com/x/setup.sha256" }
          ]
        }
        """);
        Assert.Equal("https://github.com/x/setup.sha256", checksum);
    }

    [Fact]
    public void DoesNotTreatAPortableExeAsAnInstaller()
    {
        // A portable build ignores /VERYSILENT and loses the single-instance race, so handing it
        // to the updater looked like "installing" while the app simply disappeared.
        var (download, checksum) = Select("""
        {
          "assets": [
            { "name": "Peripheral-Battery-0.3.8-x64.exe", "browser_download_url": "https://github.com/x/portable.exe" },
            { "name": "sha256.txt",                        "browser_download_url": "https://github.com/x/sha256.txt" }
          ]
        }
        """);
        Assert.Null(download);
        Assert.NotNull(checksum);
    }

    [Fact]
    public void DoesNotTreatAZipArchiveAsAnInstaller()
    {
        var (download, _) = Select("""
        { "assets": [ { "name": "build-0.3.8.zip", "browser_download_url": "https://github.com/x/b.zip" } ] }
        """);
        Assert.Null(download);
    }

    [Fact]
    public void TakesTheFirstSetupWhenTheReleaseShipsSeveral()
    {
        var (download, _) = Select("""
        {
          "assets": [
            { "name": "Peripheral-Battery-Setup-0.3.8-arm64.exe", "browser_download_url": "https://github.com/x/arm.exe" },
            { "name": "Peripheral-Battery-Setup-0.3.8.exe",       "browser_download_url": "https://github.com/x/x64.exe" }
          ]
        }
        """);
        Assert.Equal("https://github.com/x/arm.exe", download);
    }

    [Fact]
    public void ReturnsNothingWhenTheReleaseHasNoAssets()
    {
        var (download, checksum) = Select("""{ "tag_name": "v0.3.8" }""");
        Assert.Null(download);
        Assert.Null(checksum);
    }

    [Fact]
    public void IgnoresAssetsWithoutADownloadUrl()
    {
        var (download, _) = Select("""
        { "assets": [ { "name": "Peripheral-Battery-Setup-0.3.8.exe" } ] }
        """);
        Assert.Null(download);
    }
}
