extern alias MonitorApp;
using System;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;
using Refraction=MonitorApp::BluetoothBatteryMonitor.App.Services.System.LiquidRefractionEffect;

public class LiquidGlassRefractionTests
{
    [Fact]
    public void ShaderDisplacesRealEdgePixelsButKeepsTheCentreFixed()
    {
        RunSta(()=>
        {
            var pixels=new byte[128*128*4];
            for(int y=0;y<128;y++)for(int x=0;x<128;x++)
            {int i=(y*128+x)*4;pixels[i]=128;pixels[i+1]=(byte)(y*2);pixels[i+2]=(byte)(x*2);pixels[i+3]=255;}
            var image=BitmapSource.Create(128,128,96,96,PixelFormats.Bgra32,null,pixels,128*4);
            var brush=new ImageBrush(image){Stretch=Stretch.Fill};
            var flat=Render(brush,0);var bent=Render(brush,12);
            int edge=(32*64+5)*4+2,centre=(32*64+32)*4+2;
            Assert.True(bent[edge]>flat[edge]+8,$"Edge was not refracted: {flat[edge]} -> {bent[edge]}");
            Assert.InRange(Math.Abs(bent[centre]-flat[centre]),0,1);
        });
    }
    private static byte[] Render(Brush input,double amount)
    {
        var effect=new Refraction{Input=input,SurfaceSize=new Point(64,64),Radius=12,
            CaptureOrigin=new Point(.25,.25),CaptureScale=new Point(.5,.5),Dark=0,Amount=amount};
        var visual=new DrawingVisual{Effect=effect};
        using(var dc=visual.RenderOpen())dc.DrawRectangle(Brushes.White,null,new Rect(0,0,64,64));
        var bitmap=new RenderTargetBitmap(64,64,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);
        var result=new byte[64*64*4];bitmap.CopyPixels(result,64*4,0);return result;
    }
    private static void RunSta(Action action) => BluetoothBatteryMonitor.Tests.WpfTestHost.Run(action);
}
