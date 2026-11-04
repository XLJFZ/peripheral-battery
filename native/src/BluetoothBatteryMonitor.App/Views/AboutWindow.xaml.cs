using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Wpf.Ui.Controls;
using BluetoothBatteryMonitor.App.Services.Localization;
using BluetoothBatteryMonitor.App.Services.Update;

namespace BluetoothBatteryMonitor.App.Views;

public partial class AboutWindow : FluentWindow
{
    public AboutWindow()
    {
        InitializeComponent();
        try
        {
            Icon = LoadAppIcon();
            AppIcon.Source = Icon;
        }
        catch { }

        try
        {
            // The raw resource string carries a {0} placeholder; fill it with the real
            // assembly version so the dialog can never drift from the actual build.
            VersionText.Text = LocalizationService.GetString("About_Version", UpdateCheckService.GetCurrentVersion());
        }
        catch { }
    }

    internal static BitmapSource LoadAppIcon()
    {
        // BitmapImage.DecodePixelWidth does not select the requested ICO frame.
        // Decode the container explicitly and use its largest frame for the 64px image.
        var decoder = BitmapDecoder.Create(
            new Uri("pack://application:,,,/BluetoothBatteryMonitor.App;component/Assets/app.ico", UriKind.Absolute),
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        var icon = decoder.Frames.OrderByDescending(frame => frame.PixelWidth * frame.PixelHeight).First();
        icon.Freeze();
        return icon;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
    private void OpenRepository(object sender,RoutedEventArgs e)=>Services.RepositoryLink.Open();

    private async void OnCheckUpdatesClick(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn)
        {
            btn.IsEnabled = false;
            btn.Content = LocalizationService.GetString("Update_Checking");
            try
            {
                var service = new UpdateCheckService();
                var result = await service.CheckForUpdatesAsync();
                if (result.HasUpdate)
                {
                    btn.Content = LocalizationService.GetString("Update_Available", result.LatestVersion);
                    UpdateCheckService.OpenUrl(result.DownloadUrl ?? result.ReleaseUrl ?? "https://github.com/Kaandonmez/BluetoothBatteryMonitor/releases/latest");
                }
                else if (!string.IsNullOrEmpty(result.ErrorMessage))
                {
                    btn.Content = LocalizationService.GetString("Update_Error");
                }
                else
                {
                    btn.Content = LocalizationService.GetString("Update_UpToDate", result.CurrentVersion);
                }
            }
            catch
            {
                btn.Content = LocalizationService.GetString("Update_Error");
            }
            finally
            {
                btn.IsEnabled = true;
            }
        }
    }
}
