using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Controls;

namespace BluetoothBatteryMonitor.App.Services.System;

public static class GlassAppearance
{
    public static bool Enabled { get; private set; }
    /// <summary>True when the active glass palette is the dark one.</summary>
    public static bool DarkPalette { get; private set; }
    public static bool Reduced { get; private set; }
    private static bool contrastOverrides;

    /// <summary>Radius shared by the window frame and the panel so only one corner curve is visible.</summary>
    public const double PanelRadius = 8;
    public const double CardRadius = 8;

    public static void Apply(string theme,bool dark)
    {
        Enabled=theme.StartsWith("液态玻璃",StringComparison.Ordinal);
        DarkPalette=Enabled && dark;
        Reduced=theme.Contains("低透明") || SystemParameters.HighContrast || !TransparencyAllowed() || !BackdropSupported();
        var resources=Application.Current.Resources;
        if(SystemParameters.HighContrast)
        {
            resources["TextFillColorPrimaryBrush"]=SystemColors.WindowTextBrush;
            resources["TextFillColorSecondaryBrush"]=SystemColors.WindowTextBrush;
            resources["AccentTextFillColorPrimaryBrush"]=SystemColors.HighlightBrush;contrastOverrides=true;
        }
        else if(contrastOverrides)
        {
            resources.Remove("TextFillColorPrimaryBrush");resources.Remove("TextFillColorSecondaryBrush");resources.Remove("AccentTextFillColorPrimaryBrush");contrastOverrides=false;
        }
        if(Enabled && !SystemParameters.HighContrast)
        {
            // System Acrylic supplies the blur; the panel only adds a soft tint so the
            // desktop stays visible. Both palettes follow the Windows app theme.
            if(dark)
            {
                resources["PeripheralPanelBrush"]=Gradient(Reduced?"#FF262628":"#B4252527",Reduced?"#FF2E2E31":"#9E2E2E31");
                resources["PeripheralCardBrush"]=Gradient(Reduced?"#FF2A2A2D":"#24FFFFFF",Reduced?"#FF232326":"#10FFFFFF");
                resources["PeripheralEdgeBrush"]=EdgeGradient(Color.FromArgb(70,255,255,255),Color.FromArgb(16,0,0,0),Color.FromArgb(150,255,255,255));
                resources["PeripheralGlintBrush"]=new RadialGradientBrush(Color.FromArgb(38,255,255,255),Colors.Transparent){Center=new Point(.25,0),GradientOrigin=new Point(.25,0),RadiusX=.9,RadiusY=1.1};
                resources["PeripheralCardHoverBrush"]=new SolidColorBrush(Color.FromArgb(38,255,255,255));
            }
            else
            {
                resources["PeripheralPanelBrush"]=Gradient(Reduced?"#FFF5F6F8":"#B8F6F7F9",Reduced?"#FFE6E9EE":"#9EE9ECF1");
                resources["PeripheralCardBrush"]=Gradient(Reduced?"#FFFAFBFC":"#1F000000",Reduced?"#FFF2F4F7":"#0C000000");
                resources["PeripheralEdgeBrush"]=EdgeGradient(Color.FromArgb(190,255,255,255),Color.FromArgb(40,70,75,85),Color.FromArgb(190,255,255,255));
                resources["PeripheralGlintBrush"]=new RadialGradientBrush(Color.FromArgb(80,255,255,255),Colors.Transparent){Center=new Point(.25,0),GradientOrigin=new Point(.25,0),RadiusX=.9,RadiusY=1.1};
                resources["PeripheralCardHoverBrush"]=new SolidColorBrush(Color.FromArgb(28,0,0,0));
            }
            resources["PeripheralPanelRadius"]=new CornerRadius(PanelRadius);
            resources["PeripheralCardEdgeBrush"]=Brushes.Transparent;
            resources["PeripheralCardRadius"]=new CornerRadius(CardRadius);
        }
        else
        {
            resources["PeripheralPanelBrush"]=SystemParameters.HighContrast?SystemColors.WindowBrush:Application.Current.TryFindResource("ApplicationBackgroundBrush") as Brush ?? new SolidColorBrush(dark?Colors.Black:Colors.White);
            resources["PeripheralCardBrush"]=SystemParameters.HighContrast?SystemColors.WindowBrush:Application.Current.TryFindResource("CardBackgroundFillColorDefaultBrush") as Brush ?? Brushes.Transparent;
            resources["PeripheralEdgeBrush"]=SystemParameters.HighContrast?SystemColors.WindowTextBrush:Application.Current.TryFindResource("CardStrokeColorDefaultBrush") as Brush ?? Brushes.Gray;
            resources["PeripheralGlintBrush"]=Brushes.Transparent;
            resources["PeripheralCardHoverBrush"]=Brushes.Transparent;
            resources["PeripheralCardEdgeBrush"]=resources["PeripheralEdgeBrush"];
            resources["PeripheralPanelRadius"]=new CornerRadius(PanelRadius);
            resources["PeripheralCardRadius"]=new CornerRadius(CardRadius);
        }
        foreach(Window window in Application.Current.Windows)ApplyWindow(window);
    }
    private static LinearGradientBrush Gradient(string start,string end)
    {
        var brush=new LinearGradientBrush((Color)ColorConverter.ConvertFromString(start),(Color)ColorConverter.ConvertFromString(end),new Point(0,0),new Point(1,1));brush.Freeze();return brush;
    }
    private static LinearGradientBrush EdgeGradient(Color start,Color middle,Color end)
    {
        var brush=new LinearGradientBrush(new GradientStopCollection { new GradientStop(start,0),new GradientStop(middle,0.55),new GradientStop(end,1)},new Point(0,0),new Point(1,1));brush.Freeze();return brush;
    }
    private static bool TransparencyAllowed()
    {
        try{using var key=Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");return key?.GetValue("EnableTransparency") is not int value || value!=0;}catch{return false;}
    }
    /// <summary>Acrylic and rounded window corners need Windows 11 (build 22000+).
    /// Read the real build number from the registry: Environment.OSVersion is capped by
    /// the app manifest's supportedOS list and would always report Windows 10.</summary>
    private static bool? backdropSupported;
    private static bool BackdropSupported()
    {
        if(backdropSupported.HasValue)return backdropSupported.Value;
        try
        {
            using var key=Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            backdropSupported=int.TryParse(key?.GetValue("CurrentBuildNumber")?.ToString(),out var build)?build>=22000:true;
        }
        catch{backdropSupported=true;}
        return backdropSupported.Value;
    }
    public static void ApplyWindow(Window window)
    {
        if(window is not FluentWindow fluent)return;
        Wpf.Ui.Appearance.ApplicationThemeManager.Apply(fluent);
        if(window is Views.SettingsWindow or Views.AboutWindow)
            fluent.SetResourceReference(global::System.Windows.Controls.Control.BackgroundProperty,"ApplicationBackgroundBrush");
        // Acrylic uses the Windows compositor; no continuous rendering loop.
        fluent.WindowBackdropType=SystemParameters.HighContrast||!BackdropSupported()?WindowBackdropType.None:Enabled && !Reduced?WindowBackdropType.Acrylic:WindowBackdropType.Mica;
    }
}
