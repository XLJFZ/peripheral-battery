using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace BluetoothBatteryMonitor.App.Services.System;

public sealed class LiquidRefractionEffect : ShaderEffect
{
    private static readonly PixelShader Shader = LoadShader();
    private static PixelShader LoadShader()
    {
        // Load directly: pack URI registration otherwise depends on whether a Window
        // has been created first (not guaranteed for an isolated render/test).
        using var stream=typeof(LiquidRefractionEffect).Assembly.GetManifestResourceStream("LiquidRefraction.ps")
            ?? throw new InvalidOperationException("Embedded refraction shader is missing");
        var shader=new PixelShader();shader.SetStreamSource(stream);
        shader.Freeze();return shader;
    }
    public static readonly DependencyProperty InputProperty = RegisterPixelShaderSamplerProperty(nameof(Input),typeof(LiquidRefractionEffect),0);
    public static readonly DependencyProperty SurfaceSizeProperty = DependencyProperty.Register(nameof(SurfaceSize),typeof(Point),typeof(LiquidRefractionEffect),new UIPropertyMetadata(new Point(416,240),PixelShaderConstantCallback(0)));
    public static readonly DependencyProperty RadiusProperty = DependencyProperty.Register(nameof(Radius),typeof(double),typeof(LiquidRefractionEffect),new UIPropertyMetadata(8.0,PixelShaderConstantCallback(1)));
    public static readonly DependencyProperty CaptureOriginProperty = DependencyProperty.Register(nameof(CaptureOrigin),typeof(Point),typeof(LiquidRefractionEffect),new UIPropertyMetadata(new Point(0,0),PixelShaderConstantCallback(2)));
    public static readonly DependencyProperty CaptureScaleProperty = DependencyProperty.Register(nameof(CaptureScale),typeof(Point),typeof(LiquidRefractionEffect),new UIPropertyMetadata(new Point(1,1),PixelShaderConstantCallback(3)));
    public static readonly DependencyProperty AmountProperty = DependencyProperty.Register(nameof(Amount),typeof(double),typeof(LiquidRefractionEffect),new UIPropertyMetadata(12.0,PixelShaderConstantCallback(4)));
    public static readonly DependencyProperty DarkProperty = DependencyProperty.Register(nameof(Dark),typeof(double),typeof(LiquidRefractionEffect),new UIPropertyMetadata(1.0,PixelShaderConstantCallback(5)));
    public Brush Input {get=>(Brush)GetValue(InputProperty);set=>SetValue(InputProperty,value);}
    public Point SurfaceSize {get=>(Point)GetValue(SurfaceSizeProperty);set=>SetValue(SurfaceSizeProperty,value);}
    public double Radius {get=>(double)GetValue(RadiusProperty);set=>SetValue(RadiusProperty,value);}
    public Point CaptureOrigin {get=>(Point)GetValue(CaptureOriginProperty);set=>SetValue(CaptureOriginProperty,value);}
    public Point CaptureScale {get=>(Point)GetValue(CaptureScaleProperty);set=>SetValue(CaptureScaleProperty,value);}
    public double Amount {get=>(double)GetValue(AmountProperty);set=>SetValue(AmountProperty,value);}
    public double Dark {get=>(double)GetValue(DarkProperty);set=>SetValue(DarkProperty,value);}
    public LiquidRefractionEffect()
    {
        PixelShader=Shader;
        foreach(var dp in new[]{InputProperty,SurfaceSizeProperty,RadiusProperty,CaptureOriginProperty,CaptureScaleProperty,AmountProperty,DarkProperty})UpdateShaderValue(dp);
    }
}
