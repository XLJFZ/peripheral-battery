using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Controls;

namespace BluetoothBatteryMonitor.App.Services.System;

public static class GlassAppearance
{
    public static bool Enabled { get; private set; }
    public static bool Reduced { get; private set; }
    private static bool contrastOverrides;
    public static void Apply(string theme,bool dark)
    {
        Enabled=theme.StartsWith("液态玻璃",StringComparison.Ordinal);
        Reduced=theme.Contains("低透明") || SystemParameters.HighContrast || !TransparencyAllowed();
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
            resources["PeripheralPanelBrush"]=Gradient(Reduced?"#FFF2F3F5":"#ADF2F3F5",Reduced?"#FFDEE2E8":"#96E1E5EB");
            resources["PeripheralCardBrush"]=Gradient(Reduced?"#FFF8F9FA":"#32FFFFFF",Reduced?"#FFF0F2F5":"#10FFFFFF");
            resources["PeripheralEdgeBrush"]=new LinearGradientBrush(new GradientStopCollection { new GradientStop(Colors.White,0),new GradientStop(Color.FromArgb(64,255,255,255),0.35),new GradientStop(Color.FromArgb(40,80,85,95),0.7),new GradientStop(Color.FromArgb(190,255,255,255),1)},new Point(0,0),new Point(1,1));
            resources["PeripheralGlintBrush"]=new RadialGradientBrush(Color.FromArgb(90,255,255,255),Colors.Transparent){Center=new Point(.25,0),GradientOrigin=new Point(.25,0),RadiusX=.9,RadiusY=1.1};
            resources["PeripheralPanelRadius"]=new CornerRadius(22);
            resources["PeripheralCardEdgeBrush"]=Brushes.Transparent;
            resources["PeripheralCardRadius"]=new CornerRadius(12);
        }
        else
        {
            resources["PeripheralPanelBrush"]=SystemParameters.HighContrast?SystemColors.WindowBrush:Application.Current.TryFindResource("ApplicationBackgroundBrush") as Brush ?? new SolidColorBrush(dark?Colors.Black:Colors.White);
            resources["PeripheralCardBrush"]=SystemParameters.HighContrast?SystemColors.WindowBrush:Application.Current.TryFindResource("CardBackgroundFillColorDefaultBrush") as Brush ?? Brushes.Transparent;
            resources["PeripheralEdgeBrush"]=SystemParameters.HighContrast?SystemColors.WindowTextBrush:Application.Current.TryFindResource("CardStrokeColorDefaultBrush") as Brush ?? Brushes.Gray;
            resources["PeripheralGlintBrush"]=Brushes.Transparent;
            resources["PeripheralCardEdgeBrush"]=resources["PeripheralEdgeBrush"];
            resources["PeripheralPanelRadius"]=new CornerRadius(12);resources["PeripheralCardRadius"]=new CornerRadius(8);
        }
        foreach(Window window in Application.Current.Windows)ApplyWindow(window);
    }
    private static LinearGradientBrush Gradient(string start,string end)
    {
        var brush=new LinearGradientBrush((Color)ColorConverter.ConvertFromString(start),(Color)ColorConverter.ConvertFromString(end),new Point(0,0),new Point(1,1));brush.Freeze();return brush;
    }
    private static bool TransparencyAllowed()
    {
        try{using var key=Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");return key?.GetValue("EnableTransparency") is not int value || value!=0;}catch{return false;}
    }
    public static void ApplyWindow(Window window)
    {
        if(window is not FluentWindow fluent)return;
        Wpf.Ui.Appearance.ApplicationThemeManager.Apply(fluent);
        if(window is Views.SettingsWindow or Views.AboutWindow)
            fluent.SetResourceReference(global::System.Windows.Controls.Control.BackgroundProperty,"ApplicationBackgroundBrush");
        // Acrylic uses the Windows compositor; no continuous rendering loop.
        fluent.WindowBackdropType=SystemParameters.HighContrast?WindowBackdropType.None:Enabled && !Reduced?WindowBackdropType.Acrylic:WindowBackdropType.Mica;
    }
}
