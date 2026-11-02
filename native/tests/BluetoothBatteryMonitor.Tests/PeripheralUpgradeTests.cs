extern alias MonitorApp;
using Xunit;
using Reader=MonitorApp::BluetoothBatteryMonitor.App.Services.Bluetooth.NativeRazerBattery;
using Settings=MonitorApp::BluetoothBatteryMonitor.App.Models.AppSettings;
using Device=MonitorApp::BluetoothBatteryMonitor.App.Models.BluetoothDeviceModel;
public class PeripheralUpgradeTests
{
    [Fact] public void RatePacketHasValidatedCommandAndChecksum()
    {
        var b=MonitorApp::BluetoothBatteryMonitor.App.Services.GamePollingService.RateReport(0x40,2,1,16);
        Assert.Equal(91,b.Length);Assert.Equal(0,b[7]);Assert.Equal(0x40,b[8]);Assert.Equal(1,b[9]);Assert.Equal(16,b[10]);
        byte sum=0;for(int i=3;i<89;i++)sum^=b[i];Assert.Equal(sum,b[89]);
    }
    private static byte[] Reply(byte value)
    {
        var b=Reader.Request(31,128);b[1]=2;b[10]=value;b[89]=0;
        for(int i=3;i<89;i++)b[89]^=b[i];return b;
    }
    [Theory][InlineData((byte)0)][InlineData((byte)255)]
    public void NativeEndpoints(byte value)=>Assert.Equal(value,Reader.Decode(Reply(value),31,128));
    [Fact] public void NativeRejectsCorruptOrUnrelatedResponse()
    {
        var b=Reply(200);b[10]^=1;Assert.Throws<System.IO.IOException>(()=>Reader.Decode(b,31,128));
        Assert.Throws<System.IO.IOException>(()=>Reader.Decode(Reply(200),63,128));
        Assert.Throws<System.IO.IOException>(()=>Reader.Decode(Reply(200),31,132));
    }
    [Fact] public void SortingSurvivesJsonAndUnknownDevicesFollow()
    {
        var original=new Settings{DeviceOrder=new(){"b","a"}};
        var saved=System.Text.Json.JsonSerializer.Deserialize<Settings>(System.Text.Json.JsonSerializer.Serialize(original))!;
        Assert.Equal(0,saved.DeviceRank("B"));Assert.Equal(1,saved.DeviceRank("a"));Assert.Equal(int.MaxValue,saved.DeviceRank("new"));
    }
    [Theory][InlineData("设备查询超时，可能休眠或无线链路不可用","可能休眠或无线链路不可用")]
    [InlineData("Access denied","读取受限 · 接口权限或占用")][InlineData("checksum mismatch","电量读取失败")]
    public void FailureStatesDoNotClaimSleep(string error,string label)=>Assert.Equal(label,new Device{BatteryReadError=error}.ConnectionStatusText);
}
