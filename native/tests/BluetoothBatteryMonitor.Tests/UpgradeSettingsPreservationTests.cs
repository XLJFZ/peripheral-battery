extern alias MonitorApp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
using Settings = MonitorApp::BluetoothBatteryMonitor.App.Models.AppSettings;
using Installer = MonitorApp::BluetoothBatteryMonitor.App.Services.Update.UpdateInstaller;

public sealed class UpgradeSettingsPreservationTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "upgrade-settings-" + Guid.NewGuid().ToString("N"));
    private string Profile => Path.Combine(_folder, "settings.json");

    public UpgradeSettingsPreservationTests()
    {
        Directory.CreateDirectory(_folder);
        Settings.TestFolderOverride = _folder;
    }

    public void Dispose()
    {
        Settings.TestFolderOverride = null;
        Directory.Delete(_folder, true);
    }

    [Fact]
    public void EveryPersistedPropertyAndUnknownFieldsSurviveUpgradeAndSubsequentSave()
    {
        var settings = new Settings();
        // Cover every property automatically, so newly added settings join this contract too.
        foreach (var property in typeof(Settings).GetProperties().Where(p => p.CanWrite))
        {
            object value = property.PropertyType == typeof(bool) ? !(bool)property.GetValue(settings)!
                : property.PropertyType == typeof(int) ? 37
                : property.PropertyType == typeof(double) ? 11.5
                : property.PropertyType == typeof(string) ? "custom-" + property.Name
                : property.PropertyType == typeof(DateTime?) ? new DateTime(2026, 11, 4, 1, 2, 3, DateTimeKind.Utc)
                : property.PropertyType == typeof(List<string>) ? new List<string> { "device-b", "device-a" }
                : property.PropertyType == typeof(HashSet<string>) ? new HashSet<string> { "device-b", "device-a" }
                : property.PropertyType == typeof(Dictionary<string, int>) ? new Dictionary<string, int> { ["device-a"] = 35 }
                : property.PropertyType == typeof(Dictionary<string, string>) ? new Dictionary<string, string> { ["device-a"] = "我的鼠标" }
                : property.PropertyType == typeof(Dictionary<string, JsonElement>) ?
                    JsonSerializer.Deserialize<Dictionary<string, JsonElement>>("""{"FutureOption":{"enabled":true,"values":[1,"x",null]}}""")!
                : throw new Exception("Add a preservation fixture for " + property.Name);
            property.SetValue(settings, value);
        }
        string original = JsonSerializer.Serialize(settings);
        File.WriteAllText(Profile, original);

        Settings.Load().Save();

        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(original), JsonNode.Parse(File.ReadAllText(Profile))),
            "Every persisted preference must remain semantically identical after loading and saving.");
    }

    [Fact]
    public void LegacyGlassChoiceSurvivesAndSnapshotContainsOriginalBytes()
    {
        const string original = "{\"Theme\":\"液态玻璃\",\"ThemeVersion\":0,\"LowBatteryThreshold\":33}";
        File.WriteAllText(Profile, original);
        var loaded = Settings.Load();
        Assert.Equal("液态玻璃", loaded.Theme);
        loaded.Save();
        Assert.Equal("液态玻璃", Settings.Load().Theme);
        var version = typeof(Settings).Assembly.GetName().Version!;
        Assert.Equal(original, File.ReadAllText(Path.Combine(_folder, "upgrade-backups",
            $"before-{version.Major}.{version.Minor}.{version.Build}", "settings.json")));
    }

    [Fact]
    public void TargetSnapshotIsNotRotatedByLaterSavesOrRepeatedUpgradeAttempts()
    {
        const string original = "{\"LowBatteryThreshold\":33}";
        File.WriteAllText(Profile, original);
        File.WriteAllText(Profile + ".bak", "{\"LowBatteryThreshold\":32}");
        Settings.CreateUpgradeSnapshot("v9.8.7");
        var loaded = Settings.Load();
        loaded.LowBatteryThreshold = 44;
        loaded.Save();
        loaded.Save();
        Settings.CreateUpgradeSnapshot("9.8.7");
        string folder = Path.Combine(_folder, "upgrade-backups", "before-9.8.7");
        Assert.Equal(original, File.ReadAllText(Path.Combine(folder, "settings.json")));
        Assert.Equal("{\"LowBatteryThreshold\":32}", File.ReadAllText(Path.Combine(folder, "settings.json.bak")));
    }

    [Fact]
    public void SnapshotFailureLeavesExistingProfileUntouched()
    {
        const string original = "{\"LowBatteryThreshold\":33}";
        File.WriteAllText(Profile, original);
        File.WriteAllText(Path.Combine(_folder, "upgrade-backups"), "blocking file");
        var settings = Settings.Load();
        settings.LowBatteryThreshold = 44;
        settings.Save();
        Assert.Equal(original, File.ReadAllText(Profile));
        Assert.ThrowsAny<IOException>(() => Settings.CreateUpgradeSnapshot("9.8.7"));
    }

    [Fact]
    public void UnreadableProfileIsNotOverwrittenByFallbackDefaults()
    {
        const string original = "{\"LowBatteryThreshold\":{\"futureFormat\":35}}";
        File.WriteAllText(Profile, original);
        var settings = Settings.Load();
        settings.AutoUpdateLastCheckUtc = DateTime.UtcNow;
        settings.Save();
        Assert.Equal(original, File.ReadAllText(Profile));
    }

    [Fact]
    public void NewProfileDoesNotCreateAnEmptySnapshot()
    {
        Settings.Load();
        Assert.False(Directory.Exists(Path.Combine(_folder, "upgrade-backups")));
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("")]
    public void SnapshotRejectsInvalidTargetVersions(string version)
        => Assert.Throws<ArgumentException>(() => Settings.CreateUpgradeSnapshot(version));

    [Theory]
    [InlineData(true, "/TASKS=autostart")]
    [InlineData(false, "/TASKS=\"\"")]
    public void UpgradeArgumentsPreserveActualStartupState(bool enabled, string expected)
        => Assert.Contains(expected, Installer.BuildArguments(enabled));
}
