using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace BluetoothBatteryMonitor.App.Services.System;

public sealed record GlassBackdropFrame(ImageBrush Brush,int Left,int Top,int Width,int Height);

/// <summary>Crops a Windows Graphics Capture display frame to the visible flyout. No files, network, or
/// persisted frames. One worker and one awaited UI upload prevent a backlog of frames.</summary>
public sealed class RealtimeGlassCapture : IDisposable
{
    private readonly Window _window;
    private CancellationTokenSource? _cancellation;
    private Task _worker = Task.CompletedTask;
    private IntPtr _handle;
    private uint _originalAffinity;
    private bool _excluded;
    private bool _disposed;
    private WriteableBitmap? _image;
    private ImageBrush? _brush;
    public long FrameCount {get;private set;}
    public string? LastError {get;private set;}
    public double MeanCaptureMilliseconds => FrameCount==0?0:_captureMilliseconds/FrameCount;
    private double _captureMilliseconds;
    public GlassBackdropFrame? CurrentFrame {get;private set;}
    public bool IsRunning => _cancellation!=null;
    internal Task PendingWorker => _worker;

    public RealtimeGlassCapture(Window window)
    {
        _window=window;
        window.SourceInitialized+=OnStateChanged;
        window.IsVisibleChanged+=OnVisibilityChanged;
        window.Closed+=OnClosed;
    }
    private void OnStateChanged(object? sender,EventArgs e)=>Update();
    private void OnVisibilityChanged(object sender,DependencyPropertyChangedEventArgs e)=>Update();
    private void OnClosed(object? sender,EventArgs e)=>Dispose();
    public void Update()
    {
        if(_disposed)return;
        bool enabled=GlassAppearance.Liquid&&!GlassAppearance.Reduced&&!SystemParameters.HighContrast&&_window.IsVisible;
        if(!enabled){Stop();return;}
        if(IsRunning)return;
        _handle=new WindowInteropHelper(_window).Handle;
        if(_handle==IntPtr.Zero)return;
        if(!RenderCapability.IsPixelShaderVersionSupported(2,0)){LastError="Pixel shader 2.0 unavailable";return;}
        if(!GetWindowDisplayAffinity(_handle,out _originalAffinity)||!SetWindowDisplayAffinity(_handle,0x11)||
            !GetWindowDisplayAffinity(_handle,out uint actual)||actual!=0x11)
        {
            // Never capture our own surface if exclusion is unavailable.
            SetWindowDisplayAffinity(_handle,_originalAffinity);
            LastError=$"Capture exclusion unavailable ({Marshal.GetLastWin32Error()})";return;
        }
        _excluded=true;LastError=null;
        var cancellation=new CancellationTokenSource();_cancellation=cancellation;
        var previous=_worker;
        _worker=Task.Run(async()=>
        {
            // A quick hide/show cannot create two workers sharing the window's texture.
            try{await previous.ConfigureAwait(false);}catch{ }
            try{await CaptureLoop(cancellation.Token).ConfigureAwait(false);}
            catch(OperationCanceledException){ }
            catch(Exception ex)
            {
                if(!_window.Dispatcher.HasShutdownStarted)
                    await _window.Dispatcher.InvokeAsync(()=>
                    {
                        if(_cancellation==cancellation){LastError=ex.Message;Stop();}
                    },DispatcherPriority.Background);
            }
            finally{cancellation.Dispose();}
        });
    }
    private async Task CaptureLoop(CancellationToken token)
    {
        using var source=new DesktopCaptureSource();
        while(true)
        {
            token.ThrowIfCancellationRequested();
            var elapsed=Stopwatch.StartNew();
            var frame=source.Capture(_handle);
            if(frame==null){await Task.Delay(12,token).ConfigureAwait(false);continue;}
            var (left,top,width,height,pixels)=frame.Value;
            double captureMs=elapsed.Elapsed.TotalMilliseconds;
            await _window.Dispatcher.InvokeAsync(()=>
            {
                if(token.IsCancellationRequested)return;
                if(_image==null||_image.PixelWidth!=width||_image.PixelHeight!=height)
                {
                    _image=new WriteableBitmap(width,height,96,96,PixelFormats.Bgr32,null);
                    _brush=new ImageBrush(_image){Stretch=Stretch.Fill};
                }
                _image.WritePixels(new Int32Rect(0,0,width,height),pixels!,width*4,0);
                // A mutable bitmap updates the shader directly. Rebuild material geometry
                // only after movement/resize, not for every video frame.
                if(CurrentFrame==null||CurrentFrame.Left!=left||CurrentFrame.Top!=top||CurrentFrame.Width!=width||CurrentFrame.Height!=height)
                {
                    CurrentFrame=new GlassBackdropFrame(_brush!,left,top,width,height);
                    _window.SetValue(Views.LiquidGlassBorder.BackdropFrameProperty,CurrentFrame);
                }
                FrameCount++;_captureMilliseconds+=captureMs;
            },DispatcherPriority.Background,token);
            // 24 FPS maximum; sleep on a worker, never block the UI.
            await Task.Delay(Math.Max(1,42-(int)elapsed.ElapsedMilliseconds),token).ConfigureAwait(false);
        }
    }

    public void Stop()
    {
        var cancellation=_cancellation;_cancellation=null;
        cancellation?.Cancel();
        _window.ClearValue(Views.LiquidGlassBorder.BackdropFrameProperty);
        CurrentFrame=null;_image=null;_brush=null;
        if(_excluded){SetWindowDisplayAffinity(_handle,_originalAffinity);_excluded=false;}
    }
    public void Dispose()
    {
        if(_disposed)return;
        _disposed=true;Stop();
        _window.SourceInitialized-=OnStateChanged;
        _window.IsVisibleChanged-=OnVisibilityChanged;
        _window.Closed-=OnClosed;
    }
    [DllImport("user32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool SetWindowDisplayAffinity(IntPtr hwnd,uint affinity);
    [DllImport("user32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool GetWindowDisplayAffinity(IntPtr hwnd,out uint affinity);
}
