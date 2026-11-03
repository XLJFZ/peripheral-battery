using System.Windows;
using System.Windows.Controls;
using BluetoothBatteryMonitor.App.Services.Localization;

namespace BluetoothBatteryMonitor.App.Views;

/// <summary>
/// Minimal modal input dialog for renaming a device. Clearing the field and saving removes the
/// custom name, restoring the raw system name.
/// </summary>
public sealed class RenameDeviceWindow : Window
{
    public string? DeviceName { get; private set; }

    public RenameDeviceWindow(string currentName)
    {
        Title = LocalizationService.GetString("Rename_Title");
        Width = 420;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(32, 33, 36));
        Foreground = System.Windows.Media.Brushes.White;

        var panel = new StackPanel { Margin = new Thickness(24) };
        Content = panel;

        panel.Children.Add(new TextBlock
        {
            Text = LocalizationService.GetString("Rename_Prompt"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        });

        var input = new TextBox { Text = currentName, Padding = new Thickness(6, 4, 6, 4) };
        panel.Children.Add(input);
        input.Loaded += (_, _) => { input.SelectAll(); input.Focus(); };

        panel.Children.Add(new TextBlock
        {
            Text = LocalizationService.GetString("Rename_Hint"),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11,
            Opacity = 0.7,
            Margin = new Thickness(0, 8, 0, 16)
        });

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        panel.Children.Add(buttons);

        var ok = new Button { Content = LocalizationService.GetString("Rename_Save"), Padding = new Thickness(14, 6, 14, 6), IsDefault = true };
        var cancel = new Button { Content = LocalizationService.GetString("Rename_Cancel"), Padding = new Thickness(14, 6, 14, 6), IsCancel = true, Margin = new Thickness(8, 0, 0, 0) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        ok.Click += (_, _) => { DeviceName = input.Text?.Trim(); DialogResult = true; };
    }
}
