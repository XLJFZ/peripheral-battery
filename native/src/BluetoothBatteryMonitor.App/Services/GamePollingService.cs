using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using BluetoothBatteryMonitor.App.Models;
using BluetoothBatteryMonitor.App.Services.Bluetooth;
using Microsoft.Win32.SafeHandles;
using Windows.Devices.Enumeration;

namespace BluetoothBatteryMonitor.App.Services;

/// <summary>Opt-in foreground/fullscreen scheduling for the tested 00B7 receiver.</summary>
public sealed class GamePollingService : IDisposable
{
    public static GamePollingService Instance { get; }=new();
    public string Status { get; private set; }="未开启；当前已验证接收器最高 1000Hz";
    private readonly SemaphoreSlim gate=new(1,1);
    private Timer? timer;
    private string? activePath;
    private int originalRate;
    private bool? lastScene;
    private int stableCount;
    private bool faulted,disposed;
    private int restoreRetries;
    private static string Journal=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"PeripheralBatteryNative","polling-recovery.json");
    public void Start()=>timer ??= new Timer(async _=>await Tick(),null,2000,2000);
    public static async Task VerifyAsync(string output)
    {
        var devices=await DeviceInformation.FindAllAsync("System.Devices.InterfaceClassGuid:=\"{4D1E55B2-F16F-11CF-88CB-001111000030}\"");
        var device=devices.First(d=>d.Id.Contains("vid_1532&pid_00b7&mi_00#",StringComparison.OrdinalIgnoreCase));
        int original=ReadRate(device.Id),changed=0,restored=0;
        try{SetRate(device.Id,original==500?1000:500);changed=ReadRate(device.Id);}
        finally{SetRate(device.Id,original);restored=ReadRate(device.Id);}
        await File.WriteAllTextAsync(output,JsonSerializer.Serialize(new{original,changed,restored,success=restored==original && changed!=original}));
    }
    public void ResetFailure(){faulted=false;lastScene=null;stableCount=0;}
    public async Task Tick()
    {
        if(disposed || !await gate.WaitAsync(0))return;
        try
        {
            if(File.Exists(Journal) && activePath==null)
            {
                // A failed restore must not latch `faulted` forever: a receiver that is merely
                // unplugged at boot would otherwise leave the mouse at the modified rate with no
                // retry path. Retry on subsequent ticks instead.
                Recovery? saved=null;
                try{saved=JsonSerializer.Deserialize<Recovery>(File.ReadAllText(Journal));}catch{ }
                if(saved==null || string.IsNullOrWhiteSpace(saved.Path))
                {
                    // Corrupt journal: nothing restorable — drop it rather than faulting forever.
                    try{File.Delete(Journal);}catch{ }
                    Status="恢复日志损坏，已忽略";
                }
                else
                {
                    try
                    {
                        SetRate(saved.Path,saved.Rate);File.Delete(Journal);restoreRetries=0;Status="已恢复上次运行前的回报率";
                    }
                    catch
                    {
                        if(++restoreRetries>=5)
                        {
                            try{File.Delete(Journal);}catch{ }
                            restoreRetries=0;
                            Status="回报率恢复已放弃（接收器持续不可达）；如需调整请在厂商软件中设置";
                        }
                        else
                        {
                            Status=$"回报率恢复待重试（{restoreRetries}/5）";
                        }
                    }
                }
            }
            var settings=AppSettings.Load();
            if(!settings.EnableGamePolling || faulted){Restore();return;}
            if(settings.DesktopPollingHz is not (125 or 250 or 500 or 1000) || settings.GamePollingHz is not (125 or 250 or 500 or 1000))throw new IOException("当前接收器仅开放 125–1000Hz");
            var scene=IsGame(settings.PollingGames);
            stableCount=lastScene==scene?stableCount+1:1;lastScene=scene;
            if(stableCount<2)return;
            if(activePath==null)
            {
                var devices=await DeviceInformation.FindAllAsync("System.Devices.InterfaceClassGuid:=\"{4D1E55B2-F16F-11CF-88CB-001111000030}\"").AsTask().ConfigureAwait(false);
                var device=devices.FirstOrDefault(d=>d.Id.Contains("vid_1532&pid_00b7&mi_00#",StringComparison.OrdinalIgnoreCase));
                if(device==null){Status="未找到已验证的 00B7 接收器";return;}
                originalRate=ReadRate(device.Id);activePath=device.Id;
                if(originalRate is not (125 or 250 or 500 or 1000)){activePath=null;throw new IOException("当前硬件回报率超出本版验证范围");}
                Directory.CreateDirectory(Path.GetDirectoryName(Journal)!);
                File.WriteAllText(Journal,JsonSerializer.Serialize(new Recovery(activePath,originalRate)));
            }
            int target=scene?settings.GamePollingHz:settings.DesktopPollingHz;
            if(ReadRate(activePath)!=target)SetRate(activePath,target);
            Status=$"{(scene?"游戏前台全屏":"桌面")} · {target}Hz；关闭后恢复 {originalRate}Hz";
        }
        catch(Exception ex)
        {
            faulted=true;Status="调度暂停："+ex.Message;
            try{Restore();}catch(Exception restore){Status+="；恢复待重试："+restore.Message;}
        }
        finally{gate.Release();}
    }
    private sealed record Recovery(string Path,int Rate);
    private void Restore()
    {
        if(activePath==null)return;
        SetRate(activePath,originalRate);activePath=null;File.Delete(Journal);
        Status="调度已关闭，已恢复原回报率";
    }
    public static byte[] RateReport(byte command,byte size,byte arg0=0,byte arg1=0)
    {
        var b=new byte[91];b[2]=31;b[6]=size;b[8]=command;b[9]=arg0;b[10]=arg1;
        for(int i=3;i<89;i++)b[89]^=b[i];return b;
    }
    private static byte[] Transfer(string path,byte command,byte size,byte arg0=0,byte arg1=0)
    {
        using var h=CreateFileW(path,0,3,IntPtr.Zero,3,0,IntPtr.Zero);
        if(h.IsInvalid)throw new IOException("无法打开鼠标接口");
        var b=RateReport(command,size,arg0,arg1);
        if(!HidD_SetFeature(h,b,b.Length))throw new IOException("回报率请求失败");
        Thread.Sleep(60);var r=new byte[91];
        if(!HidD_GetFeature(h,r,r.Length))throw new IOException("回报率响应失败");
        byte sum=0;for(int i=3;i<89;i++)sum^=r[i];
        if(r[0]!=0 || r[1]!=2 || r[2]!=31 || r[6]!=size || r[7]!=0 || r[8]!=command || sum!=r[89])throw new IOException("回报率响应验证失败");
        return r;
    }
    public static int ReadRate(string path)
    {
        lock(NativeRazerBattery.IoGate)
        {
            var r=Transfer(path,0xc0,1);
            return r[10] switch {1=>8000,2=>4000,4=>2000,8=>1000,16=>500,32=>250,64=>125,_=>throw new IOException("未知回报率编码")};
        }
    }
    public static void SetRate(string path,int hz)
    {
        if(!path.Contains("vid_1532&pid_00b7&mi_00#",StringComparison.OrdinalIgnoreCase))throw new IOException("尚未验证该接收器");
        byte code=hz switch {125=>64,250=>32,500=>16,1000=>8,_=>throw new IOException("当前接收器未开放此回报率")};
        lock(NativeRazerBattery.IoGate)
        {
            Transfer(path,0x40,2,1,code);
            if(ReadRate(path)!=hz)throw new IOException("回报率回读不一致");
        }
    }
    internal static bool IsGame(string names)
    {
        var hwnd=GetForegroundWindow();if(hwnd==IntPtr.Zero)return false;
        GetWindowThreadProcessId(hwnd,out uint pid);
        try
        {
            var name=Process.GetProcessById((int)pid).ProcessName;
            if(!names.Split(',',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).Any(x=>string.Equals(Path.GetFileNameWithoutExtension(x),name,StringComparison.OrdinalIgnoreCase)))return false;
            if(!GetWindowRect(hwnd,out var rect))return false;
            var info=new MonitorInfo{Size=Marshal.SizeOf<MonitorInfo>()};
            if(!GetMonitorInfoW(MonitorFromWindow(hwnd,2),ref info))return false;
            return Math.Abs(rect.Left-info.Monitor.Left)<=2 && Math.Abs(rect.Top-info.Monitor.Top)<=2 && Math.Abs(rect.Right-info.Monitor.Right)<=2 && Math.Abs(rect.Bottom-info.Monitor.Bottom)<=2;
        }
        catch{return false;}
    }
    public void Dispose()
    {
        disposed=true;timer?.Dispose();gate.Wait();
        try{Restore();}catch(Exception ex){Status="恢复待下次启动重试："+ex.Message;}finally{gate.Release();}
    }
    [StructLayout(LayoutKind.Sequential)] private struct Rect{public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo{public int Size;public Rect Monitor,Work;public uint Flags;}
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h,out Rect rect);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr h,uint flags);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern bool GetMonitorInfoW(IntPtr h,ref MonitorInfo info);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] private static extern SafeFileHandle CreateFileW(string path,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
    [DllImport("hid.dll")] [return:MarshalAs(UnmanagedType.U1)] private static extern bool HidD_SetFeature(SafeFileHandle h,byte[] b,int size);
    [DllImport("hid.dll")] [return:MarshalAs(UnmanagedType.U1)] private static extern bool HidD_GetFeature(SafeFileHandle h,byte[] b,int size);
}
