extern alias MonitorApp;
using System;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;
using GlassBorder = MonitorApp::BluetoothBatteryMonitor.App.Views.LiquidGlassBorder;

public class LiquidGlassRenderingTests
{
    [Theory]
    [InlineData(160,80)]
    [InlineData(320,140)]
    [InlineData(416,96)]
    public void LiquidEdgeFadesInwardWithoutASecondContour(int width,int height)
    {
        RunSta(() =>
        {
            var glass=new GlassBorder { Width=width,Height=height,CornerRadius=new CornerRadius(12),
                LiquidLighting=true,Background=Brushes.Transparent,BorderBrush=Brushes.Lime,BorderThickness=new Thickness(3) };
            var pixels=Render(glass,width,height);
            int Alpha(int x,int y)=>pixels[(y*width+x)*4+3];
            // A second inset stroke causes a new alpha peak at depths 2–5.
            for(int y=1;y<10;y++)Assert.True(Alpha(width/2,y+1)<=Alpha(width/2,y)+1,
                $"Extra contour at depth {y+1}: {Alpha(width/2,y)} -> {Alpha(width/2,y+1)}");
            Assert.Equal(0,Alpha(0,0));
            Assert.InRange(Alpha(width/2,height/2),0,20);
            // The normal Border stroke must not be superimposed on the liquid outline.
            int i=(width+width/2)*4;
            Assert.InRange(pixels[i+1]-pixels[i+2],-15,15);
        });
    }
    [Fact]
    public void NormalMaterialRetainsItsOriginalBorder()
    {
        RunSta(() =>
        {
            var glass=new GlassBorder { Width=160,Height=80,CornerRadius=new CornerRadius(12),
                LiquidLighting=false,Background=Brushes.Transparent,BorderBrush=Brushes.Lime,BorderThickness=new Thickness(3) };
            var pixels=Render(glass,160,80);
            int i=(160+80)*4;
            Assert.Equal(255,pixels[i+1]);
            Assert.Equal(0,pixels[i+2]);
        });
    }
    [Fact]
    public void VerticalLightReachesBothEdgesAndCrossesCentreContinuously()
    {
        RunSta(()=>
        {
            byte[] At(double y)
            {
                var glass=new GlassBorder{Width=160,Height=80,CornerRadius=new CornerRadius(12),
                    LiquidLighting=true,Background=Brushes.Transparent};
                var flags=global::System.Reflection.BindingFlags.NonPublic|global::System.Reflection.BindingFlags.Static;
                var light=(DependencyProperty)typeof(GlassBorder).GetField("LightPositionProperty",flags)!.GetValue(null)!;
                var blend=(DependencyProperty)typeof(GlassBorder).GetField("HoverBlendProperty",flags)!.GetValue(null)!;
                glass.SetValue(light,new Point(.5,y));glass.SetValue(blend,1.0);
                return Render(glass,160,80);
            }
            var top=At(.05);var bottom=At(.95);
            int upper=(160+80)*4+3,lower=(78*160+80)*4+3;
            Assert.True(top[upper]>top[lower]+20,"Top light did not reach the upper edge");
            Assert.True(bottom[lower]>bottom[upper]+20,"Bottom light did not reach the lower edge");
            var before=At(.49);var after=At(.51);
            for(int i=0;i<before.Length;i++)Assert.InRange(Math.Abs(before[i]-after[i]),0,12);
        });
    }
    [Fact]
    public void PointerOnlyLightsNearbySectionOfAnEdge()
    {
        RunSta(()=>
        {
            var glass=new GlassBorder{Width=400,Height=120,CornerRadius=new CornerRadius(12),
                LiquidLighting=true,Background=Brushes.Transparent};
            var flags=global::System.Reflection.BindingFlags.NonPublic|global::System.Reflection.BindingFlags.Static;
            glass.SetValue((DependencyProperty)typeof(GlassBorder).GetField("LightPositionProperty",flags)!.GetValue(null)!,new Point(.2,.02));
            glass.SetValue((DependencyProperty)typeof(GlassBorder).GetField("HoverBlendProperty",flags)!.GetValue(null)!,1.0);
            var pixels=Render(glass,400,120);
            int Alpha(int x,int y)=>pixels[(y*400+x)*4+3];
            Assert.True(Alpha(80,1)>Alpha(300,1)+30);
            Assert.InRange(Alpha(300,1),0,10);
            Assert.InRange(Alpha(80,118),0,10);
        });
    }
    [Fact]
    public void MaterialToggleRearmsHoverOnNextPointerUpdate()
    {
        RunSta(()=>
        {
            var glass=new GlassBorder{Width=160,Height=80,LiquidLighting=true};
            var flags=global::System.Reflection.BindingFlags.NonPublic|global::System.Reflection.BindingFlags.Instance;
            var update=typeof(GlassBorder).GetMethod("UpdatePointer",flags)!;
            var active=typeof(GlassBorder).GetField("_hoverActive",flags)!;
            update.Invoke(glass,new object[]{new Point(.4,.3)});
            Assert.True((bool)active.GetValue(glass)!);
            glass.LiquidLighting=false;glass.LiquidLighting=true;
            Assert.False((bool)active.GetValue(glass)!);
            update.Invoke(glass,new object[]{new Point(.6,.7)});
            Assert.True((bool)active.GetValue(glass)!);
            var light=(DependencyProperty)typeof(GlassBorder).GetField("LightPositionProperty",global::System.Reflection.BindingFlags.NonPublic|global::System.Reflection.BindingFlags.Static)!.GetValue(null)!;
            Assert.Equal(new Point(.6,.7),(Point)glass.GetValue(light));
        });
    }
    private static byte[] Render(GlassBorder glass,int width,int height)
    {
        glass.Measure(new Size(width,height));glass.Arrange(new Rect(0,0,width,height));glass.UpdateLayout();
        var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(glass);
        var pixels=new byte[width*height*4];bitmap.CopyPixels(pixels,width*4,0);return pixels;
    }
    private static void RunSta(Action action)
    {
        Exception? error=null;
        var thread=new Thread(()=>{try{action();}catch(Exception ex){error=ex;}}){IsBackground=true};
        thread.SetApartmentState(ApartmentState.STA);thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)),"Glass render timed out");
        if(error!=null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }
}
