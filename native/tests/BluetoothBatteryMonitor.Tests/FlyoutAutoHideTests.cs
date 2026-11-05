extern alias MonitorApp;
using System.Linq;
using Xunit;
using AppSettings = MonitorApp::BluetoothBatteryMonitor.App.Models.AppSettings;
using LocalizationService = MonitorApp::BluetoothBatteryMonitor.App.Services.Localization.LocalizationService;
using SettingsViewModel = MonitorApp::BluetoothBatteryMonitor.App.ViewModels.SettingsViewModel;

public class FlyoutAutoHideTests
{
    [Theory]
    [InlineData(120,true,10)]
    [InlineData(300,true,10)]
    [InlineData(5,true,5)]
    [InlineData(120,false,120)]
    public void EmptyDeviceListRetriesWithoutFiveMinuteDelay(int configured,bool empty,int expected)
        =>Assert.Equal(expected,MonitorApp::BluetoothBatteryMonitor.App.Services.Bluetooth.BluetoothBatteryEngine.NextPollSeconds(configured,empty));
    [Theory]
    [InlineData(0, 0)]      // explicit "never"
    [InlineData(3, 3)]
    [InlineData(5, 5)]
    [InlineData(10, 10)]
    [InlineData(15, 15)]
    [InlineData(30, 30)]
    [InlineData(60, 60)]
    [InlineData(7, 5)]      // unknown value: default
    [InlineData(-1, 5)]
    [InlineData(61, 5)]
    [InlineData(100000, 5)]
    public void NormalizeAutoHide_KeepsKnownStepsAndDefaultsTheRest(int input, int expected)
    {
        Assert.Equal(expected, AppSettings.NormalizeAutoHide(input));
    }

    [Fact]
    public void OptionsCoverEverySupportedStep()
    {
        LocalizationService.ApplyLanguage("en");
        var seconds = SettingsViewModel.AvailableAutoHideOptions.Select(o => o.Seconds).ToHashSet();
        Assert.Equal(new[] { 0, 3, 5, 10, 15, 30, 60 }, seconds.OrderBy(s => s));
    }

    [Fact]
    public void ZeroRendersAsNeverInEveryLanguage()
    {
        foreach (var lang in new[] { "en", "zh", "tr" })
        {
            LocalizationService.ApplyLanguage(lang);
            var option = SettingsViewModel.AvailableAutoHideOptions.Single(o => o.Seconds == 0);
            Assert.False(string.IsNullOrWhiteSpace(option.DisplayName));
            Assert.NotEqual("Settings_AutoHideOff", option.DisplayName); // key resolved, not echoed
        }
    }

    [Fact]
    public void ChineseZeroOptionReadsAsNever()
    {
        LocalizationService.ApplyLanguage("zh");
        var option = SettingsViewModel.AvailableAutoHideOptions.Single(o => o.Seconds == 0);
        Assert.Equal("不自动隐藏", option.DisplayName);
    }
}
