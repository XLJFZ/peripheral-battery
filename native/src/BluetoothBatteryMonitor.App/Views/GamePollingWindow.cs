using System.Windows;
using System.Windows.Controls;
using BluetoothBatteryMonitor.App.Models;
using BluetoothBatteryMonitor.App.Services;

namespace BluetoothBatteryMonitor.App.Views;

public sealed class GamePollingWindow : Window
{
    public GamePollingWindow()
    {
        Title="游戏回报率调度";Width=500;Height=520;MinHeight=520;MinWidth=500;Background=new global::System.Windows.Media.SolidColorBrush(global::System.Windows.Media.Color.FromRgb(32,33,36));Foreground=global::System.Windows.Media.Brushes.White;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var settings=AppSettings.Load();var panel=new StackPanel{Margin=new Thickness(24)};Content=panel;
        panel.Children.Add(new TextBlock{Text="已验证：炼狱蝰蛇 V3 Pro · 00B7 接收器\n当前接收器开放 125–1000Hz；8000Hz 需要另行验证高回报率接收器。",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,16)});
        var enabled=new CheckBox{Content="开启游戏前台全屏调度",IsChecked=settings.EnableGamePolling};panel.Children.Add(enabled);
        ComboBox Rate(string label,int value)
        {
            panel.Children.Add(new TextBlock{Text=label,Margin=new Thickness(0,12,0,4)});
            var box=new ComboBox{ItemsSource=new[]{125,250,500,1000},SelectedItem=value};panel.Children.Add(box);return box;
        }
        var desktop=Rate("桌面回报率（Hz）",settings.DesktopPollingHz);var game=Rate("游戏回报率（Hz）",settings.GamePollingHz);
        panel.Children.Add(new TextBlock{Text="游戏进程名，逗号分隔",Margin=new Thickness(0,12,0,4)});
        var names=new TextBox{Text=settings.PollingGames};panel.Children.Add(names);
        var status=new TextBlock{Text=GamePollingService.Instance.Status,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,8)};panel.Children.Add(status);
        var timer=new global::System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};
        timer.Tick+=(_,_)=>status.Text=GamePollingService.Instance.Status;timer.Start();Closed+=(_,_)=>timer.Stop();
        var save=new Button{Content="保存并应用",Padding=new Thickness(12,6,12,6)};panel.Children.Add(save);
        save.Click+=async (_,_)=>
        {
            var current=AppSettings.Load();current.EnableGamePolling=enabled.IsChecked==true;
            current.DesktopPollingHz=(int)(desktop.SelectedItem??500);current.GamePollingHz=(int)(game.SelectedItem??1000);current.PollingGames=names.Text;current.Save();
            GamePollingService.Instance.ResetFailure();await GamePollingService.Instance.Tick();status.Text=GamePollingService.Instance.Status;
        };
    }
}
