using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using BluetoothBatteryMonitor.App.Models;

namespace BluetoothBatteryMonitor.App.Helpers;

public static class TrayIconGenerator
{
    public static Icon GenerateTrayIcon(int? batteryLevel,bool isCharging=false,bool isDarkTheme=true)
        => Generate(batteryLevel,isCharging,isDarkTheme,AppSettings.Load());
    public static Icon Generate(int? level,bool charging,bool dark,AppSettings settings)
    {
        using var bitmap=Render(level,charging,dark,settings);
        IntPtr handle=bitmap.GetHicon();
        try{return (Icon)Icon.FromHandle(handle).Clone();}finally{NativeMethods.DestroyIcon(handle);}
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
}
