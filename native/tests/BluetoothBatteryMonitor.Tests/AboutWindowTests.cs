extern alias MonitorApp;
using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Xunit;
using AboutWindow = MonitorApp::BluetoothBatteryMonitor.App.Views.AboutWindow;

public class AboutWindowTests
{
    [Fact]
    public void AppIconUsesHighestResolutionEmbeddedFrame()
    {
        // Regression: BitmapImage.DecodePixelWidth does not make the ICO decoder
        // choose its 256px frame; the About window rendered a tiny frame at 64px.
        var size = RunInSta(() =>
        {
            var icon = AboutWindow.LoadAppIcon();
            return (icon.PixelWidth, icon.PixelHeight);
        });

        Assert.Equal(256, size.PixelWidth);
        Assert.Equal(256, size.PixelHeight);
    }

    [Fact]
    public void FeatureTextWrapsInsteadOfClipping()
    {
        var wrappingModes = RunInSta(() =>
        {
            var window = new AboutWindow();
            try
            {
                var root = (Grid)window.Content;
                var scroll = root.Children.OfType<ScrollViewer>().Single();
                var cards = (StackPanel)scroll.Content;
                var featureCard = (StackPanel)((Border)cards.Children[1]).Child;
                return featureCard.Children.OfType<TextBlock>().Skip(1)
                    .Select(text => text.TextWrapping).ToArray();
            }
            finally { window.Close(); }
        });

        Assert.Equal(5, wrappingModes.Length);
        Assert.All(wrappingModes, mode => Assert.Equal(TextWrapping.Wrap, mode));
    }

    private static T RunInSta<T>(Func<T> action) => BluetoothBatteryMonitor.Tests.WpfTestHost.Run(action);
}
