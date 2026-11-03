extern alias MonitorApp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using AppSettingsModel = MonitorApp::BluetoothBatteryMonitor.App.Models.AppSettings;
using BluetoothDeviceModel = MonitorApp::BluetoothBatteryMonitor.App.Models.BluetoothDeviceModel;
using DeviceType = MonitorApp::BluetoothBatteryMonitor.App.Models.DeviceType;
using TrayIconGenerator = MonitorApp::BluetoothBatteryMonitor.App.Helpers.TrayIconGenerator;
using TrayIconManager = MonitorApp::BluetoothBatteryMonitor.App.Services.Tray.TrayIconManager;
using DeviceItemViewModel = MonitorApp::BluetoothBatteryMonitor.App.ViewModels.DeviceItemViewModel;

public class PerDeviceTrayTests : IDisposable
{
    private readonly string _folder;

    public PerDeviceTrayTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), "peripheral-perdevice-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
        AppSettingsModel.TestFolderOverride = _folder;
    }

    public void Dispose()
    {
        AppSettingsModel.TestFolderOverride = null;
        try { Directory.Delete(_folder, recursive: true); } catch { }
    }

    private static DeviceItemViewModel Vm(string id, int? battery, bool connected = true)
    {
        var model = new BluetoothDeviceModel { Id = id, Name = id, BatteryLevel = battery, IsConnected = connected };
        var vm = new DeviceItemViewModel(model);
        vm.UpdateFromModel(model);
        return vm;
    }

    [Fact]
    public void SelectPerDeviceIcons_OnlyConnectedDevicesWithBattery()
    {
        var devices = new[]
        {
            Vm("A", 60),
            Vm("B", 40, connected: false),
            Vm("C", null),
            Vm("D", 80),
        };

        var selected = TrayIconManager.SelectPerDeviceIcons(devices);

        Assert.Equal(new[] { "A", "D" }, selected.Select(d => d.Id).ToArray());
    }

    [Fact]
    public void SelectPerDeviceIcons_LowestBatteryFirst_CappedAtFour()
    {
        var devices = Enumerable.Range(1, 6).Select(i => Vm($"DEV{i}", i * 10)).ToList();

        var selected = TrayIconManager.SelectPerDeviceIcons(devices);

        Assert.Equal(4, selected.Count);
        Assert.Equal(new[] { "DEV1", "DEV2", "DEV3", "DEV4" }, selected.Select(d => d.Id).ToArray());
    }

    [Fact]
    public void PulseAlpha_BoundedAndSmooth()
    {
        // Every step stays within the 45..100 band.
        for (int i = 0; i < TrayIconGenerator.PulseSteps; i++)
        {
            int alpha = TrayIconGenerator.GetPulseAlphaPercent(i);
            Assert.InRange(alpha, 45, 100);
        }

        // Cycle start and mid are the extremes.
        Assert.Equal(45, TrayIconGenerator.GetPulseAlphaPercent(0));
        Assert.Equal(100, TrayIconGenerator.GetPulseAlphaPercent(TrayIconGenerator.PulseSteps / 2));

        // One full cycle returns to the start.
        Assert.Equal(
            TrayIconGenerator.GetPulseAlphaPercent(0),
            TrayIconGenerator.GetPulseAlphaPercent(TrayIconGenerator.PulseSteps));
    }

    [Fact]
    public void PerDeviceIcon_RendersAllDeviceTypes_WithoutThrowing()
    {
        var settings = new AppSettingsModel();
        foreach (DeviceType type in Enum.GetValues(typeof(DeviceType)))
        {
            using var icon = TrayIconGenerator.GeneratePerDevice(50, charging: false, type, dark: true, settings, pulseTick: -1);
            Assert.NotNull(icon);
            using var breathing = TrayIconGenerator.GeneratePerDevice(50, charging: true, type, dark: false, settings, pulseTick: 3);
            Assert.NotNull(breathing);
        }
    }

    [Fact]
    public void TrayFeatureSettings_HaveSensibleDefaults()
    {
        var settings = new AppSettingsModel();
        Assert.False(settings.TrayPerDeviceIcons);   // opt-in: default keeps the single aggregate icon
        Assert.True(settings.TrayChargingPulse);
    }

    // ------------------------------------------------- aggregate icon breathing (v0.3.18)

    [Fact]
    public void AggregateRing_ChargingBreathesBetweenPulseExtremes()
    {
        var settings = new AppSettingsModel();
        var devices = new List<(int Level, bool Charging)> { (80, false), (60, true), (40, false) };

        using var faint = TrayIconGenerator.RenderMultiple(devices, dark: true, settings, pulseTick: 0);
        using var bright = TrayIconGenerator.RenderMultiple(devices, dark: true, settings, pulseTick: TrayIconGenerator.PulseSteps / 2);

        // Sample the top of the charging ring (device #2, second ring slot).
        double SampleAlpha(System.Drawing.Bitmap bmp)
        {
            double sum = 0;
            for (int x = 30; x <= 34; x++) sum += bmp.GetPixel(x, 14).A;
            return sum / 5;
        }
        double SampleGreen(System.Drawing.Bitmap bmp)
        {
            double sum = 0;
            for (int x = 30; x <= 34; x++) sum += bmp.GetPixel(x, 14).G;
            return sum / 5;
        }

        Assert.True(SampleGreen(bright) > SampleGreen(faint) + 10,
            $"breathing green should rise at mid-cycle: {SampleGreen(faint):F0} -> {SampleGreen(bright):F0}");
        Assert.True(SampleAlpha(bright) >= SampleAlpha(faint),
            $"breathing alpha should not fall at mid-cycle: {SampleAlpha(faint):F0} -> {SampleAlpha(bright):F0}");
    }

    [Fact]
    public void AggregateRing_NonChargingRingIsUnaffectedByPulse()
    {
        var settings = new AppSettingsModel();
        var devices = new List<(int Level, bool Charging)> { (80, false), (60, true) };

        // Device #1 (first slot, not charging) must render identically at both extremes.
        var faint = TrayIconGenerator.RenderMultiple(devices, dark: true, settings, pulseTick: 0);
        var bright = TrayIconGenerator.RenderMultiple(devices, dark: true, settings, pulseTick: TrayIconGenerator.PulseSteps / 2);
        using (faint) using (bright)
        {
            // First ring slot centre: inset = 2 + width/2 (width 10 for count==2) -> ring box
            // (7,7,50,50); sample just outside the second ring's reach on the left edge of ring 1.
            for (int y = 8; y <= 50; y += 6)
            {
                Assert.Equal(faint.GetPixel(9, y).A, bright.GetPixel(9, y).A);
            }
        }
    }

    [Fact]
    public void AggregateRender_SmokeAllModes_WithPulse()
    {
        var settings = new AppSettingsModel();
        // multi-ring (3 devices), dual-ring, single-device (routes to Render), digit style.
        using (TrayIconGenerator.RenderMultiple(new[] { (80, false), (60, true), (30, false) }, true, settings, 4))
        using (TrayIconGenerator.RenderMultiple(new[] { (80, false), (60, true) }, true, settings, 4))
        using (TrayIconGenerator.RenderMultiple(new[] { (60, true) }, true, settings, 4))
        {
        }
        var digit = new AppSettingsModel { TrayIconStyle = "数字" };
        using (TrayIconGenerator.RenderMultiple(new[] { (80, false), (60, true) }, true, digit, 4))
        using (TrayIconGenerator.RenderMultiple(new[] { (60, true) }, true, digit, 4))
        {
        }
    }
}
