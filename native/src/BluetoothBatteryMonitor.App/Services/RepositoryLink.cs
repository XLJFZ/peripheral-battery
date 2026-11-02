using System.Diagnostics;
using System.Windows;

namespace BluetoothBatteryMonitor.App.Services;

public static class RepositoryLink
{
    public const string Url="https://github.com/XLJFZ/peripheral-battery";
    public static void Open()
    {
        try { Process.Start(new ProcessStartInfo(Url){UseShellExecute=true}); }
        catch { MessageBox.Show("无法打开浏览器。仓库地址：\n"+Url,"GitHub 仓库",MessageBoxButton.OK,MessageBoxImage.Information); }
    }
}
