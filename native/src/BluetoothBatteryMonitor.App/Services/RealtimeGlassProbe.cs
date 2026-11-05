using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using BluetoothBatteryMonitor.App.Services.System;
using BluetoothBatteryMonitor.App.Models;
using BluetoothBatteryMonitor.App.ViewModels;

namespace BluetoothBatteryMonitor.App.Services;

/// <summary>Controlled local desktop test. Only synthetic solid colours and numeric results
/// are recorded; no desktop captures are saved to disk.</summary>
public static class RealtimeGlassProbe
{
    public static async Task Run(string reportPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
        var background=new Window {Title="Realtime glass test background",Width=700,Height=550,
            Left=SystemParameters.WorkArea.Right-740,Top=SystemParameters.WorkArea.Bottom-590,
            WindowStyle=WindowStyle.None,Style=new Style(typeof(Window)),AllowsTransparency=false,ShowInTaskbar=false,Topmost=true,Background=new SolidColorBrush(Color.FromRgb(208,32,48))};
        var testCanvas=new global::System.Windows.Controls.Border{Background=new SolidColorBrush(Color.FromRgb(208,32,48))};
        background.Content=testCanvas;
        var rows=new[]{new BluetoothDeviceModel{Id="synthetic-probe",Name="Live refraction test",Type=DeviceType.Mouse,BatteryLevel=88,IsConnected=true}};
        var window=new Views.FlyoutWindow(null!){DisableAutoClose=true,ShowInTaskbar=false,
            DataContext=new{Devices=rows.Select(r=>new DeviceItemViewModel(r)).ToArray(),HasDevices=true,StatusText="Synthetic backdrop test"}};
        try
        {
            ThemeManager.ApplyTheme(ThemeManager.AppearanceGlass,true);
            background.Show();window.Show();window.Left=background.Left+60;window.Top=background.Top+70;
            GlassAppearance.ApplyWindow(window);

            var capture=window.RealtimeGlass;
            await WaitFor(()=>capture.FrameCount>=5||capture.LastError!=null);
            if(capture.LastError!=null)throw new InvalidOperationException(capture.LastError);
            var red=ReadSource(capture.CurrentFrame!);
            if(red[0]<180||red[1]>55||red[2]>70)throw new InvalidOperationException($"Self-exclusion failed: expected red background, got {string.Join(',',red)}; capture={capture.CurrentFrame}; popup={window.Left},{window.Top},{window.ActualWidth},{window.ActualHeight}; background={background.Left},{background.Top},{background.ActualWidth},{background.ActualHeight}; pixels={Describe(capture.CurrentFrame!)}");
            var renderedRed=ReadRendered(window);
            long before=capture.FrameCount;
            testCanvas.Background=new SolidColorBrush(Color.FromRgb(32,80,208));
            await WaitFor(()=>capture.FrameCount>=before+5);
            var blue=ReadSource(capture.CurrentFrame!);
            if(blue[2]<180||blue[0]>55)throw new InvalidOperationException($"Background did not update: {string.Join(',',blue)}");
            var renderedBlue=ReadRendered(window);
            if(renderedRed[0]<=renderedRed[2]+60||renderedBlue[2]<=renderedBlue[0]+60)
                throw new InvalidOperationException($"Shader did not display live source pixels: red={string.Join(',',renderedRed)},blue={string.Join(',',renderedBlue)}");
            long geometryFrame=capture.FrameCount;int oldWidth=capture.CurrentFrame!.Width;
            window.Width+=30;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            window.Left=background.Left+90;window.Top=background.Top+80;
            await WaitFor(()=>capture.FrameCount>=geometryFrame+5&&capture.CurrentFrame!.Width!=oldWidth);
            var moved=ReadRendered(window);
            if(moved[2]<=moved[0]+60)throw new InvalidOperationException("Capture lost alignment after movement/resize");
            double dpiScale=VisualTreeHelper.GetDpi(window).DpiScaleX;
            var process=Process.GetCurrentProcess();
            var cpu=process.TotalProcessorTime;long first=capture.FrameCount;var stopwatch=Stopwatch.StartNew();
            await Task.Delay(2000);
            double wallMs=stopwatch.Elapsed.TotalMilliseconds;
            double cpuMs=(process.TotalProcessorTime-cpu).TotalMilliseconds;
            double fps=(capture.FrameCount-first)*1000/wallMs;
            double captureMs=capture.MeanCaptureMilliseconds;
            window.Hide();await capture.PendingWorker;
            long hidden=capture.FrameCount;await Task.Delay(180);
            if(capture.IsRunning||capture.FrameCount!=hidden)throw new InvalidOperationException("Capture kept running while hidden");
            GetWindowDisplayAffinity(new global::System.Windows.Interop.WindowInteropHelper(window).Handle,out uint hiddenAffinity);
            if(hiddenAffinity!=0)throw new InvalidOperationException("Window capture exclusion was not restored");
            window.Show();await WaitFor(()=>capture.FrameCount>=hidden+3);
            ThemeManager.ApplyTheme(ThemeManager.AppearanceNormal,true);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            await capture.PendingWorker;
            if(capture.IsRunning)throw new InvalidOperationException("Capture kept running after material switch");
            await File.WriteAllTextAsync(reportPath,JsonSerializer.Serialize(new{success=true,
                exclusionVerified=true,realtimeBackgroundVerified=true,shaderOutputVerified=true,
                stopsWhenHidden=true,restartsWhenShown=true,stopsWhenMaterialChanges=true,moveAndResizeVerified=true,dpiScale,
                sourceRed=red,sourceBlue=blue,renderedRed,renderedBlue,
                fps,meanCaptureMilliseconds=captureMs,cpuPercentOfOneCore=cpuMs/wallMs*100,
                cpuPercentOfMachine=cpuMs/wallMs*100/Environment.ProcessorCount,
                syntheticData=true,renderTier=RenderCapability.Tier>>16},new JsonSerializerOptions{WriteIndented=true}));
            File.Delete(reportPath+"-error.txt");
        }
        finally{window.Close();background.Close();ThemeManager.ApplyTheme(AppSettings.Load().Theme);}
    }
    private static async Task WaitFor(Func<bool> condition)
    {
        var watch=Stopwatch.StartNew();
        while(!condition())
        {
            if(watch.ElapsedMilliseconds>6000)throw new TimeoutException("Realtime glass probe timed out");
            await Task.Delay(40);
        }
    }
    private static string Describe(GlassBackdropFrame frame)
    {
        var source=(BitmapSource)frame.Brush.ImageSource;var data=new byte[frame.Width*frame.Height*4];source.CopyPixels(data,frame.Width*4,0);
        return $"max={data.Max()},nonzero={data.Count(x=>x>0)},quarter={string.Join(',',data.Skip(((frame.Height/4)*frame.Width+frame.Width/4)*4).Take(4))}";
    }
    private static int[] ReadSource(GlassBackdropFrame frame)
    {
        var source=(BitmapSource)frame.Brush.ImageSource;
        var pixel=new byte[4];source.CopyPixels(new Int32Rect(frame.Width/2,frame.Height/2,1,1),pixel,4,0);
        return new[]{(int)pixel[2],pixel[1],pixel[0]};
    }
    private static int[] ReadRendered(Window window)
    {
        window.UpdateLayout();
        var source=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);source.Render(window);
        var pixel=new byte[4];source.CopyPixels(new Int32Rect((int)(window.ActualWidth*.64),25,1,1),pixel,4,0);
        return new[]{(int)pixel[2],pixel[1],pixel[0]};
    }
    [DllImport("user32.dll")][return:MarshalAs(UnmanagedType.Bool)]private static extern bool GetWindowDisplayAffinity(IntPtr hwnd,out uint affinity);
}
