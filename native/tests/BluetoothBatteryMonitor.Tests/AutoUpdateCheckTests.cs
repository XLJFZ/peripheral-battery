extern alias MonitorApp;
using System;
using Xunit;
using AppSettings = MonitorApp::BluetoothBatteryMonitor.App.Models.AppSettings;
using AutoUpdateCheckService = MonitorApp::BluetoothBatteryMonitor.App.Services.Update.AutoUpdateCheckService;

public class AutoUpdateCheckTests
{
    private static readonly DateTime Now = new(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void DefaultsToWeeklyAndNormalizesUnknownValues()
    {
        Assert.Equal(AutoUpdateCheckService.Weekly, new AppSettings().AutoUpdateCheckFrequency);
        Assert.Equal(AutoUpdateCheckService.Weekly, AutoUpdateCheckService.Normalize("unexpected"));
        Assert.Equal(AutoUpdateCheckService.Startup, AutoUpdateCheckService.Normalize("startup"));
        Assert.Equal(AutoUpdateCheckService.Off, AutoUpdateCheckService.Normalize("off"));
    }

    [Fact]
    public void OffNeverChecksAndStartupAlwaysChecks()
    {
        Assert.False(AutoUpdateCheckService.ShouldCheck(new AppSettings { AutoUpdateCheckFrequency = "off" }, Now));
        Assert.True(AutoUpdateCheckService.ShouldCheck(new AppSettings { AutoUpdateCheckFrequency = "startup", AutoUpdateLastCheckUtc = Now }, Now));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(6, false)]
    [InlineData(7, true)]
    [InlineData(-1, true)]
    public void WeeklyChecksOnlyWhenDue(int daysSinceLastCheck, bool expected)
    {
        var settings = new AppSettings
        {
            AutoUpdateCheckFrequency = "weekly",
            AutoUpdateLastCheckUtc = Now.AddDays(-daysSinceLastCheck)
        };

        Assert.Equal(expected, AutoUpdateCheckService.ShouldCheck(settings, Now));
    }

    [Fact]
    public void WeeklyWithoutPreviousCheckIsDue()
    {
        Assert.True(AutoUpdateCheckService.ShouldCheck(new AppSettings(), Now));
    }
    [Fact]
    public async System.Threading.Tasks.Task OffDoesNotSendRequest()
    {
        var settings = new AppSettings { AutoUpdateCheckFrequency = "off" };
        var result = await AutoUpdateCheckService.CheckIfDueAsync(() => settings,
            _ => throw new Exception("Unexpected save"),
            () => throw new Exception("Unexpected request"), Now);
        Assert.Null(result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async System.Threading.Tasks.Task SuccessRecordsTimeAndPreservesConcurrentEdits(bool failed)
    {
        var settings = new AppSettings();
        bool saved = false;
        await AutoUpdateCheckService.CheckIfDueAsync(() => settings, _ => saved = true, () =>
        {
            settings = new AppSettings { AutoUpdateCheckFrequency = "off", LowBatteryThreshold = 35 };
            return System.Threading.Tasks.Task.FromResult(new MonitorApp::BluetoothBatteryMonitor.App.Services.Update.UpdateCheckResult(
                false, "0.4.0", "0.4.0", null, null, null, failed ? "offline" : null));
        }, Now);
        Assert.Equal(!failed, saved);
        Assert.Equal(failed ? (DateTime?)null : Now, settings.AutoUpdateLastCheckUtc);
        Assert.Equal("off", settings.AutoUpdateCheckFrequency);
        Assert.Equal(35, settings.LowBatteryThreshold);
    }
}
