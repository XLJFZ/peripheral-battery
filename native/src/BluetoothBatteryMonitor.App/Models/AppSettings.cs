using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BluetoothBatteryMonitor.App.Models;

public class AppSettings
{
    private bool _loadedFromUnreadableProfile;
    // Keep fields written by newer versions when this version saves the profile.
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalSettings { get; set; }

    public List<string> DeviceOrder { get; set; } = new();
    public bool EnableGamePolling { get; set; }
    public int DesktopPollingHz { get; set; } = 500;
    public int GamePollingHz { get; set; } = 1000;
    public string PollingGames { get; set; } = "cs2,VALORANT-Win64-Shipping,r5apex";
    public int DeviceRank(string id) { int n=(DeviceOrder ?? new()).FindIndex(x=>string.Equals(x,id,StringComparison.OrdinalIgnoreCase)); return n<0?int.MaxValue:n; }
    public bool TrayAutoColor { get; set; } = true;
    public string TrayHighColor { get; set; } = "#00C98D";
    public string TrayMidColor { get; set; } = "#FFC048";
    public string TrayLowColor { get; set; } = "#FF6577";
    public string TrayFixedColor { get; set; } = "#61B8FF";
    public string TrayIconStyle { get; set; } = "圆环";
    public double TrayRingWidth { get; set; } = 10;
    /// <summary>Show one tray icon per connected device (max 4) in addition to the aggregate icon.</summary>
    public bool TrayPerDeviceIcons { get; set; }
    /// <summary>Animate the battery ring with a breathing pulse while a device is charging.</summary>
    public bool TrayChargingPulse { get; set; } = true;
    public bool LaunchAtStartup { get; set; } = false;
    public string AutoUpdateCheckFrequency { get; set; } = "weekly";
    public DateTime? AutoUpdateLastCheckUtc { get; set; }
    public bool EnableNotifications { get; set; } = true;
    public int LowBatteryThreshold { get; set; } = 20;
    public int CriticalBatteryThreshold { get; set; } = 10;
    public int RefreshIntervalSeconds { get; set; } = 60;
    public bool ShowBatteryPercentageInTray { get; set; } = true;
    public bool AutoSwitchAudioEndpoint { get; set; } = false;
    public bool EnableRestApi { get; set; } = false;
    public int RestApiPort { get; set; } = 23253;
    public string Theme { get; set; } = "普通版";
    /// <summary>Panel size profile: 紧凑 / 标准 / 宽松.</summary>
    public string FlyoutDensity { get; set; } = "标准";
    /// <summary>
    /// Seconds of no input before the open flyout hides itself. 0 keeps it open until the user
    /// clicks elsewhere or presses Escape.
    /// </summary>
    public int FlyoutAutoHideSeconds { get; set; } = 5;
    /// <summary>Appearance schema marker. Files written before v0.3.3 have no value (0).</summary>
    public int ThemeVersion { get; set; }
    public const int CurrentThemeVersion = 1;
    public string Language { get; set; } = "zh"; // "en", "tr"

    public Dictionary<string, int> DeviceSpecificThresholds { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> DeviceNames { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>User-assigned display names, keyed by device id or MAC hex. Removing a key restores the raw name.</summary>
    public Dictionary<string, string> CustomDeviceNames { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Hidden device keys (device id or MAC hex). Hidden devices are excluded from the flyout, tray and notifications.</summary>
    public HashSet<string> HiddenDevices { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Last known display name per hidden device id, so the settings page can list them.</summary>
    public Dictionary<string, string> HiddenDeviceNames { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Hide devices that report no battery level at all.</summary>
    public bool HideUnknownBatteryDevices { get; set; }

    /// <summary>
    /// Returns device-specific custom threshold if defined, otherwise falls back to global LowBatteryThreshold.
    /// </summary>
    public int GetEffectiveLowBatteryThreshold(string? deviceId, ulong macAddress = 0)
    {
        if (!string.IsNullOrWhiteSpace(deviceId))
        {
            if (DeviceSpecificThresholds.TryGetValue(deviceId, out int customThreshold))
            {
                return customThreshold;
            }

            ulong mac = macAddress != 0 ? macAddress : BluetoothDeviceModel.ExtractMacAddress(deviceId);
            if (mac != 0)
            {
                string macHex = mac.ToString("X12");
                if (DeviceSpecificThresholds.TryGetValue(macHex, out int macThreshold))
                {
                    return macThreshold;
                }
            }
        }
        else if (macAddress != 0)
        {
            string macHex = macAddress.ToString("X12");
            if (DeviceSpecificThresholds.TryGetValue(macHex, out int macThreshold))
            {
                return macThreshold;
            }
        }

        return LowBatteryThreshold;
    }

    public bool HasCustomThreshold(string? deviceId, ulong macAddress = 0)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) return false;
        if (DeviceSpecificThresholds.ContainsKey(deviceId)) return true;

        ulong mac = macAddress != 0 ? macAddress : BluetoothDeviceModel.ExtractMacAddress(deviceId);
        if (mac != 0)
        {
            string macHex = mac.ToString("X12");
            if (DeviceSpecificThresholds.ContainsKey(macHex)) return true;
        }

        return false;
    }

    public void SetDeviceThreshold(string deviceId, int? threshold, ulong macAddress = 0, string? deviceName = null)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) return;

        ulong mac = macAddress != 0 ? macAddress : BluetoothDeviceModel.ExtractMacAddress(deviceId);
        string macHex = mac != 0 ? mac.ToString("X12") : string.Empty;

        if (threshold.HasValue && threshold.Value > 0)
        {
            DeviceSpecificThresholds[deviceId] = threshold.Value;
            if (!string.IsNullOrEmpty(macHex))
            {
                DeviceSpecificThresholds[macHex] = threshold.Value;
            }

            if (!string.IsNullOrWhiteSpace(deviceName))
            {
                DeviceNames[deviceId] = deviceName;
                if (!string.IsNullOrEmpty(macHex))
                {
                    DeviceNames[macHex] = deviceName;
                }
            }
        }
        else
        {
            DeviceSpecificThresholds.Remove(deviceId);
            if (!string.IsNullOrEmpty(macHex))
            {
                DeviceSpecificThresholds.Remove(macHex);
            }
        }
        Save();
    }

    private static readonly string DefaultSettingsFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PeripheralBatteryNative");

    /// <summary>Test-only redirect so tests never touch the real %APPDATA% profile.</summary>
    internal static string? TestFolderOverride;

    private static string SettingsFolder => TestFolderOverride ?? DefaultSettingsFolder;

    private static string SettingsFilePath => Path.Combine(SettingsFolder, "settings.json");

    /// <summary>
    /// An immutable, byte-for-byte copy before this version can migrate or save the profile.
    /// Also called with the target version before launching an in-app upgrade. Unlike .bak,
    /// these copies are never rotated by ordinary saves. Failure must prevent a destructive save.
    /// </summary>
    public static void CreateUpgradeSnapshot(string targetVersion)
    {
        if (!Version.TryParse(targetVersion.TrimStart('v', 'V'), out var version))
            throw new ArgumentException("Invalid upgrade version", nameof(targetVersion));
        var folder = Path.Combine(SettingsFolder, "upgrade-backups", "before-" + version);
        foreach (var suffix in new[] { "", BackupSuffix })
        {
            var source = SettingsFilePath + suffix;
            var destination = Path.Combine(folder, "settings.json" + suffix);
            if (!File.Exists(source) || File.Exists(destination)) continue;
            Directory.CreateDirectory(folder);
            // A unique temporary file avoids leaving a partial snapshot after an interrupted copy.
            var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.Copy(source, temporary);
                try { File.Move(temporary, destination); }
                catch (IOException) when (File.Exists(destination)) { /* Another caller won. */ }
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
    }

    private static void SnapshotForCurrentVersion()
    {
        var version = typeof(AppSettings).Assembly.GetName().Version!;
        CreateUpgradeSnapshot($"{version.Major}.{version.Minor}.{version.Build}");
    }

    /// <summary>
    /// Last-write time (UTC) of settings.json, or DateTime.MinValue when it does not exist yet.
    /// Lets hot-path callers (e.g. the flyout opening on every tray click) cache a loaded
    /// instance and only pay a re-read when the file actually changed.
    /// </summary>
    public static DateTime GetSettingsStampUtc()
        => File.Exists(SettingsFilePath) ? File.GetLastWriteTimeUtc(SettingsFilePath) : DateTime.MinValue;

    public static AppSettings Load()
    {
        // Reading remains available on read-only profiles. Save retries and refuses to overwrite
        // the profile if its pre-migration snapshot still cannot be created.
        try { SnapshotForCurrentVersion(); } catch { }
        // Primary file. A parse failure here means the file is corrupt (e.g. half-written by a
        // crash); archive it for forensics before anything overwrites it, then fall back to the
        // .bak copy that every successful Save leaves behind, and only then to defaults.
        if (TryReadSettings(SettingsFilePath, out var settings))
        {
            return settings!;
        }

        ArchiveCorruptSettings();

        if (TryReadSettings(SettingsFilePath + BackupSuffix, out settings))
        {
            return settings!;
        }

        // Keep the application usable, but never persist fallback defaults over an existing
        // profile that this version could not understand (including a future schema).
        return new AppSettings
        {
            _loadedFromUnreadableProfile = File.Exists(SettingsFilePath) || File.Exists(SettingsFilePath + BackupSuffix)
        };
    }

    private const string BackupSuffix = ".bak";
    private const string CorruptSuffix = ".corrupt";

    private static bool TryReadSettings(string path, out AppSettings? settings)
    {
        settings = null;
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            var json = File.ReadAllText(path);
            var parsed = JsonSerializer.Deserialize<AppSettings>(json);
            if (parsed == null)
            {
                return false;
            }

            if (parsed.DeviceSpecificThresholds != null)
            {
                parsed.DeviceSpecificThresholds = new Dictionary<string, int>(parsed.DeviceSpecificThresholds, StringComparer.OrdinalIgnoreCase);
            }
            if (parsed.DeviceNames != null)
            {
                parsed.DeviceNames = new Dictionary<string, string>(parsed.DeviceNames, StringComparer.OrdinalIgnoreCase);
            }
            if (parsed.CustomDeviceNames != null)
            {
                parsed.CustomDeviceNames = new Dictionary<string, string>(parsed.CustomDeviceNames, StringComparer.OrdinalIgnoreCase);
            }
            if (parsed.HiddenDevices != null)
            {
                parsed.HiddenDevices = new HashSet<string>(parsed.HiddenDevices, StringComparer.OrdinalIgnoreCase);
            }
            if (parsed.HiddenDeviceNames != null)
            {
                parsed.HiddenDeviceNames = new Dictionary<string, string>(parsed.HiddenDeviceNames, StringComparer.OrdinalIgnoreCase);
            }
            parsed.MigrateAppearance();
            settings = parsed;
            return true;
        }
        catch
        {
            // Caller decides how to recover (backup file / defaults).
            return false;
        }
    }

    private static void ArchiveCorruptSettings()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                File.Copy(SettingsFilePath, SettingsFilePath + CorruptSuffix, overwrite: true);
            }
        }
        catch
        {
            // Best effort; recovery continues with the backup or defaults.
        }
    }

    public static event EventHandler<AppSettings>? SettingsChanged;

    /// <summary>
    /// Preserve supported user choices regardless of the version that wrote them.
    /// Legacy aliases are mapped to their current equivalent; unknown values survive on disk.
    /// </summary>
    public static string MigrateTheme(string? theme, int themeVersion)
        => string.IsNullOrWhiteSpace(theme) ? Services.System.ThemeManager.AppearanceNormal
            : theme == "液态玻璃（低透明）"
                ? Services.System.ThemeManager.AppearanceGlass : theme;

    private void MigrateAppearance()
    {
        Theme = MigrateTheme(Theme, ThemeVersion);
        // Consumers apply safe display fallbacks without rewriting future/unknown preferences.
        ThemeVersion = Math.Max(ThemeVersion, CurrentThemeVersion);
    }

    /// <summary>Unknown or out-of-range values fall back to 5 s; only known steps survive.</summary>
    public static int NormalizeAutoHide(int seconds)
        => seconds is 0 or 3 or 5 or 10 or 15 or 30 or 60 ? seconds : 5;

    /// <summary>
    /// Custom display name for a device, matching by device id first and MAC hex second
    /// (same dual-key scheme as DeviceSpecificThresholds). Null when no rename is stored.
    /// </summary>
    public string? GetCustomName(string? deviceId, ulong macAddress = 0)
    {
        if (!string.IsNullOrWhiteSpace(deviceId) && CustomDeviceNames.TryGetValue(deviceId, out var byId))
        {
            return byId;
        }

        ulong mac = macAddress != 0 ? macAddress : (deviceId != null ? BluetoothDeviceModel.ExtractMacAddress(deviceId) : 0);
        if (mac != 0 && CustomDeviceNames.TryGetValue(mac.ToString("X12"), out var byMac))
        {
            return byMac;
        }

        return null;
    }

    /// <summary>Sets (or clears when <paramref name="name"/> is empty) the custom display name and persists.</summary>
    public void SetCustomName(string deviceId, ulong macAddress, string? name)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) return;
        string macHex = macAddress != 0 ? macAddress.ToString("X12") : BluetoothDeviceModel.ExtractMacAddress(deviceId).ToString("X12");

        if (string.IsNullOrWhiteSpace(name))
        {
            CustomDeviceNames.Remove(deviceId);
            CustomDeviceNames.Remove(macHex);
        }
        else
        {
            CustomDeviceNames[deviceId] = name.Trim();
            if (!string.IsNullOrEmpty(macHex))
            {
                CustomDeviceNames[macHex] = name!.Trim();
            }
        }
        Save();
    }

    public bool IsHidden(string? deviceId, ulong macAddress = 0)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) return false;
        if (HiddenDevices.Contains(deviceId)) return true;

        ulong mac = macAddress != 0 ? macAddress : BluetoothDeviceModel.ExtractMacAddress(deviceId);
        return mac != 0 && HiddenDevices.Contains(mac.ToString("X12"));
    }

    /// <summary>Hides (or unhides) a device, remembering its display name for the settings list, and persists.</summary>
    public void SetHidden(string deviceId, ulong macAddress, bool hidden, string? displayName = null)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) return;
        string macHex = macAddress != 0 ? macAddress.ToString("X12") : BluetoothDeviceModel.ExtractMacAddress(deviceId).ToString("X12");

        if (hidden)
        {
            HiddenDevices.Add(deviceId);
            if (!string.IsNullOrEmpty(macHex)) HiddenDevices.Add(macHex);
            if (!string.IsNullOrWhiteSpace(displayName)) HiddenDeviceNames[deviceId] = displayName!;
        }
        else
        {
            HiddenDevices.Remove(deviceId);
            HiddenDevices.Remove(macHex);
            HiddenDeviceNames.Remove(deviceId);
        }
        Save();
    }

    public void Save()
    {
        if (_loadedFromUnreadableProfile) return;
        ThemeVersion = Math.Max(ThemeVersion, CurrentThemeVersion);
        try
        {
            SnapshotForCurrentVersion();
            if (!Directory.Exists(SettingsFolder))
            {
                Directory.CreateDirectory(SettingsFolder);
            }

            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });

            // Atomic write: serialize to a temp file in the same directory, then swap it in with
            // File.Replace (which also keeps the previous contents as .bak). A crash or power cut
            // mid-write can no longer leave a half-written settings.json behind.
            var tempPath = SettingsFilePath + ".tmp";
            var backupPath = SettingsFilePath + BackupSuffix;
            File.WriteAllText(tempPath, json);

            if (File.Exists(SettingsFilePath))
            {
                File.Replace(tempPath, SettingsFilePath, backupPath, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tempPath, SettingsFilePath);
            }

            SettingsChanged?.Invoke(null, this);
        }
        catch
        {
            // Hata sessizce yutulabilir veya loglanabilir
        }
    }
}
