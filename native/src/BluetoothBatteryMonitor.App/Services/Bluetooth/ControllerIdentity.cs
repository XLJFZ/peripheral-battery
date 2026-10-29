using System.Text.RegularExpressions;
using Windows.Devices.Enumeration;
using BluetoothBatteryMonitor.App.Models;

namespace BluetoothBatteryMonitor.App.Services.Bluetooth;

public static class ControllerIdentity
{
    public static async Task<string?> ContainerAsync(string id,CancellationToken ct)
    {
        try
        {
            var info=await DeviceInformation.CreateFromIdAsync(id,new[]{"System.Devices.ContainerId"}).AsTask(ct);
            return info.Properties.TryGetValue("System.Devices.ContainerId",out var v)?v?.ToString():null;
        }catch{return null;}
    }
    public static void SetUsbIds(BluetoothDeviceModel d)
    {
        var m=Regex.Match(d.Id,"vid_([0-9a-f]{4})&pid_([0-9a-f]{4})",RegexOptions.IgnoreCase);
        if(m.Success){d.UsbVendorId=Convert.ToUInt16(m.Groups[1].Value,16);d.UsbProductId=Convert.ToUInt16(m.Groups[2].Value,16);}
    }
    public static List<BluetoothDeviceModel> Filter(IEnumerable<BluetoothDeviceModel> devices)
    {
        var rows=devices.ToList();
        var exact=rows.Where(d=>d.Type==DeviceType.Gamepad && d.IsConnected && d.BatteryLevel.HasValue && d.ProviderSource.StartsWith("Peripheral HID",StringComparison.Ordinal)).ToList();
        var windows=rows.Where(d=>d.ProviderSource.StartsWith("Windows gamepad",StringComparison.Ordinal)).ToList();
        var removed=new HashSet<string>();
        foreach(var alias in windows)
        {
            bool match=exact.Any(d=>!string.IsNullOrEmpty(d.ContainerId)&&d.ContainerId!=Guid.Empty.ToString()&&d.ContainerId.Equals(alias.ContainerId,StringComparison.OrdinalIgnoreCase));
            if(!match && alias.UsbVendorId!=0 && alias.UsbProductId!=0)
            {
                var candidates=exact.Where(d=>d.UsbVendorId==alias.UsbVendorId&&d.UsbProductId==alias.UsbProductId).ToList();
                var aliases=windows.Where(d=>d.UsbVendorId==alias.UsbVendorId&&d.UsbProductId==alias.UsbProductId).ToList();
                // A family fallback is safe only for one unambiguous endpoint pair.
                match=candidates.Count==1&&aliases.Count==1&&
                    (string.IsNullOrEmpty(alias.ContainerId)||string.IsNullOrEmpty(candidates[0].ContainerId)||alias.ContainerId.Equals(candidates[0].ContainerId,StringComparison.OrdinalIgnoreCase));
            }
            if(match)removed.Add(alias.Id);
        }
        var xinput=rows.Where(d=>d.ProviderSource.StartsWith("XInput",StringComparison.Ordinal)).ToList();
        if(windows.Count>0&&windows.All(d=>removed.Contains(d.Id))&&xinput.Count==windows.Count)
            foreach(var alias in xinput)removed.Add(alias.Id);
        return rows.Where(d=>!removed.Contains(d.Id)).ToList();
    }
}
