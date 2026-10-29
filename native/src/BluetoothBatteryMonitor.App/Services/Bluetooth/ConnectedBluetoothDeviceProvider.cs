using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using BluetoothBatteryMonitor.App.Models;

namespace BluetoothBatteryMonitor.App.Services.Bluetooth;

/// <summary>Show connected devices even when their driver provides no battery value.</summary>
public sealed class ConnectedBluetoothDeviceProvider : IBluetoothBatteryProvider
{
    public string Name=>"Windows connected Bluetooth devices";
    public bool IsSupported=>true;
    public event EventHandler<BluetoothDeviceModel>? DeviceUpdated{add{}remove{}}
    public async Task<IReadOnlyList<BluetoothDeviceModel>> GetDevicesAsync(CancellationToken ct=default)
    {
        var result=new List<BluetoothDeviceModel>();
        var selectors=new[]{BluetoothDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected),
            BluetoothLEDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected)};
        foreach(var selector in selectors)
        {
            var devices=await DeviceInformation.FindAllAsync(selector).AsTask(ct);
            foreach(var d in devices)
            {
                string name=string.IsNullOrWhiteSpace(d.Name)?"Bluetooth device":d.Name;
                result.Add(new BluetoothDeviceModel{Id=d.Id,Name=name,IsConnected=true,
                    Type=name.Contains("Mouse",StringComparison.OrdinalIgnoreCase)?DeviceType.Mouse:
                        name.Contains("键盘")||name.Contains("Keyboard",StringComparison.OrdinalIgnoreCase)?DeviceType.Keyboard:
                        name.Contains("Enco",StringComparison.OrdinalIgnoreCase)||name.Contains("Lolli",StringComparison.OrdinalIgnoreCase)?DeviceType.Earbuds:DeviceType.Generic,
                    BluetoothAddress=BluetoothDeviceModel.ExtractMacAddress(d.Id),
                    ProviderSource="Windows connected Bluetooth · 电量由设备协议提供",LastUpdated=DateTime.Now});
            }
        }
        return result;
    }
    public void StartMonitoring(){}public void StopMonitoring(){}public void Dispose(){}
}
