extern alias MonitorApp;
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using AppSettingsModel = MonitorApp::BluetoothBatteryMonitor.App.Models.AppSettings;
using BluetoothDeviceModel = MonitorApp::BluetoothBatteryMonitor.App.Models.BluetoothDeviceModel;
using ToastNotificationService = MonitorApp::BluetoothBatteryMonitor.App.Services.Notification.ToastNotificationService;

public class DeviceVisibilityTests : IDisposable
{
    private readonly string _folder;

    public DeviceVisibilityTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), "peripheral-visibility-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
        AppSettingsModel.TestFolderOverride = _folder;
    }

    public void Dispose()
    {
        AppSettingsModel.TestFolderOverride = null;
        try { Directory.Delete(_folder, recursive: true); } catch { }
    }

    private static BluetoothDeviceModel Device(string id, string name, ulong mac = 0xAABBCCDDEEFF, int? battery = 80)
        => new() { Id = id, Name = name, BluetoothAddress = mac, BatteryLevel = battery, IsConnected = true };

    // ---------------------------------------------------------------- renames

    [Fact]
    public void SetCustomName_ThenGetCustomName_MatchesByIdAndMac()
    {
        var settings = new AppSettingsModel();
        settings.SetCustomName("DEV1", 0xAABBCCDDEEFF, "我的鼠标");

        Assert.Equal("我的鼠标", settings.GetCustomName("DEV1"));
        Assert.Equal("我的鼠标", settings.GetCustomName("DEV1", 0xAABBCCDDEEFF));
        Assert.Equal("我的鼠标", settings.GetCustomName("SOME_OTHER_ID", 0xAABBCCDDEEFF));
        Assert.Null(settings.GetCustomName("DEV2"));
    }

    [Fact]
    public void SetCustomName_Empty_RemovesOverrideAndPersistsRemoval()
    {
        var settings = new AppSettingsModel();
        settings.SetCustomName("DEV1", 0xAABBCCDDEEFF, "Old name");
        Assert.NotNull(settings.GetCustomName("DEV1"));

        settings.SetCustomName("DEV1", 0xAABBCCDDEEFF, "");
        Assert.Null(settings.GetCustomName("DEV1"));

        var reloaded = AppSettingsModel.Load();
        Assert.Null(reloaded.GetCustomName("DEV1"));
    }

    [Fact]
    public void Rename_PersistsAcrossReload()
    {
        var settings = new AppSettingsModel();
        settings.SetCustomName("DEV1", 0xAABBCCDDEEFF, "Custom name");

        var reloaded = AppSettingsModel.Load();
        Assert.Equal("Custom name", reloaded.GetCustomName("DEV1", 0xAABBCCDDEEFF));
    }

    [Fact]
    public void Model_DisplayName_PrefersCustomNameOverRaw()
    {
        var dev = Device("DEV1", "Raw Name");
        Assert.Equal("Raw Name", dev.DisplayName);

        dev.CustomName = "Renamed";
        Assert.Equal("Renamed", dev.DisplayName);

        dev.CustomName = null;
        Assert.Equal("Raw Name", dev.DisplayName);
    }

    // ---------------------------------------------------------------- hidden devices

    [Fact]
    public void SetHidden_MatchesByIdAndMac()
    {
        var settings = new AppSettingsModel();
        settings.SetHidden("DEV1", 0xAABBCCDDEEFF, true, "My Headset");

        Assert.True(settings.IsHidden("DEV1"));
        Assert.True(settings.IsHidden("DEV1", 0xAABBCCDDEEFF));
        Assert.True(settings.IsHidden("OTHER_ID", 0xAABBCCDDEEFF));
        Assert.False(settings.IsHidden("DEV2"));
        Assert.Equal("My Headset", settings.HiddenDeviceNames["DEV1"]);
    }

    [Fact]
    public void Unhide_RemovesBothKeyForms()
    {
        var settings = new AppSettingsModel();
        settings.SetHidden("DEV1", 0xAABBCCDDEEFF, true, "X");
        settings.SetHidden("DEV1", 0xAABBCCDDEEFF, false);

        Assert.False(settings.IsHidden("DEV1"));
        Assert.False(settings.IsHidden("OTHER", 0xAABBCCDDEEFF));
        Assert.False(settings.HiddenDeviceNames.ContainsKey("DEV1"));
    }

    [Fact]
    public void Hidden_PersistsAcrossReload()
    {
        var settings = new AppSettingsModel();
        settings.SetHidden("DEV1", 0xAABBCCDDEEFF, true, "X");

        var reloaded = AppSettingsModel.Load();
        Assert.True(reloaded.IsHidden("DEV1"));
    }

    [Fact]
    public void HasAnyBatteryReport_FlagsUnknownBatteryDevices()
    {
        Assert.False(Device("DEV1", "A", battery: null).HasAnyBatteryReport);
        Assert.True(Device("DEV2", "B", battery: 50).HasAnyBatteryReport);

        var tws = Device("DEV3", "C", battery: null);
        tws.IsTws = true;
        tws.LeftBatteryLevel = 60;
        Assert.True(tws.HasAnyBatteryReport);
    }
}

public class NotificationTransitionTests : IDisposable
{
    private class RecordingToastService : ToastNotificationService
    {
        public List<(string Name, int Level, bool IsCritical)> Sent { get; } = new();
        public bool FailNext { get; set; }

        protected override bool ShowToast(BluetoothDeviceModel device, bool isCritical)
        {
            if (FailNext) return false;
            Sent.Add((device.DisplayName, device.EffectiveBatteryLevel ?? device.BatteryLevel ?? 0, isCritical));
            return true;
        }
    }

    private readonly string _folder;

    public NotificationTransitionTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), "peripheral-toast-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
        AppSettingsModel.TestFolderOverride = _folder;
    }

    public void Dispose()
    {
        AppSettingsModel.TestFolderOverride = null;
        try { Directory.Delete(_folder, recursive: true); } catch { }
    }

    private static BluetoothDeviceModel Device(string id, int? battery, bool connected = true, bool charging = false)
        => new() { Id = id, Name = "Test Device", BluetoothAddress = 0xAABBCCDDEE01, BatteryLevel = battery, IsConnected = connected, IsCharging = charging };

    private static AppSettingsModel Settings() => new() { EnableNotifications = true };

    [Fact]
    public void FirstSighting_NormalLevel_DoesNotNotify()
    {
        var svc = new RecordingToastService();
        svc.CheckAndNotifyBattery(Device("DEV1", 80), Settings());
        Assert.Empty(svc.Sent);
    }

    [Fact]
    public void DropToLow_NotifiesOnce_ThenStaysSilent()
    {
        var svc = new RecordingToastService();
        svc.CheckAndNotifyBattery(Device("DEV1", 80), Settings());
        svc.CheckAndNotifyBattery(Device("DEV1", 18), Settings());

        Assert.Single(svc.Sent);
        Assert.False(svc.Sent[0].IsCritical);

        // Repeated observations of the same low level never re-notify (the old 30-minute
        // cooldown would have).
        svc.CheckAndNotifyBattery(Device("DEV1", 18), Settings());
        svc.CheckAndNotifyBattery(Device("DEV1", 17), Settings());
        Assert.Single(svc.Sent);
    }

    [Fact]
    public void LowToCritical_NotifiesCritical()
    {
        var svc = new RecordingToastService();
        svc.CheckAndNotifyBattery(Device("DEV1", 80), Settings());
        svc.CheckAndNotifyBattery(Device("DEV1", 18), Settings());
        svc.CheckAndNotifyBattery(Device("DEV1", 8), Settings());

        Assert.Equal(2, svc.Sent.Count);
        Assert.True(svc.Sent[1].IsCritical);

        svc.CheckAndNotifyBattery(Device("DEV1", 7), Settings());
        Assert.Equal(2, svc.Sent.Count);
    }

    [Fact]
    public void FastDropStraightToCritical_NotifiesOnlyCritical()
    {
        var svc = new RecordingToastService();
        svc.CheckAndNotifyBattery(Device("DEV1", 80), Settings());
        svc.CheckAndNotifyBattery(Device("DEV1", 5), Settings());

        Assert.Single(svc.Sent);
        Assert.True(svc.Sent[0].IsCritical);
    }

    [Fact]
    public void RecoveryThenDropAgain_NotifiesAgain()
    {
        var svc = new RecordingToastService();
        svc.CheckAndNotifyBattery(Device("DEV1", 80), Settings());
        svc.CheckAndNotifyBattery(Device("DEV1", 18), Settings());
        Assert.Single(svc.Sent);

        // Charging re-arms the alert.
        svc.CheckAndNotifyBattery(Device("DEV1", 30, charging: true), Settings());
        svc.CheckAndNotifyBattery(Device("DEV1", 18), Settings());

        Assert.Equal(2, svc.Sent.Count);
    }

    [Fact]
    public void DisconnectReconnect_RearsTheAlert()
    {
        var svc = new RecordingToastService();
        svc.CheckAndNotifyBattery(Device("DEV1", 80), Settings());
        svc.CheckAndNotifyBattery(Device("DEV1", 18), Settings());
        Assert.Single(svc.Sent);

        svc.CheckAndNotifyBattery(Device("DEV1", 18, connected: false), Settings());
        svc.CheckAndNotifyBattery(Device("DEV1", 18), Settings());

        Assert.Equal(2, svc.Sent.Count);
    }

    [Fact]
    public void FailedToast_RetriesOnNextObservation()
    {
        var svc = new RecordingToastService();
        svc.CheckAndNotifyBattery(Device("DEV1", 80), Settings());

        svc.FailNext = true;
        svc.CheckAndNotifyBattery(Device("DEV1", 18), Settings());
        Assert.Empty(svc.Sent);

        svc.FailNext = false;
        svc.CheckAndNotifyBattery(Device("DEV1", 18), Settings());
        Assert.Single(svc.Sent);
    }

    [Fact]
    public void Toast_UsesCustomDisplayName()
    {
        var svc = new RecordingToastService();
        var dev = Device("DEV1", 18);
        dev.CustomName = "我的耳机";
        svc.CheckAndNotifyBattery(dev, Settings());

        Assert.Single(svc.Sent);
        Assert.Equal("我的耳机", svc.Sent[0].Name);
    }

    [Fact]
    public void DisabledNotifications_NeverTrackOrNotify()
    {
        var svc = new RecordingToastService();
        svc.CheckAndNotifyBattery(Device("DEV1", 18), new AppSettingsModel { EnableNotifications = false });
        svc.CheckAndNotifyBattery(Device("DEV1", 18), Settings());

        // The first call was a no-op; the second is a first sighting of Low and notifies once.
        Assert.Single(svc.Sent);
    }
}
