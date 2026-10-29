extern alias MonitorApp;
using Xunit;
using Device = MonitorApp::BluetoothBatteryMonitor.App.Models.BluetoothDeviceModel;
using Engine = MonitorApp::BluetoothBatteryMonitor.App.Services.Bluetooth.BluetoothBatteryEngine;
using Toast = MonitorApp::BluetoothBatteryMonitor.App.Services.Notification.ToastNotificationService;

public class HidFreshnessTests
{
    [Fact]
    public void MissingHidReadingClearsCachedBatteryAndCanRecover()
    {
        using var engine = new Engine(new Toast());
        const string id = "PeripheralHID:test-mouse";
        var old = engine.MergeOrAddDevice(new Device { Id=id, Name="Mouse", ProviderSource="Peripheral HID · Razer HID", BatteryLevel=94, RawBatteryValue=240, IsConnected=true });
        var failed = engine.MergeOrAddDevice(new Device { Id=id, Name="Mouse", ProviderSource="Peripheral HID · Razer HID", BatteryLevel=null, IsConnected=false, BatteryReadError="unavailable" });
        Assert.Same(old, failed);
        Assert.Null(failed.BatteryLevel);
        Assert.Null(failed.RawBatteryValue);
        Assert.False(failed.IsConnected);
        var fresh = engine.MergeOrAddDevice(new Device { Id=id, Name="Mouse", ProviderSource="Peripheral HID · Razer HID", BatteryLevel=90, RawBatteryValue=230, IsConnected=true });
        Assert.Equal(90, fresh.BatteryLevel);
        Assert.Equal(230, fresh.RawBatteryValue);
        Assert.True(fresh.IsConnected);
        Assert.Null(fresh.BatteryReadError);
    }
}
