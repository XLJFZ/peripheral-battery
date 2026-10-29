using System.IO;
using System.Text.Json;
using BluetoothBatteryMonitor.App.Services.Bluetooth;

namespace BluetoothBatteryMonitor.App.Services;

public static class DeviceProbe
{
    public static async Task RunAsync(string path)
    {
        IBluetoothBatteryProvider[] providers = {
            new PeripheralHidBatteryProvider(),new WindowsGamepadBatteryProvider(),
            new ConnectedBluetoothDeviceProvider(),
            new XInputBatteryProvider(),
            new BleGattBatteryProvider(),new WindowsPnpBatteryProvider(),
            new AppleAirPodsBeaconProvider(),new SamsungGalaxyBudsBatteryProvider(),
            new SonyHeadphonesBatteryProvider(),new GoogleFastPairBatteryProvider(),
            new LogitechHidBatteryProvider(),new LogitechGHubBatteryProvider(),
            new PlayStationControllerBatteryProvider(),new NintendoSwitchBatteryProvider(),new SteelSeriesBatteryProvider()};
        try
        {
            foreach(var p in providers)try{p.StartMonitoring();}catch{}
            await Task.Delay(1500);
            var tasks=providers.Select(async p=>
            {
                using var cts=new CancellationTokenSource(TimeSpan.FromSeconds(15));
                try{return (object)new {provider=p.Name, devices=await p.GetDevicesAsync(cts.Token),error=""};}
                catch(Exception ex){return new {provider=p.Name,devices=Array.Empty<object>(),error=ex.GetType().Name+": "+ex.Message};}
            });
            var results=await Task.WhenAll(tasks);
            var sourceJson=JsonSerializer.SerializeToElement(results);
            var combined=sourceJson.EnumerateArray().SelectMany(s=>s.GetProperty("devices").EnumerateArray()).Select(d=>d.Deserialize<BluetoothBatteryMonitor.App.Models.BluetoothDeviceModel>()!).ToList();
            var filteredDevices=ControllerIdentity.Filter(combined);
            var snapshot=await BluetoothConnectionChecker.CaptureSnapshotAsync();
            var json=JsonSerializer.Serialize(new{sources=results,filteredDevices,classicConnections=snapshot.ClassicConnectedByMac,aepConnections=snapshot.AepConnectedByMac},new JsonSerializerOptions{WriteIndented=true});
            await File.WriteAllTextAsync(path,json);
        }
        finally{foreach(var p in providers)p.Dispose();}
    }
}
