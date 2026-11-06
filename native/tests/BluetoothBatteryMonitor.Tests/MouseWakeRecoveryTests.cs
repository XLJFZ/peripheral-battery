extern alias MonitorApp;
using Xunit;
using Model = MonitorApp::BluetoothBatteryMonitor.App.Models.BluetoothDeviceModel;
using DeviceType = MonitorApp::BluetoothBatteryMonitor.App.Models.DeviceType;
using Engine = MonitorApp::BluetoothBatteryMonitor.App.Services.Bluetooth.BluetoothBatteryEngine;

public class MouseWakeRecoveryTests
{
    [Theory]
    [InlineData("Peripheral HID · Razer 原生", false, DeviceType.Mouse, true)]
    [InlineData("Peripheral HID · MCHOSE", false, DeviceType.Mouse, true)]
    [InlineData("Peripheral HID · Razer 原生", true, DeviceType.Mouse, false)]
    [InlineData("Windows connected Bluetooth", false, DeviceType.Mouse, false)]
    [InlineData("Peripheral HID", false, DeviceType.Gamepad, false)]
    public void RecoveryTargetsOnlySleepingHidMice(string source, bool connected, DeviceType type, bool expected)
        => Assert.Equal(expected, Engine.NeedsHidRecovery(new Model
        { ProviderSource = source, IsConnected = connected, Type = type }));
}
