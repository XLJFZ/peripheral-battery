using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Drawing;
using System.Drawing.Imaging;

namespace BluetoothBatteryMonitor.App.Services.System;

/// <summary>Samples only the flyout's desktop region, without creating a Windows
/// Graphics Capture session (which displays a yellow monitor border).
/// The caller must exclude the flyout from capture before sampling.</summary>
internal sealed class DesktopCaptureSource : IDisposable
{
    private Bitmap? _bitmap;
    private Graphics? _graphics;
    private byte[]? _pixels;

    public (int Left,int Top,int Width,int Height,byte[] Pixels)? Capture(IntPtr hwnd)
    {
        var monitor=MonitorFromWindow(hwnd,2);
        var info=new MonitorInfo{Size=Marshal.SizeOf<MonitorInfo>()};
        if(monitor==IntPtr.Zero||!GetMonitorInfo(monitor,ref info)||
            !GetClientRect(hwnd,out var client))throw new Win32Exception();
        var origin=new NativePoint();
        if(!ClientToScreen(hwnd,ref origin))throw new Win32Exception();
        const int padding=24;
        int left=Math.Max(origin.X-padding,info.Monitor.Left),top=Math.Max(origin.Y-padding,info.Monitor.Top);
        int right=Math.Min(origin.X+client.Right+padding,info.Monitor.Right),bottom=Math.Min(origin.Y+client.Bottom+padding,info.Monitor.Bottom);
        int width=right-left,height=bottom-top;
        if(width<=0||height<=0||width>2560||height>2160)throw new InvalidOperationException("Capture crop outside supported bounds");
        if(_bitmap==null||_bitmap.Width!=width||_bitmap.Height!=height)
        {
            Dispose();
            _bitmap=new Bitmap(width,height,PixelFormat.Format32bppRgb);
            _graphics=Graphics.FromImage(_bitmap);
            _pixels=new byte[width*height*4];
        }
        // Copy only this small region. CaptureBlt includes desktop overlays, while
        // WDA_EXCLUDEFROMCAPTURE keeps our own glass surface out of the result.
        IntPtr desktop=GetDC(IntPtr.Zero),destination=_graphics!.GetHdc();
        try
        {
            if(desktop==IntPtr.Zero||!BitBlt(destination,0,0,width,height,desktop,left,top,0x40CC0020))throw new Win32Exception();
        }
        finally{_graphics.ReleaseHdc(destination);if(desktop!=IntPtr.Zero)ReleaseDC(IntPtr.Zero,desktop);}
        var data=_bitmap.LockBits(new Rectangle(0,0,width,height),ImageLockMode.ReadOnly,PixelFormat.Format32bppRgb);
        try
        {
            for(int y=0;y<height;y++)Marshal.Copy(IntPtr.Add(data.Scan0,y*data.Stride),_pixels!,y*width*4,width*4);
        }
        finally{_bitmap.UnlockBits(data);}
        return(left,top,width,height,_pixels!);
    }
    public void Dispose()
    {
        _graphics?.Dispose();_graphics=null;
        _bitmap?.Dispose();_bitmap=null;_pixels=null;
    }
    [StructLayout(LayoutKind.Sequential)]private struct NativePoint{public int X,Y;}
    [StructLayout(LayoutKind.Sequential)]private struct NativeRect{public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)]private struct MonitorInfo{public int Size;public NativeRect Monitor,Work;public uint Flags;}
    [DllImport("user32.dll")]private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")]private static extern int ReleaseDC(IntPtr hwnd,IntPtr dc);
    [DllImport("gdi32.dll",SetLastError=true)]private static extern bool BitBlt(IntPtr destination,int x,int y,int width,int height,IntPtr source,int sourceX,int sourceY,uint operation);
    [DllImport("user32.dll")]private static extern IntPtr MonitorFromWindow(IntPtr hwnd,uint flags);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorInfo info);
    [DllImport("user32.dll",SetLastError=true)]private static extern bool GetClientRect(IntPtr hwnd,out NativeRect rect);
    [DllImport("user32.dll",SetLastError=true)]private static extern bool ClientToScreen(IntPtr hwnd,ref NativePoint point);
}
