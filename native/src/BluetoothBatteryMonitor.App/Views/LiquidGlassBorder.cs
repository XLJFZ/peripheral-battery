using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using BluetoothBatteryMonitor.App.Services.System;

namespace BluetoothBatteryMonitor.App.Views;

/// <summary>Live desktop refraction behind sharp foreground content, with one rounded outline.
/// Falls back to native Acrylic and edge lighting when capture is unavailable.</summary>
public sealed class LiquidGlassBorder : Border
{
    public static readonly DependencyProperty LiquidLightingProperty = DependencyProperty.Register(
        nameof(LiquidLighting), typeof(bool), typeof(LiquidGlassBorder),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender,
            (owner,args)=>((LiquidGlassBorder)owner).ResetMaterialHover((bool)args.NewValue)));
    public bool LiquidLighting
    {
        get => (bool)GetValue(LiquidLightingProperty);
        set => SetValue(LiquidLightingProperty, value);
    }
    private static readonly DependencyProperty LightPositionProperty = DependencyProperty.Register(
        "LightPosition", typeof(Point), typeof(LiquidGlassBorder),
        new FrameworkPropertyMetadata(new Point(.25, 0), FrameworkPropertyMetadataOptions.AffectsRender));
    private static readonly DependencyProperty HoverBlendProperty = DependencyProperty.Register(
        "HoverBlend",typeof(double),typeof(LiquidGlassBorder),
        new FrameworkPropertyMetadata(0.0,FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty BackdropFrameProperty = DependencyProperty.RegisterAttached(
        "BackdropFrame",typeof(GlassBackdropFrame),typeof(LiquidGlassBorder),
        new FrameworkPropertyMetadata(null,FrameworkPropertyMetadataOptions.Inherits,
            (owner,args)=>
            {
                if(owner is not LiquidGlassBorder border)return;
                if(args.NewValue==null)
                {
                    border._background.Effect=null;
                    border._refraction?.ClearValue(LiquidRefractionEffect.InputProperty);
                    using(border._background.RenderOpen()){ }
                }
                border.InvalidateVisual();
            }));
    private readonly ContainerVisual _material=new();
    private readonly DrawingVisual _background=new();
    private readonly DrawingVisual _lighting=new();
    private LiquidRefractionEffect? _refraction;
    private Point _sampleOrigin=new(double.NaN,double.NaN);
    protected override int VisualChildrenCount=>base.VisualChildrenCount+1;
    protected override Visual GetVisualChild(int index)=>index==0?_material:base.GetVisualChild(index-1);
    private bool _hoverActive;
    private Point _target = new(.25, 0);
    private WriteableBitmap? _lens;
    private byte[]? _pixels;
    private Point _cachedLight=new(double.NaN,double.NaN);
    private double _cachedRadius=-1;
    private double _cachedBlend=-1;

    public LiquidGlassBorder()
    {
        _material.Children.Add(_background);_material.Children.Add(_lighting);AddVisualChild(_material);
        LayoutUpdated+=(_,_)=>
        {
            if(GetValue(BackdropFrameProperty)==null||PresentationSource.FromVisual(this)==null)return;
            var origin=PointToScreen(new Point(0,0));
            if(origin!=_sampleOrigin)InvalidateVisual();
        };
        Loaded+=(_,_)=>SynchronizeHover();
        IsVisibleChanged+=(_,e)=>{if(e.NewValue is true)SynchronizeHover();};
        Unloaded += (_, _) =>
        {
            _hoverActive=false;
            BeginAnimation(HoverBlendProperty,null);
            SetCurrentValue(HoverBlendProperty,0.0);
            BeginAnimation(LightPositionProperty, null);
            _target = new Point(.25, 0);
            SetCurrentValue(LightPositionProperty, _target);
            _background.Effect=null;_refraction=null;
            _lens = null;
            _pixels = null;
        };
    }
    private void ResetMaterialHover(bool enabled)
    {
        _hoverActive=false;
        BeginAnimation(HoverBlendProperty,null);SetCurrentValue(HoverBlendProperty,0.0);
        if(enabled)SynchronizeHover();
    }
    private void SynchronizeHover()
    {
        if(!LiquidLighting||!IsMouseOver||ActualWidth<=0||ActualHeight<=0)return;
        var p=Mouse.GetPosition(this);
        UpdatePointer(new Point(Math.Clamp(p.X/ActualWidth,0,1),Math.Clamp(p.Y/ActualHeight,0,1)));
    }
    private void UpdatePointer(Point position)
    {
        MoveLight(position);
        if(!_hoverActive)FadeHover(true);
    }
    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        if(!LiquidLighting||ActualWidth<=0||ActualHeight<=0)return;
        var p=e.GetPosition(this);
        UpdatePointer(new Point(Math.Clamp(p.X/ActualWidth,0,1),Math.Clamp(p.Y/ActualHeight,0,1)));
    }
    private void FadeHover(bool active)
    {
        _hoverActive=active;
        double target=active?1:0;
        if(!SystemParameters.ClientAreaAnimation)
        {
            BeginAnimation(HoverBlendProperty,null);
            SetCurrentValue(HoverBlendProperty,target);
            return;
        }
        // Only enter/leave blend is animated. Pointer updates never restart this clock.
        BeginAnimation(HoverBlendProperty,new DoubleAnimation(target,TimeSpan.FromMilliseconds(active?65:110))
        {EasingFunction=new QuadraticEase{EasingMode=EasingMode.EaseOut}});
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!LiquidLighting || ActualWidth <= 0 || ActualHeight <= 0) return;
        var p = e.GetPosition(this);
        UpdatePointer(new Point(Math.Clamp(p.X / ActualWidth, 0, 1), Math.Clamp(p.Y / ActualHeight, 0, 1)));
    }
    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (LiquidLighting) FadeHover(false);
    }
    private void MoveLight(Point target)
    {
        // Track the latest input immediately. Restarting a 140ms animation on each
        // mouse event made the highlight perpetually chase the pointer; a normalized
        // dead zone also discarded several pixels of movement on wide panels.
        _target = target;
        BeginAnimation(LightPositionProperty, null);
        SetCurrentValue(LightPositionProperty, target);
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (!LiquidLighting || SystemParameters.HighContrast || ActualWidth < 8 || ActualHeight < 8)
        {
            using(_background.RenderOpen()){ }
            using(_lighting.RenderOpen()){ }
            base.OnRender(dc);
            return;
        }
        using var surface=_lighting.RenderOpen();
        // Own the entire outline in liquid mode. Calling Border.OnRender as well would
        // superimpose its stroke and recreate the double rounded frame.
        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        double radius = Math.Min(CornerRadius.TopLeft, Math.Min(ActualWidth, ActualHeight) / 2);
        bool live=DrawRealtimeBackground(bounds,radius);
        // The live shader already applies a readability tint; don't double-tint the capture.
        if(!live)surface.DrawRoundedRectangle(Background, null, bounds, radius, radius);
        var pointer=(Point)GetValue(LightPositionProperty);
        double blend=Math.Clamp((double)GetValue(HoverBlendProperty),0,1);
        // Fade a local spot in place. Returning to ambient must not sweep a light across the panel.
        var light=pointer;
        var reflection = new RadialGradientBrush(Color.FromArgb((byte)(18*blend), 255, 255, 255), Colors.Transparent)
        {
            Center = light, GradientOrigin = light,
            RadiusX = 76/ActualWidth, RadiusY = 76/ActualHeight
        };
        surface.DrawRoundedRectangle(reflection, null, bounds, radius, radius);
        DrawLens(surface, bounds, radius, light, blend);
    }
    private bool DrawRealtimeBackground(Rect bounds,double radius)
    {
        using var drawing=_background.RenderOpen();
        if(GetValue(BackdropFrameProperty) is not GlassBackdropFrame frame || PresentationSource.FromVisual(this)==null)
        { _background.Effect=null;return false; }
        try
        {
            var start=PointToScreen(new Point(0,0));
            _sampleOrigin=start;
            var end=PointToScreen(new Point(ActualWidth,ActualHeight));
            _refraction??=new LiquidRefractionEffect();
            _refraction.Input=frame.Brush;
            _refraction.SurfaceSize=new Point(ActualWidth,ActualHeight);
            _refraction.Radius=radius;
            _refraction.CaptureOrigin=new Point((start.X-frame.Left)/frame.Width,(start.Y-frame.Top)/frame.Height);
            _refraction.CaptureScale=new Point((end.X-start.X)/frame.Width,(end.Y-start.Y)/frame.Height);
            _refraction.Dark=GlassAppearance.DarkPalette?1:0;
            _background.Effect=_refraction;
            _background.Clip=new RectangleGeometry(bounds,radius,radius);
            drawing.DrawRectangle(Brushes.White,null,bounds);
            return true;
        }
        catch(Exception ex)
        {
            global::System.Diagnostics.Debug.WriteLine($"[LiquidGlass] refraction unavailable: {ex.Message}");
            _background.Effect=null;return false;
        }
    }
    private void DrawLens(DrawingContext dc, Rect bounds, double radius, Point light, double blend)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        // Keep the narrow lens smooth at fractional Windows scale, without unbounded caches.
        double scale = Math.Min(3, Math.Max(dpi.DpiScaleX, dpi.DpiScaleY));
        int width = (int)Math.Ceiling(bounds.Width * scale), height = (int)Math.Ceiling(bounds.Height * scale);
        if (width > 4096 || height > 4096) return;
        if (_lens == null || _lens.PixelWidth != width || _lens.PixelHeight != height)
        {
            _cachedRadius=-1;
            _lens = new WriteableBitmap(width, height, 96 * scale, 96 * scale, PixelFormats.Pbgra32, null);
            _pixels = new byte[width * height * 4];
        }
        if(_cachedRadius==radius && _cachedLight==light && _cachedBlend==blend)
        {dc.DrawImage(_lens,bounds);return;}
        _cachedRadius=radius;_cachedLight=light;_cachedBlend=blend;
        Array.Clear(_pixels!);
        double halfW = bounds.Width / 2, halfH = bounds.Height / 2;
        const double band = 7;
        for (int y = 0; y < height; y++)
        {
            double py = (y + .5) / scale - halfH;
            for (int x = 0; x < width; x++)
            {
                double px = (x + .5) / scale - halfW;
                // Skip the clear centre; only the perimeter needs distance/normal work.
                if (Math.Abs(px) < halfW - radius - band && Math.Abs(py) < halfH - band) continue;
                double qx = Math.Abs(px) - halfW + radius, qy = Math.Abs(py) - halfH + radius;
                double ax = Math.Max(qx, 0), ay = Math.Max(qy, 0);
                double norm = Math.Sqrt(ax * ax + ay * ay);
                double depth = radius - norm - Math.Min(Math.Max(qx, qy), 0);
                if (depth < -.5 / scale || depth > band) continue;
                double nx = norm > 0 ? ax / norm : qx > qy ? 1 : 0;
                double ny = norm > 0 ? ay / norm : qy >= qx ? 1 : 0;
                nx *= Math.Sign(px); ny *= Math.Sign(py);
                double dx=px+halfW-light.X*bounds.Width,dy=py+halfH-light.Y*bounds.Height;
                // Fixed DIP radius, not a directional light shared by the whole edge.
                // Compact smooth falloff reaches zero outside the pointer's neighbourhood.
                double spot=1-SmoothStep(0,96,Math.Sqrt(dx*dx+dy*dy));
                // A continuous bevel: its brightest point is the silhouette, then it fades
                // inward. No inset strokes, independent inner radii, or closed second ring.
                double edge = Math.Exp(-Math.Max(0, depth) / 1.45);
                double coverage = Math.Clamp(depth * scale + .5, 0, 1);
                double fade = 1 - SmoothStep(4, band, depth);
                double shade=.025+.55*blend*spot*spot;
                byte alpha=(byte)Math.Clamp(255*shade*edge*coverage*fade,0,255);
                int i = (y * width + x) * 4;
                // Neutral highlights; dispersion is handled by actual background refraction.
                _pixels![i] = alpha;
                _pixels[i + 1] = alpha;
                _pixels[i + 2] = alpha;
                _pixels[i + 3] = alpha;
            }
        }
        _lens.WritePixels(new Int32Rect(0, 0, width, height), _pixels!, width * 4, 0);
        dc.DrawImage(_lens, bounds);
    }
    private static double SmoothStep(double a, double b, double value)
    {
        double t = Math.Clamp((value - a) / (b - a), 0, 1);
        return t * t * (3 - 2 * t);
    }
}
