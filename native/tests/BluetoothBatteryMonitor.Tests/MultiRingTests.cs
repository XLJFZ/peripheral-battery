extern alias MonitorApp;
using Xunit;
using Generator=MonitorApp::BluetoothBatteryMonitor.App.Helpers.TrayIconGenerator;
using Settings=MonitorApp::BluetoothBatteryMonitor.App.Models.AppSettings;
public class MultiRingTests
{
    [Theory][InlineData(16)][InlineData(32)][InlineData(64)]
    public void IndependentRingColorsRemainVisible(int size)
    {
        using var image=Generator.RenderMultiple(new[]{(80,false),(20,false),(5,true)},true,new Settings(),size);
        var pixels=Enumerable.Range(0,size).SelectMany(x=>Enumerable.Range(0,size).Select(y=>image.GetPixel(x,y))).ToList();
        Assert.Contains(pixels,c=>c.A>100&&c.G>c.R*1.3&&c.G>c.B);
        Assert.Contains(pixels,c=>c.A>100&&c.R>c.B*1.3&&c.G>c.B*1.3);
        Assert.Contains(pixels,c=>c.A>100&&c.R>c.G*1.3&&c.R>c.B*1.3);
    }
    [Fact] public void SingleDevicePreservesExistingRing()
    {
        var settings=new Settings();using var a=Generator.RenderMultiple(new[]{(65,false)},true,settings,16);using var b=Generator.Render(65,false,true,settings,16);
        for(int x=0;x<16;x++)for(int y=0;y<16;y++)Assert.Equal(a.GetPixel(x,y),b.GetPixel(x,y));
    }
}
