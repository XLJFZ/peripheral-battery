using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using BluetoothBatteryMonitor.App.Models;
using BluetoothBatteryMonitor.App.ViewModels;

namespace BluetoothBatteryMonitor.App.Services;

public static class GlassPreview
{
    public static async Task Run(string folder)
    {
        Directory.CreateDirectory(folder);Localization.LocalizationService.ApplyLanguage("zh");
        // 普通版 + 毛玻璃 + 液态玻璃 under both Windows app themes, so the dark/light palettes are
        // rendered from real resources rather than asserted from source.
        foreach(var pair in new[]{("普通版","ordinary-dark",(bool?)true),("普通版","ordinary-light",(bool?)false),("毛玻璃","frosted-dark",(bool?)true),("毛玻璃","frosted-light",(bool?)false),("液态玻璃","glass-dark",(bool?)true),("液态玻璃","glass-light",(bool?)false)})
        {
            System.ThemeManager.ApplyTheme(pair.Item1,pair.Item3);
            var rows=new[]{new BluetoothDeviceModel{Id="preview-mouse",Name="Razer DeathAdder V3 Pro",Type=DeviceType.Mouse,BatteryLevel=88,IsConnected=true},
                new BluetoothDeviceModel{Id="preview-keyboard",Name="键盘 · 主题预览",Type=DeviceType.Keyboard,BatteryLevel=64,IsConnected=true},
                new BluetoothDeviceModel{Id="preview-headset",Name="耳机 · 主题预览",Type=DeviceType.Headphones,BatteryLevel=26,IsConnected=true}};
            var window=new Views.FlyoutWindow(null!){DataContext=new{Devices=rows.Select(r=>new DeviceItemViewModel(r)).ToArray(),HasDevices=true,StatusText="主题预览 · 示例数据"},ShowInTaskbar=false,DisableAutoClose=true,Left=-10000,Top=-10000};
            try
            {
                window.Show();System.GlassAppearance.ApplyWindow(window);await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);window.UpdateLayout();
                SaveComposited(window,Path.Combine(folder,pair.Item2+".png"),pair.Item3==true);
            }
            finally{window.Close();}
            var settingsWindow=new Views.SettingsWindow{ShowInTaskbar=false,Left=-10000,Top=-10000,WindowStartupLocation=WindowStartupLocation.Manual};
            try
            {
                settingsWindow.Show();System.GlassAppearance.ApplyWindow(settingsWindow);await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);settingsWindow.UpdateLayout();
                var vm=(SettingsViewModel)settingsWindow.DataContext;
                if(!vm.AvailableThemes.SequenceEqual(new[]{System.ThemeManager.AppearanceNormal,System.ThemeManager.AppearanceFrosted,System.ThemeManager.AppearanceGlass}) || !vm.AvailableThemes.Contains(vm.SelectedTheme))throw new InvalidOperationException("Appearance choices or legacy migration invalid");
                if(vm.SelectedTheme!=System.ThemeManager.Normalize(AppSettings.Load().Theme))throw new InvalidOperationException($"Appearance selection drifted from the saved profile to {vm.SelectedTheme}");
                ((FrameworkElement)settingsWindow.FindName("AppearanceModeSelector")).BringIntoView();
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);settingsWindow.UpdateLayout();
                SaveComposited(settingsWindow,Path.Combine(folder,pair.Item2+"-settings.png"),pair.Item3==true);
                // Verify the rendered palette (before the live-switch probe mutates state).
                if(pair.Item1=="液态玻璃")
                {
                    if(!System.GlassAppearance.Enabled || !System.GlassAppearance.Liquid)throw new InvalidOperationException("Liquid glass palette not active");
                    if(System.GlassAppearance.DarkPalette!=pair.Item3)throw new InvalidOperationException("Glass palette does not follow the dark/light request");
                }
                else if(pair.Item1=="毛玻璃")
                {
                    if(!System.GlassAppearance.Enabled || System.GlassAppearance.Liquid)throw new InvalidOperationException("Frosted glass palette not active");
                    if(System.GlassAppearance.DarkPalette!=pair.Item3)throw new InvalidOperationException("Frosted palette does not follow the dark/light request");
                }
                else if(System.GlassAppearance.Enabled)throw new InvalidOperationException("普通版 must not enable glass resources");
                // Verify live resource replacement, not only fresh-window rendering.
                var next=pair.Item1=="液态玻璃"?"普通版":pair.Item1=="毛玻璃"?"液态玻璃":"毛玻璃";
                System.ThemeManager.ApplyTheme(next,pair.Item3);
                bool nextOk=next=="液态玻璃"?System.GlassAppearance.Liquid:next=="毛玻璃"?System.GlassAppearance.Enabled&&!System.GlassAppearance.Liquid:!System.GlassAppearance.Enabled;
                if(!nextOk)throw new InvalidOperationException("Live appearance switching failed");
                System.ThemeManager.ApplyTheme(pair.Item1,pair.Item3);
            }
            finally{settingsWindow.Close();}
        }
        await File.WriteAllTextAsync(Path.Combine(folder,"result.json"),JsonSerializer.Serialize(new{success=true,themes=3,variants=6,previewData=true}));
    }

    /// <summary>Flattens the translucent window over a desktop-coloured canvas so the
    /// preview shows the same contrast a user sees instead of transparent PNG pixels.</summary>
    private static void SaveComposited(Window window,string path,bool darkDesktop)
    {
        int width=(int)Math.Ceiling(window.ActualWidth),height=(int)Math.Ceiling(window.ActualHeight);
        if(width<=0||height<=0)return;
        var visual=new DrawingVisual();
        using(var dc=visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(darkDesktop?Color.FromRgb(0x1F,0x1F,0x1F):Color.FromRgb(0xE9,0xE9,0xE9)),null,new Rect(0,0,width,height));
            dc.DrawRectangle(new VisualBrush(window),null,new Rect(0,0,width,height));
        }
        var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream=File.Create(path);encoder.Save(stream);
    }
}
