using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;

namespace BluetoothBatteryMonitor.App.Views;

public sealed class TrayColorWindow : FluentWindow
{
    public string SelectedHex { get; private set; }
    private readonly Border sample=new(){Height=34,CornerRadius=new CornerRadius(6),Margin=new Thickness(0,8,0,8)};
    private readonly System.Windows.Controls.TextBox hex=new(){Margin=new Thickness(0,6,0,10)};
    private readonly Slider[] channels=new Slider[3];
    private bool updating;
    public TrayColorWindow(string current)
    {
        SelectedHex=current;Title="选择托盘颜色";Width=380;Height=430;ResizeMode=ResizeMode.NoResize;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;WindowBackdropType=WindowBackdropType.Mica;
        var panel=new StackPanel{Margin=new Thickness(24)};Content=panel;
        panel.Children.Add(new TextBlock{Text="选择颜色",FontSize=20,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,12)});
        var palette=new WrapPanel();panel.Children.Add(palette);
        foreach(string value in new[]{"#00C98D","#61B8FF","#B59AFF","#FFC048","#FF6577","#FFFFFF","#0A9D62","#2878DD","#8648D9","#F08A24","#E4445B","#202023"})
        {
            var button=new System.Windows.Controls.Button{Width=38,Height=29,Margin=new Thickness(3),Background=(Brush)new BrushConverter().ConvertFromString(value)!,BorderThickness=new Thickness(0)};
            button.Click+=(s,e)=>Set(value);palette.Children.Add(button);
        }
        panel.Children.Add(sample);
        for(int i=0;i<3;i++)
        {
            var row=new DockPanel{Margin=new Thickness(0,4,0,4)};
            row.Children.Add(new TextBlock{Text=new[]{"R","G","B"}[i],Width=24,VerticalAlignment=VerticalAlignment.Center});
            channels[i]=new Slider{Minimum=0,Maximum=255,TickFrequency=1,IsSnapToTickEnabled=true};
            channels[i].ValueChanged+=(s,e)=>{if(!updating)Set($"#{(int)channels[0].Value:X2}{(int)channels[1].Value:X2}{(int)channels[2].Value:X2}");};
            row.Children.Add(channels[i]);panel.Children.Add(row);
        }
        hex.TextChanged+=(s,e)=>{if(!updating && System.Text.RegularExpressions.Regex.IsMatch(hex.Text,"^#[0-9A-Fa-f]{6}$"))Set(hex.Text);};
        panel.Children.Add(hex);
        var done=new System.Windows.Controls.Button{Content="应用颜色",HorizontalAlignment=HorizontalAlignment.Right,Padding=new Thickness(20,8,20,8)};
        done.Click+=(s,e)=>{DialogResult=true;Close();};panel.Children.Add(done);Set(current);
    }
    private void Set(string value)
    {
        updating=true;
        var color=(Color)ColorConverter.ConvertFromString(value);
        SelectedHex=value.ToUpperInvariant();sample.Background=new SolidColorBrush(color);hex.Text=SelectedHex;
        if(channels[2]!=null){channels[0].Value=color.R;channels[1].Value=color.G;channels[2].Value=color.B;}
        updating=false;
    }
}
