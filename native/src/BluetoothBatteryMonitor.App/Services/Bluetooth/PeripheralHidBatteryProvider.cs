using System.Diagnostics;
using System.IO;
using System.Text.Json;
using BluetoothBatteryMonitor.App.Models;

namespace BluetoothBatteryMonitor.App.Services.Bluetooth;

/// <summary>Reuses the verified local Razer/MCHOSE reader; never calls a remote service.</summary>
public sealed class PeripheralHidBatteryProvider : IBluetoothBatteryProvider
{
    public string Name => "Peripheral HID";
    public bool IsSupported => File.Exists(BackendPath);
    public event EventHandler<BluetoothDeviceModel>? DeviceUpdated { add { } remove { } }
    private static string BackendPath => Path.Combine(AppContext.BaseDirectory,"backend","UniversalBatteryTray.exe");
    private Dictionary<string,BluetoothDeviceModel> previous = new();
    public async Task<IReadOnlyList<BluetoothDeviceModel>> GetDevicesAsync(CancellationToken ct=default)
    {
        if(!IsSupported)return InvalidatePrevious("HID backend unavailable");
        string temp=Path.Combine(Path.GetTempPath(),"peripheral-hid-"+Guid.NewGuid().ToString("N")+".json");
        var rows=new List<BluetoothDeviceModel>();
        try
        {
            var info=new ProcessStartInfo(BackendPath){UseShellExecute=false,CreateNoWindow=true};
            info.ArgumentList.Add("--hid-scan");info.ArgumentList.Add(temp);
            using var process=Process.Start(info);
            if(process is null)return InvalidatePrevious("HID backend did not start");
            try { await process.WaitForExitAsync(ct); }
            catch(OperationCanceledException){ try{process.Kill(true);}catch{} throw; }
            if(process.ExitCode!=0 || !File.Exists(temp))return InvalidatePrevious("HID backend did not return a reading");
            using var doc=JsonDocument.Parse(await File.ReadAllTextAsync(temp,ct));
            foreach(var r in doc.RootElement.EnumerateArray())
            {
                string id="PeripheralHID:"+r.GetProperty("key").GetString();
                string name=r.GetProperty("name").GetString()??"Wireless peripheral";
                int? battery=r.GetProperty("percent").ValueKind==JsonValueKind.Number?r.GetProperty("percent").GetInt32():null;
                if(battery is <0 or >100)battery=null;
                rows.Add(new BluetoothDeviceModel{Id=id,Name=name,BatteryLevel=battery,
                    RawBatteryValue=r.TryGetProperty("raw_value",out var raw)&&raw.ValueKind==JsonValueKind.Number?raw.GetInt32():null,
                    BatterySampledAt=battery.HasValue&&r.TryGetProperty("sampled_at",out var sampled)&&sampled.ValueKind==JsonValueKind.Number?DateTimeOffset.FromUnixTimeMilliseconds((long)(sampled.GetDouble()*1000)).LocalDateTime:null,
                    BatteryReadError=r.TryGetProperty("error",out var error)?error.GetString():null,
                    IsCharging=r.GetProperty("charging").ValueKind==JsonValueKind.True,
                    IsConnected=battery.HasValue||(r.TryGetProperty("connected",out var connected)&&connected.ValueKind==JsonValueKind.True),
                    ProviderSource="Peripheral HID · "+r.GetProperty("provider").GetString(),
                    Type=name.Contains("8BitDo")?DeviceType.Gamepad:name.Contains("G87")||name.Contains("QK75")?DeviceType.Keyboard:DeviceType.Mouse,LastUpdated=DateTime.Now});
                var added=rows[^1];ControllerIdentity.SetUsbIds(added);
                if(added.Type==DeviceType.Gamepad)
                    added.ContainerId=await ControllerIdentity.ContainerAsync(r.GetProperty("key").GetString()![4..],ct);
            }
            var ids=rows.Select(r=>r.Id).ToHashSet();
            foreach(var old in previous.Values.Where(p=>!ids.Contains(p.Id)))
            { old.IsConnected=false;old.BatteryLevel=null;old.RawBatteryValue=null;old.BatterySampledAt=null;old.LastUpdated=DateTime.Now;rows.Add(old); }
            previous=rows.ToDictionary(r=>r.Id);
        }
        catch(Exception ex){return InvalidatePrevious("HID read failed: "+ex.GetType().Name);}
        finally{try{File.Delete(temp);}catch{}}
        return rows;
    }
    private IReadOnlyList<BluetoothDeviceModel> InvalidatePrevious(string reason)
    {
        foreach(var old in previous.Values)
        {
            old.IsConnected=false;old.BatteryLevel=null;old.IsCharging=false;
            old.RawBatteryValue=null;old.BatterySampledAt=null;
            old.BatteryReadError=reason;old.LastUpdated=DateTime.Now;
        }
        return previous.Values.ToList();
    }
    public void StartMonitoring(){} public void StopMonitoring(){} public void Dispose(){}
}
