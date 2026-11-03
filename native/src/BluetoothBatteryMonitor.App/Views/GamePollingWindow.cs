using System.Windows;
using System.Windows.Controls;
using BluetoothBatteryMonitor.App.Models;
using BluetoothBatteryMonitor.App.Services;
using BluetoothBatteryMonitor.App.Services.Localization;

namespace BluetoothBatteryMonitor.App.Views;

public sealed class GamePollingWindow : Window
{
    public GamePollingWindow()
    {
        Title=LocalizationService.GetString("GamePolling_Title");Width=500;Height=520;MinHeight=520;MinWidth=500;Background=new global::System.Windows.Media.SolidColorBrush(global::System.Windows.Media.Color.FromRgb(32,33,36));Foreground=global::System.Windows.Media.Brushes.White;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var settings=AppSettings.Load();var panel=new StackPanel{Margin=new Thickness(24)};Content=panel;
        panel.Children.Add(new TextBlock{Text=LocalizationService.GetString("GamePolling_Note"),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,16)});
        var enabled=new CheckBox{Content=LocalizationService.GetString("GamePolling_Enable"),IsChecked=settings.EnableGamePolling};panel.Children.Add(enabled);
        ComboBox Rate(string label,int value)
        {
            panel.Children.Add(new TextBlock{Text=label,Margin=new Thickness(0,12,0,4)});
            var box=new ComboBox{ItemsSource=new[]{125,250,500,1000},SelectedItem=value};panel.Children.Add(box);return box;
        }
        var desktop=Rate(LocalizationService.GetString("GamePolling_DesktopRate"),settings.DesktopPollingHz);var game=Rate(LocalizationService.GetString("GamePolling_GameRate"),settings.GamePollingHz);
        panel.Children.Add(new TextBlock{Text=LocalizationService.GetString("GamePolling_Processes"),Margin=new Thickness(0,12,0,4)});
        var names=new TextBox{Text=settings.PollingGames};panel.Children.Add(names);
        var status=new TextBlock{Text=GamePollingService.Instance.Status,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,8)};panel.Children.Add(status);
        var timer=new global::System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};
        timer.Tick+=(_,_)=>status.Text=GamePollingService.Instance.Status;timer.Start();Closed+=(_,_)=>timer.Stop();
        var save=new Button{Content=LocalizationService.GetString("GamePolling_Save"),Padding=new Thickness(12,6,12,6)};panel.Children.Add(save);
        save.Click+=async (_,_)=>
        {
            var current=AppSettings.Load();current.EnableGamePolling=enabled.IsChecked==true;
            current.DesktopPollingHz=(int)(desktop.SelectedItem??500);current.GamePollingHz=(int)(game.SelectedItem??1000);current.PollingGames=names.Text;current.Save();
            GamePollingService.Instance.ResetFailure();await GamePollingService.Instance.Tick();status.Text=GamePollingService.Instance.Status;
        };
    }
}
