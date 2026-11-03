using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using BluetoothBatteryMonitor.App.Models;

namespace BluetoothBatteryMonitor.App.Helpers;

public static class TrayIconGenerator
{
    /// <summary>Number of steps in one charging-breathing cycle (one step per pulse timer tick).</summary>
    public const int PulseSteps = 12;

    /// <summary>
    /// Breathing alpha percent for a charging ring at <paramref name="tick"/> (0..PulseSteps-1):
    /// a smooth 45%→100%→45% sine over one cycle. Static + pure so it is unit-testable.
    /// </summary>
    public static int GetPulseAlphaPercent(int tick)
    {
        double radians = (tick % PulseSteps) * 2.0 * Math.PI / PulseSteps;
        return (int)Math.Round(45 + 55 * (0.5 - 0.5 * Math.Cos(radians)));
    }

    private static Color ChargingColor => Color.FromArgb(0, 201, 141);

    public static Icon GenerateTrayIcon(int? batteryLevel,bool isCharging=false,bool isDarkTheme=true)
        => Generate(batteryLevel,isCharging,isDarkTheme,AppSettings.Load());
    public static Icon Generate(int? level,bool charging,bool dark,AppSettings settings)
    {
        using var bitmap=Render(level,charging,dark,settings);
        IntPtr handle=bitmap.GetHicon();
        try{return (Icon)Icon.FromHandle(handle).Clone();}finally{NativeMethods.DestroyIcon(handle);}
    }
    public static Icon GenerateMultiple(IReadOnlyList<(int Level,bool Charging)> devices,bool dark,AppSettings settings)
    {
        using var bitmap=RenderMultiple(devices,dark,settings);
        IntPtr handle=bitmap.GetHicon();
        try{return (Icon)Icon.FromHandle(handle).Clone();}finally{NativeMethods.DestroyIcon(handle);}
    }
    public static Bitmap RenderMultiple(IReadOnlyList<(int Level,bool Charging)> devices,bool dark,AppSettings settings,int size=64)
    {
        if(devices.Count==0)return Render(null,false,dark,settings,size);
        if(devices.Count==1||settings.TrayIconStyle=="数字")
            return Render(devices.Min(d=>d.Level),devices.Any(d=>d.Charging),dark,settings,size);
        var bitmap=new Bitmap(size,size);
        using var g=Graphics.FromImage(bitmap);
        g.ScaleTransform(size/64f,size/64f);
        g.SmoothingMode=SmoothingMode.AntiAlias;g.PixelOffsetMode=PixelOffsetMode.HighQuality;
        g.Clear(Color.Transparent);
        int count=Math.Min(devices.Count,3);
        float width=Math.Min((float)Math.Clamp(settings.TrayRingWidth,7,13),count==2?10:7);
        float spacing=width+3;
        for(int i=0;i<count;i++)
        {
            float inset=2+width/2+i*spacing;
            var box=new RectangleF(inset,inset,64-2*inset,64-2*inset);
            using var outline=new Pen(Color.FromArgb(190,dark?Color.Black:Color.White),width+2);
            using var track=new Pen(Color.FromArgb(90,dark?Color.White:Color.Black),width);
            using var arc=new Pen(TrayAppearance.GetColor(devices[i].Level,settings),width){StartCap=LineCap.Round,EndCap=LineCap.Round};
            g.DrawEllipse(outline,box);g.DrawEllipse(track,box);
            if(devices[i].Level>0)g.DrawArc(arc,box,-90,Math.Clamp(devices[i].Level,0,100)*3.6f);
            if(devices[i].Charging)
            {
                using var marker=new SolidBrush(dark?Color.White:Color.Black);
                g.FillEllipse(marker,32-width/4,inset-width/4,width/2,width/2);
            }
        }
        if(settings.TrayIconStyle=="圆环 + 数字"&&count==2)
        {
            using var font=new Font("Segoe UI",17,FontStyle.Bold,GraphicsUnit.Pixel);
            using var brush=new SolidBrush(TrayAppearance.GetColor(devices.Min(d=>d.Level),settings));
            using var format=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center};
            g.DrawString(devices.Min(d=>d.Level).ToString(),font,brush,new RectangleF(0,0,64,64),format);
        }
        return bitmap;
    }
    public static Bitmap Render(int? level,bool charging,bool dark,AppSettings settings,int size=64)
    {
        var bitmap=new Bitmap(size,size);
        using var g=Graphics.FromImage(bitmap);
        g.ScaleTransform(size/64f,size/64f);
        g.SmoothingMode=SmoothingMode.AntiAlias;g.PixelOffsetMode=PixelOffsetMode.HighQuality;
        g.TextRenderingHint=TextRenderingHint.AntiAliasGridFit;
        g.Clear(Color.Transparent);
        Color color=TrayAppearance.GetColor(level,settings);
        float width=(float)Math.Clamp(settings.TrayRingWidth,7,13);
        if(settings.TrayIconStyle!="数字")
        {
            float inset=width/2+2;
            var box=new RectangleF(inset,inset,64-inset*2,64-inset*2);
            using var keyline=new Pen(Color.FromArgb(190,dark?Color.Black:Color.White),width+2);
            using var track=new Pen(Color.FromArgb(100,dark?Color.White:Color.Black),width);
            using var arc=new Pen(color,width){StartCap=LineCap.Round,EndCap=LineCap.Round};
            g.DrawEllipse(keyline,box);g.DrawEllipse(track,box);
            if(level>0)g.DrawArc(arc,box,-90,Math.Clamp(level.Value,0,100)*3.6f);
        }
        if(settings.TrayIconStyle!="圆环"||level==null)
        {
            string text=level?.ToString()??"—";
            int point=settings.TrayIconStyle=="数字"?(text.Length<3?42:32):(text.Length<3?30:23);
            using var font=new Font("Segoe UI",point,FontStyle.Bold,GraphicsUnit.Pixel);
            using var format=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center};
            using var path=new GraphicsPath();path.AddString(text,font.FontFamily,(int)FontStyle.Bold,point,new RectangleF(0,0,64,64),format);
            using var border=new Pen(dark?Color.Black:Color.White,2.5f){LineJoin=LineJoin.Round};
            using var brush=new SolidBrush(color);g.DrawPath(border,path);g.FillPath(brush,path);
        }
        if(charging)
        {
            using var bolt=new SolidBrush(dark?Color.White:Color.FromArgb(40,40,40));
            g.FillPolygon(bolt,new[]{new PointF(52,41),new PointF(44,53),new PointF(50,53),new PointF(47,63),new PointF(60,48),new PointF(54,48)});
        }
        return bitmap;
    }

    /// <summary>
    /// Per-device tray icon: battery ring + a device pictogram inside. While charging the ring
    /// turns green and breathes (alpha modulated by <paramref name="pulseTick"/>; pass a negative
    /// value for a steady ring).
    /// </summary>
    public static Icon GeneratePerDevice(int level,bool charging,DeviceType type,bool dark,AppSettings settings,int pulseTick=-1)
    {
        using var bitmap=RenderPerDevice(level,charging,type,dark,settings,pulseTick);
        IntPtr handle=bitmap.GetHicon();
        try{return (Icon)Icon.FromHandle(handle).Clone();}finally{NativeMethods.DestroyIcon(handle);}
    }

    public static Bitmap RenderPerDevice(int level,bool charging,DeviceType type,bool dark,AppSettings settings,int pulseTick=-1,int size=64)
    {
        var bitmap=new Bitmap(size,size);
        using var g=Graphics.FromImage(bitmap);
        g.ScaleTransform(size/64f,size/64f);
        g.SmoothingMode=SmoothingMode.AntiAlias;g.PixelOffsetMode=PixelOffsetMode.HighQuality;
        g.Clear(Color.Transparent);

        Color ringColor=charging?ChargingColor:TrayAppearance.GetColor(level,settings);
        if(charging&&pulseTick>=0)
        {
            int alpha=GetPulseAlphaPercent(pulseTick);
            ringColor=Color.FromArgb(alpha*255/100,ringColor);
        }

        float width=(float)Math.Clamp(settings.TrayRingWidth,7,13);
        float inset=width/2+2;
        var box=new RectangleF(inset,inset,64-inset*2,64-inset*2);
        using(var keyline=new Pen(Color.FromArgb(190,dark?Color.Black:Color.White),width+2))
        using(var track=new Pen(Color.FromArgb(100,dark?Color.White:Color.Black),width))
        using(var arc=new Pen(ringColor,width){StartCap=LineCap.Round,EndCap=LineCap.Round})
        {
            g.DrawEllipse(keyline,box);g.DrawEllipse(track,box);
            if(level>0)g.DrawArc(arc,box,-90,Math.Clamp(level,0,100)*3.6f);
        }

        var contrast=dark?Color.White:Color.FromArgb(30,30,30);
        DrawDeviceGlyph(g,type,contrast,Math.Clamp(level,0,100));

        if(charging&&pulseTick<0)
        {
            // Static per-device icons (pulse disabled) keep the familiar bolt marker.
            using var bolt=new SolidBrush(dark?Color.White:Color.FromArgb(40,40,40));
            g.FillPolygon(bolt,new[]{new PointF(52,41),new PointF(44,53),new PointF(50,53),new PointF(47,63),new PointF(60,48),new PointF(54,48)});
        }
        return bitmap;
    }

    /// <summary>Hand-drawn pictogram in the 64×64 canvas space, centred inside the ring.</summary>
    public static void DrawDeviceGlyph(Graphics g,DeviceType type,Color color,int level)
    {
        using var pen=new Pen(color,3f){StartCap=LineCap.Round,EndCap=LineCap.Round,LineJoin=LineJoin.Round};
        using var fill=new SolidBrush(color);
        switch(type)
        {
            case DeviceType.Mouse:
                g.DrawPath(pen,RoundedRect(25,22,14,22,7));
                g.DrawLine(pen,32,26,32,31);
                break;
            case DeviceType.Keyboard:
                g.DrawPath(pen,RoundedRect(21,26,22,13,3));
                for(int i=0;i<5;i++)g.FillRectangle(fill,24+i*3.6f,29,2f,2f);
                g.DrawLine(pen,25,35,39,35);
                break;
            case DeviceType.Headphones:
                g.DrawArc(pen,22,22,20,20,180,180);
                g.DrawPath(pen,RoundedRect(21,32,5,9,2));
                g.DrawPath(pen,RoundedRect(38,32,5,9,2));
                break;
            case DeviceType.Earbuds:
                g.FillEllipse(fill,24,23,6,6);
                g.DrawLine(pen,27,29,27,39);
                g.FillEllipse(fill,34,23,6,6);
                g.DrawLine(pen,37,29,37,39);
                break;
            case DeviceType.Speaker:
                g.DrawPath(pen,RoundedRect(25,22,14,22,3));
                g.DrawEllipse(pen,29,26,6,6);
                g.DrawEllipse(pen,27,34,10,8);
                break;
            case DeviceType.Gamepad:
                g.DrawPath(pen,RoundedRect(20,27,24,13,6));
                g.DrawLine(pen,26,33.5f,31,33.5f);
                g.DrawLine(pen,28.5f,31,28.5f,36);
                g.FillEllipse(fill,36,30,3,3);
                g.FillEllipse(fill,39,35,3,3);
                break;
            case DeviceType.Watch:
                g.DrawLine(pen,29,21,29,25);
                g.DrawLine(pen,39,21,39,25);
                g.DrawLine(pen,29,41,29,45);
                g.DrawLine(pen,39,41,39,45);
                g.DrawPath(pen,RoundedRect(24,25,16,16,4));
                break;
            case DeviceType.Phone:
                g.DrawPath(pen,RoundedRect(25,22,14,22,3));
                g.DrawLine(pen,30,40,34,40);
                break;
            case DeviceType.Stylus:
                g.DrawLine(pen,25,41,37,25);
                g.FillPolygon(fill,new[]{new PointF(37,25),new PointF(40,21),new PointF(41,26)});
                break;
            default:
            {
                // Generic: battery outline with a proportional fill.
                g.DrawPath(pen,RoundedRect(23,28,15,10,2));
                g.FillRectangle(fill,39,31,3,4);
                float w=13f*Math.Clamp(level,0,100)/100f;
                if(w>1f)g.FillRectangle(fill,25,30,w,6);
                break;
            }
        }
    }

    private static GraphicsPath RoundedRect(float x,float y,float w,float h,float r)
    {
        var path=new GraphicsPath();
        float d=r*2;
        path.AddArc(x,y,d,d,180,90);
        path.AddArc(x+w-d,y,d,d,270,90);
        path.AddArc(x+w-d,y+h-d,d,d,0,90);
        path.AddArc(x,y+h-d,d,d,90,90);
        path.CloseFigure();
        return path;
    }
}
