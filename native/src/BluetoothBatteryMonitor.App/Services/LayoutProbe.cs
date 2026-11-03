using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using BluetoothBatteryMonitor.App.Models;
using BluetoothBatteryMonitor.App.ViewModels;

namespace BluetoothBatteryMonitor.App.Services;

/// <summary>
/// Measures the flyout layout after content changes. The window uses SizeToContent=Height,
/// so this records whether the panel shrinks back after the empty state is replaced by devices.
/// </summary>
public static class LayoutProbe
{
    private sealed class ProbeModel : INotifyPropertyChanged
    {
        public ObservableCollection<DeviceItemViewModel> Devices { get; } = new();
        private bool _hasDevices;
        public bool HasDevices { get => _hasDevices; set { if (_hasDevices == value) return; _hasDevices = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasDevices))); } }
        public string StatusText { get; set; } = "布局探针";
        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public static async Task Run(string path)
    {
        Localization.LocalizationService.ApplyLanguage("zh");
        var report = new List<object>();

        // A: one device present before the window is shown.
        var direct = new ProbeModel { HasDevices = true };
        direct.Devices.Add(Row("probe-1"));
        report.Add(await Measure("device-before-show", direct, addLater: false));

        // B: empty state first, then a device arrives (the real startup order).
        var late = new ProbeModel { HasDevices = false };
        report.Add(await Measure("empty-then-device", late, addLater: true));

        // C: two devices arriving after the empty state.
        var lateTwo = new ProbeModel { HasDevices = false };
        report.Add(await Measure("empty-then-two-devices", lateTwo, addLater: true, second: true));

        // D: enough devices to hit MaxHeight; the list must scroll instead of growing.
        var crowded = new ProbeModel { HasDevices = false };
        report.Add(await Measure("empty-then-ten-devices", crowded, addLater: true, extra: 9));

        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { success = true, cases = report }, new JsonSerializerOptions { WriteIndented = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals }));
    }

    /// <summary>
    /// Measures the settings footer buttons in every interface language. The footer is a
    /// fixed-width window with four right-aligned buttons whose labels differ per language;
    /// German historically overflowed until the strings were shortened by hand. This records
    /// the desired width per language so the CI check can catch the next overflow.
    /// </summary>
    public static async Task RunSettingsFooter(string path)
    {
        string[] codes = { "zh", "zht", "en", "tr", "fr", "de", "es", "it" };
        var report = new List<object>();

        var window = new Views.SettingsWindow
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -10000,
            Top = -10000,
            ShowInTaskbar = false,
            ShowActivated = false
        };
        try
        {
            window.Show();
            await Pump();
            var footer = window.FindName("FooterButtons") as StackPanel
                ?? throw new InvalidOperationException("FooterButtons not found in SettingsWindow");

            foreach (string code in codes)
            {
                Localization.LocalizationService.ApplyLanguage(code);
                await Pump();
                footer.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var content = window.Content as FrameworkElement;
                report.Add(new
                {
                    language = code,
                    footerDesiredWidth = Math.Round(footer.DesiredSize.Width, 1),
                    contentActualWidth = Math.Round(content?.ActualWidth ?? 0, 1),
                    windowActualWidth = Math.Round(window.ActualWidth, 1)
                });
            }

            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { success = true, languages = report }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { window.Close(); }
    }

    private static DeviceItemViewModel Row(string id)
        => new(new BluetoothDeviceModel { Id = id, Name = "布局探针设备", Type = DeviceType.Mouse, BatteryLevel = 76, IsConnected = true });

    private static async Task<object> Measure(string name, ProbeModel model, bool addLater, bool second = false, int extra = 0)
    {
        var window = new Views.FlyoutWindow(null!)
        {
            DataContext = model,
            ShowInTaskbar = false,
            DisableAutoClose = true,
            Left = -10000,
            Top = -10000
        };
        try
        {
            window.Show();
            await Pump();
            double emptyFit = window.FitToContent();
            await Pump();
            var empty = Snapshot(window, "0 devices");
            if (addLater)
            {
                model.Devices.Add(Row("probe-2"));
                model.HasDevices = true;
                if (second) model.Devices.Add(Row("probe-3"));
                for (int i = 0; i < extra; i++) model.Devices.Add(Row($"probe-extra-{i}"));
                await Pump();
                await Pump();
            }
            double contentFit = window.FitToContent();
            await Pump();
            var after = Snapshot(window, $"{model.Devices.Count} devices");
            return new { name, emptyFit, contentFit, emptyState = empty, afterContent = after };
        }
        finally { window.Close(); }
    }

    private static async Task Pump()
    {
        await Dispatcher.Yield(DispatcherPriority.Loaded);
        await Task.Delay(120);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    }

    private static object Snapshot(Window window, string label)
    {
        window.UpdateLayout();
        var content = window.Content as FrameworkElement;
        var grid = (content as Border)?.Child as Grid;
        var scroll = FindChild<ScrollViewer>(window);
        var rows = grid?.RowDefinitions.Select(r => Math.Round(r.ActualHeight, 1)).ToArray() ?? Array.Empty<double>();
        return new
        {
            label,
            windowHeight = Math.Round(window.ActualHeight, 1),
            windowWidth = Math.Round(window.ActualWidth, 1),
            windowWidthProperty = Math.Round(window.Width, 1),
            windowMinWidth = Math.Round(window.MinWidth, 1),
            windowMaxWidth = double.IsInfinity(window.MaxWidth) ? "Infinity" : Math.Round(window.MaxWidth, 1).ToString(),
            flyoutWidthResource = Application.Current.TryFindResource("FlyoutWidth"),
            windowHeightProperty = double.IsNaN(window.Height) ? "NaN" : Math.Round(window.Height, 1).ToString(),
            sizeToContent = window.SizeToContent.ToString(),
            windowMaxHeight = double.IsInfinity(window.MaxHeight) ? "Infinity" : Math.Round(window.MaxHeight, 1).ToString(),
            windowMinHeight = Math.Round(window.MinHeight, 1),
            dpi = VisualTreeHelper.GetDpi(window).DpiScaleY,
            contentDesired = Math.Round(content?.DesiredSize.Height ?? 0, 1),
            contentActual = Math.Round(content?.ActualHeight ?? 0, 1),
            gridHeight = Math.Round(grid?.ActualHeight ?? 0, 1),
            gridDesired = Math.Round(grid?.DesiredSize.Height ?? 0, 1),
            rows,
            scrollHeight = Math.Round(scroll?.ActualHeight ?? 0, 1),
            scrollDesired = Math.Round(scroll?.DesiredSize.Height ?? 0, 1),
            scrollMax = scroll?.MaxHeight ?? 0
        };
    }

    private static T? FindChild<T>(DependencyObject root) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;
            var nested = FindChild<T>(child);
            if (nested != null) return nested;
        }
        return null;
    }
}
