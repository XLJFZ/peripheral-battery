extern alias MonitorApp;
using Xunit;
using Settings = MonitorApp::BluetoothBatteryMonitor.App.Models.AppSettings;
using Appearance = MonitorApp::BluetoothBatteryMonitor.App.Helpers.TrayAppearance;
using Generator = MonitorApp::BluetoothBatteryMonitor.App.Helpers.TrayIconGenerator;

public class PeripheralAppearanceTests
{
    [Fact] public void WiredGamepadNeverBecomesFakePercentage()
    {
        Assert.Equal("未报告电量",MonitorApp::BluetoothBatteryMonitor.App.Services.Bluetooth.XInputBatteryProvider.Describe(1,3));
        Assert.Equal("电量低",MonitorApp::BluetoothBatteryMonitor.App.Services.Bluetooth.XInputBatteryProvider.Describe(2,1));
    }
    [Theory]
    [InlineData(0,"#FF6577")]
    [InlineData(9,"#FF6577")]
    [InlineData(10,"#FFC048")]
    [InlineData(30,"#FFC048")]
    [InlineData(31,"#00C98D")]
    [InlineData(100,"#00C98D")]
    public void ExactBatteryBoundaries(int level,string expected)
    {
        var color=Appearance.GetColor(level,new Settings());
        Assert.Equal(System.Drawing.ColorTranslator.FromHtml(expected).ToArgb(),color.ToArgb());
    }
    [Fact] public void CustomColorsAndFixedMode()
    {
        var s=new Settings{TrayAutoColor=true,TrayHighColor="#123456",TrayFixedColor="#AABBCC"};
        Assert.Equal(System.Drawing.ColorTranslator.FromHtml("#123456").ToArgb(),Appearance.GetColor(50,s).ToArgb());
        s.TrayAutoColor=false;
        Assert.Equal(System.Drawing.ColorTranslator.FromHtml("#AABBCC").ToArgb(),Appearance.GetColor(2,s).ToArgb());
    }
    [Fact] public void InvalidColorFallsBack()=>Assert.Equal("#00C98D",Appearance.ValidColor("bad","#00C98D"));
    [Theory]
    [InlineData("System",0,"普通版")]
    [InlineData("Dark",0,"普通版")]
    [InlineData("Light",0,"普通版")]
    [InlineData("",0,"普通版")]
    [InlineData(null,0,"普通版")]
    [InlineData("液态玻璃",0,"普通版")]
    [InlineData("液态玻璃（低透明）",0,"普通版")]
    [InlineData("液态玻璃",1,"液态玻璃")]
    [InlineData("液态玻璃（低透明）",1,"液态玻璃")]
    [InlineData("普通版",1,"普通版")]
    [InlineData("未知值",1,"普通版")]
    public void AppearanceDefaultsToNormalAndKeepsExplicitChoice(string? theme,int version,string expected)
        => Assert.Equal(expected,Settings.MigrateTheme(theme,version));

    [Fact] public void FreshProfileUsesNormalAppearance()
    {
        var settings=new Settings();
        Assert.Equal("普通版",settings.Theme);
        Assert.Equal("普通版",Settings.MigrateTheme(settings.Theme,settings.ThemeVersion));
    }
    [Theory][InlineData("圆环")][InlineData("数字")][InlineData("圆环 + 数字")]
    public void IconRendersAtTaskbarSize(string style)
    {
        using var image=Generator.Render(30,false,true,new Settings{TrayIconStyle=style},16);
        Assert.Equal(16,image.Width);
        Assert.True(Enumerable.Range(0,16).Any(x=>Enumerable.Range(0,16).Any(y=>image.GetPixel(x,y).A>0)));
    }
}
