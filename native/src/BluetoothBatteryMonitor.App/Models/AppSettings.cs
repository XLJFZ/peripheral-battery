using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace BluetoothBatteryMonitor.App.Models;

public class AppSettings
{
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
    public bool LaunchAtStartup { get; set; } = false;
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

    private static readonly string SettingsFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PeripheralBatteryNative");

    private static readonly string SettingsFilePath = Path.Combine(SettingsFolder, "settings.json");

    /// <summary>
    /// Last-write time (UTC) of settings.json, or DateTime.MinValue when it does not exist yet.
    /// Lets hot-path callers (e.g. the flyout opening on every tray click) cache a loaded
    /// instance and only pay a re-read when the file actually changed.
    /// </summary>
    public static DateTime GetSettingsStampUtc()
        => File.Exists(SettingsFilePath) ? File.GetLastWriteTimeUtc(SettingsFilePath) : DateTime.MinValue;

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                var json = File.ReadAllText(SettingsFilePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                if (settings != null)
                {
                    if (settings.DeviceSpecificThresholds != null)
                    {
                        settings.DeviceSpecificThresholds = new Dictionary<string, int>(settings.DeviceSpecificThresholds, StringComparer.OrdinalIgnoreCase);
                    }
                    if (settings.DeviceNames != null)
                    {
                        settings.DeviceNames = new Dictionary<string, string>(settings.DeviceNames, StringComparer.OrdinalIgnoreCase);
                    }
                    settings.MigrateAppearance();
                    return settings;
                }
            }
        }
        catch
        {
            // Fallback to default settings on deserialization error
        }

        return new AppSettings();
    }

    public static event EventHandler<AppSettings>? SettingsChanged;

    /// <summary>
    /// Normalises the appearance value and once-per-profile returns profiles written by
    /// v0.3.0/v0.3.1 (System / Dark / Light / 液态玻璃) to the 普通版 default. Only an
    /// explicit choice saved from v0.3.3 onwards keeps 液态玻璃.
    /// </summary>
    public static string MigrateTheme(string? theme, int themeVersion)
        => themeVersion < CurrentThemeVersion
            ? Services.System.ThemeManager.AppearanceNormal
            : Services.System.ThemeManager.Normalize(theme);

    private void MigrateAppearance()
    {
        Theme = MigrateTheme(Theme, ThemeVersion);
        FlyoutDensity = Services.System.FlyoutDensity.Normalize(FlyoutDensity);
        FlyoutAutoHideSeconds = NormalizeAutoHide(FlyoutAutoHideSeconds);
        ThemeVersion = CurrentThemeVersion;
    }

    /// <summary>Unknown or out-of-range values fall back to 5 s; only known steps survive.</summary>
    public static int NormalizeAutoHide(int seconds)
        => seconds is 0 or 3 or 5 or 10 or 15 or 30 or 60 ? seconds : 5;

    public void Save()
    {
        Theme = Services.System.ThemeManager.Normalize(Theme);
        ThemeVersion = CurrentThemeVersion;
        try
        {
            if (!Directory.Exists(SettingsFolder))
            {
                Directory.CreateDirectory(SettingsFolder);
            }

            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsFilePath, json);

            SettingsChanged?.Invoke(null, this);
        }
        catch
        {
            // Hata sessizce yutulabilir veya loglanabilir
        }
    }
}
