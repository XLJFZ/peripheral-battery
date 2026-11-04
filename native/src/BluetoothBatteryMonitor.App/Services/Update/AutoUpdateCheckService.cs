using System;
using System.Threading.Tasks;
using BluetoothBatteryMonitor.App.Models;

namespace BluetoothBatteryMonitor.App.Services.Update;

/// <summary>Checks at launch when due; never downloads or installs automatically.</summary>
public static class AutoUpdateCheckService
{
    public const string Weekly = "weekly";
    public const string Startup = "startup";
    public const string Off = "off";

    public static string Normalize(string? value) => value switch
    {
        Startup => Startup,
        Off => Off,
        _ => Weekly
    };

    public static bool ShouldCheck(AppSettings settings, DateTime nowUtc)
    {
        var frequency = Normalize(settings.AutoUpdateCheckFrequency);
        if (frequency == Off) return false;
        if (frequency == Startup) return true;
        var last = settings.AutoUpdateLastCheckUtc;
        return !last.HasValue || last.Value > nowUtc || nowUtc - last.Value >= TimeSpan.FromDays(7);
    }

    public static async Task<UpdateCheckResult?> CheckIfDueAsync(
        Func<AppSettings> load, Action<AppSettings> save,
        Func<Task<UpdateCheckResult>> check, DateTime nowUtc)
    {
        if (!ShouldCheck(load(), nowUtc)) return null;
        var result = await check();
        // Retry failed requests on the next launch; reload to preserve settings edited in flight.
        if (string.IsNullOrEmpty(result.ErrorMessage))
        {
            var latest = load();
            latest.AutoUpdateLastCheckUtc = nowUtc;
            save(latest);
        }
        return result;
    }
}
