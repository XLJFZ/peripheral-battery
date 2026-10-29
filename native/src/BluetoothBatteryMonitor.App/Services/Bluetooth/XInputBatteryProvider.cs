using System.Runtime.InteropServices;
using BluetoothBatteryMonitor.App.Models;

namespace BluetoothBatteryMonitor.App.Services.Bluetooth;

public sealed class XInputBatteryProvider : IBluetoothBatteryProvider
{
    public string Name=>"XInput controller status";
    public bool IsSupported=>true;
    public event EventHandler<BluetoothDeviceModel>? DeviceUpdated{add{}remove{}}
    [StructLayout(LayoutKind.Sequential)]private struct BatteryInfo{public byte Type;public byte Level;}
    [DllImport("xinput1_4.dll",EntryPoint="XInputGetState")]private static extern uint GetState(uint index,[Out]byte[] state);
    [DllImport("xinput1_4.dll",EntryPoint="XInputGetBatteryInformation")]private static extern uint GetBattery(uint index,byte deviceType,out BatteryInfo battery);
    public static string Describe(byte type,byte level)=>type==1?"未报告电量":type==2||type==3?level switch{0=>"电量耗尽",1=>"电量低",2=>"电量中等",3=>"电量充足",_=>"电量未知"}:"未报告电量";
    public Task<IReadOnlyList<BluetoothDeviceModel>> GetDevicesAsync(CancellationToken ct=default)
    {
        var rows=new List<BluetoothDeviceModel>();
        for(uint i=0;i<4;i++)
        {
            ct.ThrowIfCancellationRequested();
            if(GetState(i,new byte[16])!=0)continue;
            string state=GetBattery(i,0,out var b)==0?Describe(b.Type,b.Level):"未报告电量";
            // A driver with no battery telemetry supplies no useful fallback card.
            if(state=="未报告电量")continue;
            rows.Add(new BluetoothDeviceModel{Id="XInput:"+i,Name="XInput 手柄 "+(i+1),Type=DeviceType.Gamepad,
                IsConnected=true,BatteryLevelDescription=state,ProviderSource="XInput · "+state+"（驱动未提供百分比）",LastUpdated=DateTime.Now});
        }
        return Task.FromResult<IReadOnlyList<BluetoothDeviceModel>>(rows);
    }
    public void StartMonitoring(){}public void StopMonitoring(){}public void Dispose(){}
}
