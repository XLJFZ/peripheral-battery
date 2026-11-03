extern alias MonitorApp;
using System;
using System.IO;
using Xunit;
using UpdateInstaller = MonitorApp::BluetoothBatteryMonitor.App.Services.Update.UpdateInstaller;
using UpdateStage = MonitorApp::BluetoothBatteryMonitor.App.Services.Update.UpdateStage;
using UpdateProgress = MonitorApp::BluetoothBatteryMonitor.App.Services.Update.UpdateProgress;
using UpdateInstallResult = MonitorApp::BluetoothBatteryMonitor.App.Services.Update.UpdateInstallResult;

public class UpdateInstallerTests
{
    private const string HashA = "30fb5fa04b25addd0ed59155977f3cfd143075126a0476c6b3e87c79be61190c";
    private const string HashB = "16533a8e8c3555dfeca50919283918d0e3a1f1a2dc8269baec5ee593d8bbe2c9";

    private const string ChecksumFile =
        HashB + " *Peripheral-Battery-0.3.7-x64.exe\n" +
        HashA + " *Peripheral-Battery-Setup-0.3.7.exe\n";

    [Fact]
    public void ParseChecksum_PicksTheSetupLine()
    {
        Assert.Equal(HashA, UpdateInstaller.ParseChecksum(ChecksumFile, "Peripheral-Battery-Setup-0.3.7.exe"));
    }

    [Fact]
    public void ParseChecksum_PicksThePortableLine()
    {
        Assert.Equal(HashB, UpdateInstaller.ParseChecksum(ChecksumFile, "Peripheral-Battery-0.3.7-x64.exe"));
    }

    [Fact]
    public void ParseChecksum_ReturnsNullWhenTheFileIsNotListed()
    {
        Assert.Null(UpdateInstaller.ParseChecksum(ChecksumFile, "Peripheral-Battery-Setup-9.9.9.exe"));
    }

    [Fact]
    public void ParseChecksum_AcceptsABareSingleLineHash()
    {
        Assert.Equal(HashA, UpdateInstaller.ParseChecksum(HashA + "\n", "anything.exe"));
    }

    [Fact]
    public void ParseChecksum_IgnoresLinesThatAreNotHex()
    {
        string text = new string('z', 64) + " *Peripheral-Battery-Setup-0.3.7.exe\n" +
                      HashA + " *Peripheral-Battery-Setup-0.3.7.exe\n";
        Assert.Equal(HashA, UpdateInstaller.ParseChecksum(text, "Peripheral-Battery-Setup-0.3.7.exe"));
    }

    [Fact]
    public void ParseChecksum_ToleratesAUtf8BomAndCrlf()
    {
        string text = "\uFEFF" + HashA + "  Peripheral-Battery-Setup-0.3.7.exe\r\n";
        Assert.Equal(HashA, UpdateInstaller.ParseChecksum(text, "Peripheral-Battery-Setup-0.3.7.exe"));
    }

    [Fact]
    public void ParseChecksum_RequiresAnExactFileNameSuffix()
    {
        // "Setup.exe" must not match "...-Setup-0.3.7-extra.exe".
        string text = HashA + " *Peripheral-Battery-Setup-0.3.7.exe\n";
        Assert.Null(UpdateInstaller.ParseChecksum(text, "Setup-0.3.7-extra.exe"));
    }

    [Theory]
    [InlineData("https://github.com/XLJFZ/peripheral-battery/releases/download/v0.3.8/x.exe", true)]
    [InlineData("https://objects.githubusercontent.com/abc/x.exe", true)]
    [InlineData("https://github-releases.githubusercontent.com/abc/x.exe", true)]
    [InlineData("http://github.com/XLJFZ/x.exe", false)]
    [InlineData("https://github.com.evil.example/x.exe", false)]
    [InlineData("https://evil.example/github.com/x.exe", false)]
    [InlineData("file:///C:/x.exe", false)]
    [InlineData("", false)]
    public void IsAllowed_OnlyAcceptsHttpsOnGitHubHosts(string url, bool expected)
    {
        Assert.Equal(expected, UpdateInstaller.IsAllowed(url));
    }

    [Fact]
    public void IsInstalledCopy_DoesNotThrow()
    {
        // Whatever the hosting folder is, the probe must answer instead of throwing.
        _ = UpdateInstaller.IsInstalledCopy();
    }

    [Theory]
    [InlineData(UpdateStage.Downloading, 0)]
    [InlineData(UpdateStage.Downloading, 100)]
    [InlineData(UpdateStage.Verifying, 100)]
    [InlineData(UpdateStage.Launching, 100)]
    public void UpdateProgress_CarriesStageAndPercent(UpdateStage stage, int percent)
    {
        var progress = new UpdateProgress(stage, percent);
        Assert.Equal(stage, progress.Stage);
        Assert.Equal(percent, progress.Percent);
    }

    [Fact]
    public void UpdateInstallResult_DefaultsToNoErrorCode()
    {
        var result = new UpdateInstallResult(true, InstallerPath: @"C:\x\setup.exe");
        Assert.True(result.Success);
        Assert.Null(result.ErrorCode);
        Assert.Equal(@"C:\x\setup.exe", result.InstallerPath);
    }

    [Theory]
    [InlineData("https://github.com/x/Peripheral-Battery-Setup-0.3.9.exe", "Peripheral-Battery-Setup-0.3.9.exe")]
    [InlineData("https://github.com/x/Peripheral%20Battery%20Setup%200.3.9.exe", "Peripheral Battery Setup 0.3.9.exe")]
    [InlineData("https://github.com/x/%E5%A4%96%E8%AE%BE-Setup.exe", "外设-Setup.exe")]
    public void LocalFileName_DecodesPercentEncodedAssets(string url, string expected)
    {
        // The name has to match the sha256.txt entry, which holds the decoded asset name.
        Assert.Equal(expected, UpdateInstaller.LocalFileName(url));
    }

    [Theory]
    [InlineData("Peripheral-Battery-Setup-0.3.9.exe", true)]
    [InlineData("peripheral-battery-setup-0.3.9.EXE", true)]
    [InlineData("Peripheral-Battery-0.3.9-x64.exe", false)]
    [InlineData("build-0.3.9.zip", false)]
    [InlineData("Setup", false)]
    [InlineData("", false)]
    public void IsInstallerFileName_RejectsAnythingButASetup(string fileName, bool expected)
    {
        Assert.Equal(expected, UpdateInstaller.IsInstallerFileName(fileName));
    }

    [Fact]
    public void PruneStaleDownloads_DropsPartialFilesAndOldInstallers()
    {
        string dir = Path.Combine(Path.GetTempPath(), "pb-prune-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string stale = Path.Combine(dir, "Peripheral-Battery-Setup-0.3.7.exe");
            string fresh = Path.Combine(dir, "Peripheral-Battery-Setup-0.3.9.exe");
            string partial = Path.Combine(dir, "Peripheral-Battery-Setup-0.3.9.exe.part");
            File.WriteAllText(stale, "old");
            File.WriteAllText(fresh, "new");
            File.WriteAllText(partial, "half");

            var now = DateTime.UtcNow;
            File.SetLastWriteTimeUtc(stale, now.AddDays(-10));
            File.SetLastWriteTimeUtc(fresh, now);

            int removed = UpdateInstaller.PruneStaleDownloads(TimeSpan.FromDays(2), now, dir);

            Assert.Equal(2, removed);
            Assert.False(File.Exists(stale));
            Assert.False(File.Exists(partial));
            Assert.True(File.Exists(fresh));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
