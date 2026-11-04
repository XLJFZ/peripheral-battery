extern alias MonitorApp;
using System.Windows;
using Xunit;
using AboutWindow = MonitorApp::BluetoothBatteryMonitor.App.Views.AboutWindow;

public class AboutWindowTests
{
    [Fact]
    public void AppIconUsesHighestResolutionEmbeddedFrame()
    {
        // Regression: BitmapImage.DecodePixelWidth does not make the ICO decoder
        // choose its 256px frame; the About window rendered a tiny frame at 64px.
        _ = Application.Current ?? new Application();
        var icon = AboutWindow.LoadAppIcon();

        Assert.Equal(256, icon.PixelWidth);
        Assert.Equal(256, icon.PixelHeight);
    }
}
