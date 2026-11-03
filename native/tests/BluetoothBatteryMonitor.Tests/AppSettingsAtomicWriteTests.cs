extern alias MonitorApp;
using System;
using System.IO;
using Xunit;
using AppSettingsModel = MonitorApp::BluetoothBatteryMonitor.App.Models.AppSettings;

public class AppSettingsAtomicWriteTests : IDisposable
{
    private readonly string _folder;

    public AppSettingsAtomicWriteTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), "peripheral-settings-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
        AppSettingsModel.TestFolderOverride = _folder;
    }

    public void Dispose()
    {
        AppSettingsModel.TestFolderOverride = null;
        try { Directory.Delete(_folder, recursive: true); } catch { }
    }

    private string SettingsPath => Path.Combine(_folder, "settings.json");

    [Fact]
    public void Save_LeavesNoTempFileAndKeepsBackup()
    {
        // First save moves the temp file into place (no previous file to back up);
        // a second save goes through File.Replace, which keeps the old content as .bak.
        new AppSettingsModel { LowBatteryThreshold = 25 }.Save();
        new AppSettingsModel { LowBatteryThreshold = 25 }.Save();

        Assert.True(File.Exists(SettingsPath));
        Assert.False(File.Exists(SettingsPath + ".tmp"));
        Assert.True(File.Exists(SettingsPath + ".bak"));

        var loaded = AppSettingsModel.Load();
        Assert.Equal(25, loaded.LowBatteryThreshold);
    }

    [Fact]
    public void Save_Twice_BackupHoldsPreviousContent()
    {
        new AppSettingsModel { LowBatteryThreshold = 25 }.Save();
        new AppSettingsModel { LowBatteryThreshold = 35 }.Save();

        var backupJson = File.ReadAllText(SettingsPath + ".bak");
        Assert.Contains(": 25", backupJson);

        var loaded = AppSettingsModel.Load();
        Assert.Equal(35, loaded.LowBatteryThreshold);
    }

    [Fact]
    public void Load_CorruptPrimary_ArchivesItAndRecoversFromBackup()
    {
        new AppSettingsModel { LowBatteryThreshold = 25 }.Save();
        new AppSettingsModel { LowBatteryThreshold = 25 }.Save(); // second save produces the .bak

        // Simulate a half-written file (crash mid-write).
        File.WriteAllText(SettingsPath, "{ \"LowBatteryThreshold\": 3");

        var loaded = AppSettingsModel.Load();
        Assert.Equal(25, loaded.LowBatteryThreshold);
        Assert.True(File.Exists(SettingsPath + ".corrupt"));
    }

    [Fact]
    public void Load_CorruptWithoutBackup_FallsBackToDefaults()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(SettingsPath, "not json at all");

        var loaded = AppSettingsModel.Load();
        Assert.Equal(20, loaded.LowBatteryThreshold); // default
        Assert.True(File.Exists(SettingsPath + ".corrupt"));
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var loaded = AppSettingsModel.Load();
        Assert.False(File.Exists(SettingsPath + ".corrupt"));
        Assert.Equal(20, loaded.LowBatteryThreshold);
    }

    [Fact]
    public void Save_CorruptExistingFile_ReplacesItAtomically()
    {
        File.WriteAllText(SettingsPath, "{ half-written garbage");
        new AppSettingsModel { LowBatteryThreshold = 30 }.Save();

        var loaded = AppSettingsModel.Load();
        Assert.Equal(30, loaded.LowBatteryThreshold);
        Assert.False(File.Exists(SettingsPath + ".tmp"));
    }
}
