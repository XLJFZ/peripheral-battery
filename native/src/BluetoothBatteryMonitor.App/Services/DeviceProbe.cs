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
            new SamsungGalaxyBudsBatteryProvider(),
            new SonyHeadphonesBatteryProvider(),
            new LogitechHidBatteryProvider(),new LogitechGHubBatteryProvider(),
            new PlayStationControllerBatteryProvider(),new NintendoSwitchBatteryProvider(),new SteelSeriesBatteryProvider()};
        try
        {
            var monitoringErrors=new List<object>();
            foreach(var p in providers)try{p.StartMonitoring();}catch(Exception ex){monitoringErrors.Add(new{provider=p.Name,error=ex.ToString()});}
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
            using var engine=new BluetoothBatteryEngine(new Notification.ToastNotificationService());
            var viewModel=new ViewModels.MainViewModel(engine);
            var statuses=new global::System.Collections.Concurrent.ConcurrentQueue<string>();
            engine.StatusChanged+=(_,text)=>statuses.Enqueue(text);
            bool collectionUpdated=false;engine.DevicesUpdated+=(_,_)=>collectionUpdated=true;
            string? engineStartError=null;
            try{await Task.Run(engine.Start);}catch(Exception ex){engineStartError=ex.ToString();}
            var until=DateTime.UtcNow.AddSeconds(15);
            while(engineStartError==null&&DateTime.UtcNow<until&&!collectionUpdated&&!statuses.Any(s=>s.StartsWith("Scan failed:")))await Task.Delay(100);
            var engineReport=new{startError=engineStartError,statuses=statuses.ToArray(),count=engine.CurrentDevices.Count,
                visibleCount=viewModel.Devices.Count,collectionUpdated,viewStatus=viewModel.StatusText,
                devices=engine.CurrentDevices.Select(d=>new{d.Name,d.BatteryLevel,d.HasAnyBatteryReport,d.BatteryReadError,
                    hidden=Models.AppSettings.Load().IsHidden(d.Id,d.BluetoothAddress)})};
            var json=JsonSerializer.Serialize(new{monitoringErrors,engineReport,sources=results,filteredDevices,classicConnections=snapshot.ClassicConnectedByMac,aepConnections=snapshot.AepConnectedByMac},new JsonSerializerOptions{WriteIndented=true});
            await File.WriteAllTextAsync(path,json);
        }
        finally{foreach(var p in providers)p.Dispose();}
    }
}
