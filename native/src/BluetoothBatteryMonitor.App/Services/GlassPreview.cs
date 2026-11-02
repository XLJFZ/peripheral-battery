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
        foreach(var pair in new[]{("液态玻璃","glass"),("普通版","ordinary")})
        {
            System.ThemeManager.ApplyTheme(pair.Item1);
            var rows=new[]{new BluetoothDeviceModel{Id="preview-mouse",Name="Razer DeathAdder V3 Pro",Type=DeviceType.Mouse,BatteryLevel=88,IsConnected=true},
                new BluetoothDeviceModel{Id="preview-keyboard",Name="键盘 · 主题预览",Type=DeviceType.Keyboard,BatteryLevel=64,IsConnected=true},
                new BluetoothDeviceModel{Id="preview-headset",Name="耳机 · 主题预览",Type=DeviceType.Headphones,BatteryLevel=26,IsConnected=true}};
            var window=new Views.FlyoutWindow(null!){DataContext=new{Devices=rows.Select(r=>new DeviceItemViewModel(r)).ToArray(),HasDevices=true,StatusText="主题预览 · 示例数据"},ShowInTaskbar=false,DisableAutoClose=true,Left=-10000,Top=-10000};
            try
            {
                window.Show();System.GlassAppearance.ApplyWindow(window);await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);window.UpdateLayout();
                var bitmap=new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth),(int)Math.Ceiling(window.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(window);
                var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream=File.Create(Path.Combine(folder,pair.Item2+".png"));encoder.Save(stream);
            }
            finally{window.Close();}
            var settingsWindow=new Views.SettingsWindow{ShowInTaskbar=false,Left=-10000,Top=-10000,WindowStartupLocation=WindowStartupLocation.Manual};
            try
            {
                settingsWindow.Show();System.GlassAppearance.ApplyWindow(settingsWindow);await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);settingsWindow.UpdateLayout();
                var vm=(SettingsViewModel)settingsWindow.DataContext;
                if(!vm.AvailableThemes.SequenceEqual(new[]{"液态玻璃","普通版"}) || !vm.AvailableThemes.Contains(vm.SelectedTheme))throw new InvalidOperationException("Appearance choices or legacy migration invalid");
                ((FrameworkElement)settingsWindow.FindName("AppearanceModeSelector")).BringIntoView();
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);settingsWindow.UpdateLayout();
                var bitmap=new RenderTargetBitmap((int)Math.Ceiling(settingsWindow.ActualWidth),(int)Math.Ceiling(settingsWindow.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(settingsWindow);
                var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream=File.Create(Path.Combine(folder,pair.Item2+"-settings.png"));encoder.Save(stream);
                // Verify live resource replacement, not only fresh-window rendering.
                System.ThemeManager.ApplyTheme(pair.Item1=="液态玻璃"?"普通版":"液态玻璃");
                if(System.GlassAppearance.Enabled!=(pair.Item1!="液态玻璃"))throw new InvalidOperationException("Live appearance switching failed");
                System.ThemeManager.ApplyTheme(pair.Item1);
            }
            finally{settingsWindow.Close();}
        }
        await File.WriteAllTextAsync(Path.Combine(folder,"result.json"),JsonSerializer.Serialize(new{success=true,themes=2,previewData=true}));
    }
}
