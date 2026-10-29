using Windows.Gaming.Input;
using Windows.System.Power;
using BluetoothBatteryMonitor.App.Models;

namespace BluetoothBatteryMonitor.App.Services.Bluetooth;

/// <summary>Read the battery report the installed game-controller driver exposes.</summary>
public sealed class WindowsGamepadBatteryProvider : IBluetoothBatteryProvider
{
    public WindowsGamepadBatteryProvider(){ _ = RawGameController.RawGameControllers.Count; }
    public string Name=>"Windows game controller battery";
    public bool IsSupported=>true;
    public event EventHandler<BluetoothDeviceModel>? DeviceUpdated{add{}remove{}}
    public async Task<IReadOnlyList<BluetoothDeviceModel>> GetDevicesAsync(CancellationToken ct=default)
    {
        var rows=new List<BluetoothDeviceModel>();
        foreach(var gamepad in RawGameController.RawGameControllers)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var report=gamepad.TryGetBatteryReport();
                int? percent=null;
                if(report?.Status!=BatteryStatus.NotPresent && report?.FullChargeCapacityInMilliwattHours is int full && full>0 && report.RemainingCapacityInMilliwattHours is int remaining)
                    percent=Math.Clamp((int)Math.Round(100.0*remaining/full),0,100);
                rows.Add(new BluetoothDeviceModel{Id="WindowsGamepad:"+gamepad.NonRoamableId,
                    Name=gamepad.DisplayName,Type=DeviceType.Gamepad,IsConnected=true,
                    UsbVendorId=gamepad.HardwareVendorId,UsbProductId=gamepad.HardwareProductId,
                    ContainerId=await ControllerIdentity.ContainerAsync(gamepad.NonRoamableId,ct),
                    BatteryLevel=percent,IsCharging=report?.Status==BatteryStatus.Charging,
                    ProviderSource="Windows gamepad driver",LastUpdated=DateTime.Now});
            }catch{}
        }
        return rows;
    }
    public void StartMonitoring(){}public void StopMonitoring(){}public void Dispose(){}
}
