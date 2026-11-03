extern alias MonitorApp;
using System;
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
}
