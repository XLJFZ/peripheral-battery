using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;
using Windows.Devices.Enumeration;
using BluetoothBatteryMonitor.App.Models;

namespace BluetoothBatteryMonitor.App.Services.Bluetooth;

/// <summary>Independent feature-only reader. Only catalogued battery/charge queries.</summary>
public static class NativeRazerBattery
{
    internal static readonly object IoGate=new();
    private const string Selector="System.Devices.InterfaceClassGuid:=\"{4D1E55B2-F16F-11CF-88CB-001111000030}\"";
    public static byte[] Request(byte tid, byte command)
    {
        var b=new byte[91]; b[2]=tid;b[6]=2;b[7]=7;b[8]=command;
        for(int i=3;i<89;i++)b[89]^=b[i];return b;
    }
    public static byte Decode(byte[] b, byte tid, byte command)
    {
        if(b.Length!=91 || b[0]!=0)throw new IOException("Invalid report length/ID");
        byte sum=0;for(int i=3;i<89;i++)sum^=b[i];
        if(sum!=b[89] || b[2]!=tid || b[6]!=2 || b[7]!=7 || b[8]!=command)throw new IOException("Response mismatch/checksum");
        if(b[1]==1)throw new IOException("设备忙，等待重试");
        if(b[1]==4)throw new IOException("设备查询超时，可能休眠或无线链路不可用");
        if(b[1]!=2)throw new IOException($"设备返回状态 0x{b[1]:X2}");
        return b[10];
    }
    private static byte Query(SafeFileHandle handle,byte tid,byte command,int delay,CancellationToken ct)
    {
        lock(IoGate) return QueryLocked(handle,tid,command,delay,ct);
    }
    private static byte QueryLocked(SafeFileHandle handle,byte tid,byte command,int delay,CancellationToken ct)
    {
        Exception? last=null;
        for(int n=0;n<3;n++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var request=Request(tid,command);
                if(!HidD_SetFeature(handle,request,request.Length))throw new global::System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                if(ct.WaitHandle.WaitOne(delay))ct.ThrowIfCancellationRequested();
                var response=new byte[91];
                if(!HidD_GetFeature(handle,response,response.Length))throw new global::System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                return Decode(response,tid,command);
            }
            catch(Exception ex) when(ex is IOException or global::System.ComponentModel.Win32Exception){last=ex;}
        }
        throw new IOException(last?.Message);
    }
    public static async Task<List<BluetoothDeviceModel>> ReadAsync(CancellationToken ct)
    {
        var result=new List<BluetoothDeviceModel>();
        using var catalog=JsonDocument.Parse(await RazerCatalog.ReadAllTextAsync(ct));
        var entries=catalog.RootElement.EnumerateArray().ToDictionary(e=>e.GetProperty("pid").GetInt32(),e=>e.Clone());
        var devices=await DeviceInformation.FindAllAsync(Selector).AsTask(ct);
        foreach(var d in devices)
        {
            ct.ThrowIfCancellationRequested();var path=d.Id.ToLowerInvariant();
            var match=Regex.Match(path,@"vid_1532&pid_([0-9a-f]{4})&mi_([0-9a-f]{2})");
            if(!match.Success || !entries.TryGetValue(Convert.ToInt32(match.Groups[1].Value,16),out var entry) || Convert.ToInt32(match.Groups[2].Value,16)!=entry.GetProperty("interface").GetInt32())continue;
            // Synapse virtual system/media collections have a second Col suffix.
            if(Regex.Matches(path,"&col").Count>1)continue;
            var key=path;var parts=path.Split('#');
            if(parts.Length>=3 && parts[1].Contains("&col"))key=parts[0]+"#"+parts[1].Split("&col")[0]+"#"+parts[2][..parts[2].LastIndexOf('&')];
            var id="PeripheralHID:hid:"+key;
            if(result.Any(x=>x.Id==id && x.BatteryLevel.HasValue))continue;
            var row=new BluetoothDeviceModel{Id=id,Name=entry.GetProperty("name").GetString()!,Type=DeviceType.Mouse,
                ProviderSource="Peripheral HID · Razer 原生",BatterySourceNote="Windows 共享 Feature 直读；不依赖 Python 或雷云日志",LastUpdated=DateTime.Now};
            using var handle=CreateFileW(d.Id,0,3,IntPtr.Zero,3,0,IntPtr.Zero);
            if(handle.IsInvalid && Marshal.GetLastWin32Error() is 2 or 3 or 1167)continue;
            try
            {
                if(handle.IsInvalid)throw new global::System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                byte tid=entry.GetProperty("tid").GetByte();int wait=(int)Math.Ceiling(entry.GetProperty("wait").GetDouble()*1000);
                byte raw=await Task.Run(()=>Query(handle,tid,0x80,wait,ct),ct);
                row.RawBatteryValue=raw;row.BatteryLevel=(int)Math.Floor(raw*100.0/255+.5);row.BatterySampledAt=DateTime.Now;row.IsConnected=true;
                if(entry.GetProperty("charging").GetBoolean())
                    try{row.IsCharging=Query(handle,tid,0x84,wait,ct)==1;}catch(IOException){row.BatterySourceNote+="；充电状态本次不可用";}
            }
            catch(Exception ex) when(ex is IOException or global::System.ComponentModel.Win32Exception){row.BatteryReadError=ex.Message;}
            result.RemoveAll(x=>x.Id==id);result.Add(row);
        }
        return result;
    }
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern SafeFileHandle CreateFileW(string path,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
    [DllImport("hid.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.U1)] private static extern bool HidD_SetFeature(SafeFileHandle handle,byte[] data,int length);
    [DllImport("hid.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.U1)] private static extern bool HidD_GetFeature(SafeFileHandle handle,byte[] data,int length);
}
