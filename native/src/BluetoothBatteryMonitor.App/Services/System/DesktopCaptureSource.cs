using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace BluetoothBatteryMonitor.App.Services.System;

/// <summary>Windows Graphics Capture + GPU-side crop. Only the small flyout region is
/// mapped to CPU memory. COM resources belong to the single capture worker.</summary>
internal sealed class DesktopCaptureSource : IDisposable
{
    private IntPtr _device,_context,_staging,_monitor;
    private IDirect3DDevice? _winrtDevice;
    private Direct3D11CaptureFramePool? _pool;
    private GraphicsCaptureSession? _session;
    private int _stagingWidth,_stagingHeight;
    private byte[]? _pixels;
    private NativeRect _monitorRect;
    public DesktopCaptureSource()
    {
        if(!GraphicsCaptureSession.IsSupported())throw new NotSupportedException("Windows Graphics Capture unavailable");
        IntPtr dxgi=IntPtr.Zero,inspectable=IntPtr.Zero;
        try
        {
            Check(D3D11CreateDevice(IntPtr.Zero,1,IntPtr.Zero,0x20,IntPtr.Zero,0,7,out _device,out _,out _context));
            var iid=new Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c");Check(Marshal.QueryInterface(_device,ref iid,out dxgi));
            Check(CreateDirect3D11DeviceFromDXGIDevice(dxgi,out inspectable));
            _winrtDevice=MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
        }
        catch{Dispose();throw;}
        finally{Release(ref dxgi);Release(ref inspectable);}
    }
    public (int Left,int Top,int Width,int Height,byte[] Pixels)? Capture(IntPtr hwnd)
    {
        var monitor=MonitorFromWindow(hwnd,2);
        if(monitor==IntPtr.Zero)throw new InvalidOperationException("No display to capture");
        if(monitor!=_monitor)StartMonitor(monitor);
        GetClientRect(hwnd,out var client);var origin=new NativePoint();ClientToScreen(hwnd,ref origin);
        const int padding=24;
        int left=Math.Max(origin.X-padding,_monitorRect.Left),top=Math.Max(origin.Y-padding,_monitorRect.Top);
        int right=Math.Min(origin.X+client.Right+padding,_monitorRect.Right),bottom=Math.Min(origin.Y+client.Bottom+padding,_monitorRect.Bottom);
        int width=right-left,height=bottom-top;
        if(width<=0||height<=0||width>2560||height>2160)throw new InvalidOperationException("Capture crop outside supported bounds");
        using var frame=_pool!.TryGetNextFrame();
        if(frame==null)return null;
        // The monitor can change resolution while open. Recreate instead of reading stale dimensions.
        if(frame.ContentSize.Width!=_monitorRect.Right-_monitorRect.Left||frame.ContentSize.Height!=_monitorRect.Bottom-_monitorRect.Top)
        {StartMonitor(monitor);return null;}
        if(_staging==IntPtr.Zero||width!=_stagingWidth||height!=_stagingHeight)
        {
            Release(ref _staging);
            var desc=new TextureDesc{Width=(uint)width,Height=(uint)height,MipLevels=1,ArraySize=1,Format=87,SampleCount=1,Usage=3,CpuAccessFlags=0x20000};
            Check(Call<CreateTexture2D>(_device,5)(_device,ref desc,IntPtr.Zero,out _staging));
            _stagingWidth=width;_stagingHeight=height;_pixels=new byte[width*height*4];
        }
        IntPtr surface=MarshalInterface<IDirect3DSurface>.FromManaged(frame.Surface),access=IntPtr.Zero,texture=IntPtr.Zero;
        try
        {
            var iid=new Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1");Check(Marshal.QueryInterface(surface,ref iid,out access));
            iid=new Guid("6f15aaf2-d208-4e89-9ab4-489535d34f9c");Check(Call<GetInterface>(access,3)(access,ref iid,out texture));
            var box=new Box{Left=(uint)(left-_monitorRect.Left),Top=(uint)(top-_monitorRect.Top),Right=(uint)(right-_monitorRect.Left),Bottom=(uint)(bottom-_monitorRect.Top),Back=1};
            Call<CopySubresourceRegion>(_context,46)(_context,_staging,0,0,0,0,texture,0,ref box);
            Check(Call<Map>(_context,14)(_context,_staging,0,1,0,out var mapped));
            try
            {
                for(int y=0;y<height;y++)Marshal.Copy(IntPtr.Add(mapped.Data,y*(int)mapped.RowPitch),_pixels!,y*width*4,width*4);
                return(left,top,width,height,_pixels!);
            }
            finally{Call<Unmap>(_context,15)(_context,_staging,0);}
        }
        finally{Release(ref texture);Release(ref access);Release(ref surface);}
    }
    private void StartMonitor(IntPtr monitor)
    {
        _session?.Dispose();_pool?.Dispose();_session=null;_pool=null;
        var info=new MonitorInfo{Size=Marshal.SizeOf<MonitorInfo>()};
        if(!GetMonitorInfo(monitor,ref info))throw new InvalidOperationException("Unable to query display bounds");
        _monitorRect=info.Monitor;
        IntPtr name=IntPtr.Zero,factory=IntPtr.Zero,itemPtr=IntPtr.Zero;
        try
        {
            const string className="Windows.Graphics.Capture.GraphicsCaptureItem";
            Check(WindowsCreateString(className,className.Length,out name));
            var iid=new Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");Check(RoGetActivationFactory(name,ref iid,out factory));
            iid=new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");Check(Call<CreateForMonitor>(factory,4)(factory,monitor,ref iid,out itemPtr));
            var item=MarshalInterface<GraphicsCaptureItem>.FromAbi(itemPtr);
            _pool=Direct3D11CaptureFramePool.CreateFreeThreaded(_winrtDevice!,DirectXPixelFormat.B8G8R8A8UIntNormalized,2,item.Size);
            _session=_pool.CreateCaptureSession(item);
            if(global::System.OperatingSystem.IsWindowsVersionAtLeast(10,0,19041))_session.IsCursorCaptureEnabled=false;
            _session.StartCapture();_monitor=monitor;
        }
        finally{Release(ref itemPtr);Release(ref factory);if(name!=IntPtr.Zero)WindowsDeleteString(name);}
    }
    public void Dispose()
    {
        _session?.Dispose();_pool?.Dispose();_winrtDevice?.Dispose();
        Release(ref _staging);Release(ref _context);Release(ref _device);
    }
    private static T Call<T>(IntPtr instance,int slot)where T:Delegate=>Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance),slot*IntPtr.Size));
    private static void Check(int hr){if(hr<0)Marshal.ThrowExceptionForHR(hr);}
    private static void Release(ref IntPtr value){if(value!=IntPtr.Zero){Marshal.Release(value);value=IntPtr.Zero;}}
    [StructLayout(LayoutKind.Sequential)]private struct NativePoint{public int X,Y;}
    [StructLayout(LayoutKind.Sequential)]private struct NativeRect{public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)]private struct MonitorInfo{public int Size;public NativeRect Monitor,Work;public uint Flags;}
    [StructLayout(LayoutKind.Sequential)]private struct TextureDesc{public uint Width,Height,MipLevels,ArraySize,Format,SampleCount,SampleQuality,Usage,BindFlags,CpuAccessFlags,MiscFlags;}
    [StructLayout(LayoutKind.Sequential)]private struct Box{public uint Left,Top,Front,Right,Bottom,Back;}
    [StructLayout(LayoutKind.Sequential)]private struct Mapped{public IntPtr Data;public uint RowPitch,DepthPitch;}
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int CreateTexture2D(IntPtr self,ref TextureDesc desc,IntPtr initial,out IntPtr texture);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int GetInterface(IntPtr self,ref Guid iid,out IntPtr result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int CreateForMonitor(IntPtr self,IntPtr monitor,ref Guid iid,out IntPtr result);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate void CopySubresourceRegion(IntPtr self,IntPtr destination,uint dstSub,uint x,uint y,uint z,IntPtr source,uint sourceSub,ref Box box);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate int Map(IntPtr self,IntPtr resource,uint sub,uint type,uint flags,out Mapped mapped);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]private delegate void Unmap(IntPtr self,IntPtr resource,uint sub);
    [DllImport("d3d11.dll")]private static extern int D3D11CreateDevice(IntPtr adapter,int driverType,IntPtr software,uint flags,IntPtr featureLevels,uint featureCount,uint sdk,out IntPtr device,out uint level,out IntPtr context);
    [DllImport("d3d11.dll")]private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr device,out IntPtr result);
    [DllImport("combase.dll",CharSet=CharSet.Unicode)]private static extern int WindowsCreateString(string text,int length,out IntPtr result);
    [DllImport("combase.dll")]private static extern int WindowsDeleteString(IntPtr value);
    [DllImport("combase.dll")]private static extern int RoGetActivationFactory(IntPtr name,ref Guid iid,out IntPtr result);
    [DllImport("user32.dll")]private static extern IntPtr MonitorFromWindow(IntPtr hwnd,uint flags);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorInfo info);
    [DllImport("user32.dll")]private static extern bool GetClientRect(IntPtr hwnd,out NativeRect rect);
    [DllImport("user32.dll")]private static extern bool ClientToScreen(IntPtr hwnd,ref NativePoint point);
}
