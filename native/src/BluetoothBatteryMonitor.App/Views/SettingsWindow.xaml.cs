using System.Collections.Generic;
using System.Windows;
using BluetoothBatteryMonitor.App.Models;
using BluetoothBatteryMonitor.App.ViewModels;
using Wpf.Ui.Controls;

namespace BluetoothBatteryMonitor.App.Views;

public partial class SettingsWindow : FluentWindow
{
    private readonly SettingsViewModel _viewModel;

    public SettingsWindow(IEnumerable<BluetoothDeviceModel>? knownDevices = null)
    {
        InitializeComponent();
        try
        {
            Icon = new System.Windows.Media.Imaging.BitmapImage(
                new Uri("pack://application:,,,/BluetoothBatteryMonitor.App;component/Assets/app.ico", UriKind.Absolute));
        }
        catch { }
        _viewModel = new SettingsViewModel(knownDevices);
        DataContext = _viewModel;
        _viewModel.PropertyChanged += (sender,args) => UpdateTrayPreview();
        UpdateTrayPreview();
    }

    private void ChooseTrayColor(object sender,System.Windows.Input.MouseButtonEventArgs e)
    {
        string? key=(sender as FrameworkElement)?.Tag as string;
        var prop=key==null?null:typeof(SettingsViewModel).GetProperty(key);
        if(prop==null)return;
        string current=Helpers.TrayAppearance.ValidColor(prop.GetValue(_viewModel) as string,"#00C98D");
        var dialog=new TrayColorWindow(current){Owner=this};
        if(dialog.ShowDialog()==true)prop.SetValue(_viewModel,dialog.SelectedHex);
    }
    private void UpdateTrayPreview()
    {
        var settings=new AppSettings { TrayAutoColor=_viewModel.TrayAutoColor,
            TrayHighColor=_viewModel.TrayHighColor,TrayMidColor=_viewModel.TrayMidColor,
            TrayLowColor=_viewModel.TrayLowColor,TrayFixedColor=_viewModel.TrayFixedColor,
            TrayIconStyle=_viewModel.TrayIconStyle,TrayRingWidth=_viewModel.TrayRingWidth };
        void Set(System.Windows.Controls.Image target,int level)
        {
            using var icon=Helpers.TrayIconGenerator.Generate(level,false,true,settings);
            var source=System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(icon.Handle,
                Int32Rect.Empty,System.Windows.Media.Imaging.BitmapSizeOptions.FromWidthAndHeight(32,32));
            source.Freeze();target.Source=source;
        }
        Set(PreviewHigh,80);Set(PreviewMid,30);Set(PreviewLow,9);
    }
    private void OnSaveAndCloseClick(object sender, RoutedEventArgs e)
    {
        _viewModel.SaveCommand.Execute(null);
        Close();
    }
}
