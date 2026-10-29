extern alias MonitorApp;
using Xunit;
using Device = MonitorApp::BluetoothBatteryMonitor.App.Models.BluetoothDeviceModel;
using Kind = MonitorApp::BluetoothBatteryMonitor.App.Models.DeviceType;
using Identity = MonitorApp::BluetoothBatteryMonitor.App.Services.Bluetooth.ControllerIdentity;
public class ControllerIdentityTests
{
    static Device Pad(string id,string provider,string? container,int battery=100)=>new(){Id=id,Type=Kind.Gamepad,IsConnected=true,BatteryLevel=battery,ProviderSource=provider,ContainerId=container,UsbVendorId=0x2dc8,UsbProductId=0x310b};
    [Fact] public void SamePhysicalControllerKeepsExactHidBattery()
    {
        var hid=Pad("hid","Peripheral HID · 8BitDo HID","one",99);
        var alias=Pad("windows","Windows gamepad driver","one");
        var rows=Identity.Filter(new[]{hid,alias});
        Assert.Single(rows);Assert.Same(hid,rows[0]);Assert.Equal(99,rows[0].BatteryLevel);
    }
    [Fact] public void DifferentPhysicalControllersStaySeparate()
    {
        Assert.Equal(2,Identity.Filter(new[]{Pad("a","Peripheral HID","one"),Pad("b","Windows gamepad driver","two")}).Count);
    }
    [Fact] public void AmbiguousSameModelControllersStaySeparate()
    {
        Assert.Equal(3,Identity.Filter(new[]{Pad("a","Peripheral HID",null),Pad("b","Peripheral HID",null),Pad("c","Windows gamepad driver",null)}).Count);
    }
    [Fact] public void UniqueUsbPairCanUseIdentityFallback()
    {
        Assert.Single(Identity.Filter(new[]{Pad("a","Peripheral HID",null),Pad("b","Windows gamepad driver",null)}));
    }
}
