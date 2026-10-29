using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace BluetoothBatteryMonitor.App.Services;

/// <summary>Exercise the published UI dependencies without scanning devices or touching settings.</summary>
public static class UiPackageSmoke
{
    public static async Task RunAsync(string reportPath)
    {
        var accessibility = Assembly.Load("Accessibility, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35");
        if (!File.Exists(accessibility.Location))
            throw new FileNotFoundException("Accessibility must be present in the published folder.");
        var button = new Button { Content = "Package popup check" };
        var window = new Window { Width=180, Height=90, Left=-10000, Top=-10000,
            ShowInTaskbar=false, WindowStyle=WindowStyle.None, Content=button,
            WindowStartupLocation=WindowStartupLocation.Manual };
        var menu = new ContextMenu { PlacementTarget=button, Placement=PlacementMode.Bottom };
        menu.Items.Add(new MenuItem { Header="Menu dependency check" });
        var tooltip = new ToolTip { Content="Tooltip dependency check", PlacementTarget=button, Placement=PlacementMode.Bottom };
        try
        {
            window.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            menu.IsOpen=true;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            bool menuOpened=menu.IsOpen && PresentationSource.FromVisual(menu)!=null;
            menu.IsOpen=false;
            tooltip.IsOpen=true;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            bool tooltipOpened=tooltip.IsOpen && PresentationSource.FromVisual(tooltip)!=null;
            if(!menuOpened || !tooltipOpened)throw new InvalidOperationException("Published popup smoke check failed.");
            await File.WriteAllTextAsync(reportPath,JsonSerializer.Serialize(new {
                success=true,menuOpened,tooltipOpened,accessibilityAssembly=accessibility.Location
            }));
        }
        finally { tooltip.IsOpen=false;menu.IsOpen=false;window.Close(); }
    }
}
