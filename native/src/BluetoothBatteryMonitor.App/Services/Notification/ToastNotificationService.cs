using System;
using System.Collections.Concurrent;
using BluetoothBatteryMonitor.App.Models;
using Microsoft.Toolkit.Uwp.Notifications;

namespace BluetoothBatteryMonitor.App.Services.Notification;

public class ToastNotificationService
{
    /// <summary>Battery alert tier of a device relative to the effective thresholds.</summary>
    protected internal enum BatteryTier
    {
        Normal = 0,
        Low = 1,
        Critical = 2,
        /// <summary>Unknown / not tracked yet — the first observation never notifies.</summary>
        Untracked = 3
    }

    // Transition-based alerts: a toast fires only when a device's tier drops (Normal→Low,
    // Normal/Low→Critical). Staying in a tier never re-notifies; the tier re-arms when the
    // battery recovers above the threshold, the device charges, or it disconnects.
    private readonly ConcurrentDictionary<string, BatteryTier> _lastTiers = new();

    public virtual void CheckAndNotifyBattery(BluetoothDeviceModel device, AppSettings settings)
    {
        if (!settings.EnableNotifications) return;

        string key = $"{device.Id}_{device.BluetoothAddress}";

        // Charging or disconnected devices are out of the alert flow; clear the tracked tier so
        // a later drop notifies again (competitor-standard re-arm semantics).
        if (!device.IsConnected || device.IsCharging)
        {
            _lastTiers.TryRemove(key, out _);
            return;
        }

        int? levelVal = device.EffectiveBatteryLevel ?? device.BatteryLevel;
        if (!levelVal.HasValue)
        {
            _lastTiers.TryRemove(key, out _);
            return;
        }

        int level = levelVal.Value;

        // Use device-specific threshold if set; otherwise fallback to global threshold
        int lowThreshold = settings.GetEffectiveLowBatteryThreshold(device.Id, device.BluetoothAddress);

        // If device's low battery threshold is less than or equal to global critical threshold (e.g. 10% for a mouse),
        // scale down the critical threshold proportionally so warning triggers prior to shutdown.
        int criticalThreshold = Math.Min(settings.CriticalBatteryThreshold, Math.Max(1, lowThreshold / 2));

        var tier = level <= criticalThreshold ? BatteryTier.Critical
            : level <= lowThreshold ? BatteryTier.Low
            : BatteryTier.Normal;

        var previous = _lastTiers.GetOrAdd(key, BatteryTier.Untracked);

        // Record the new tier regardless of whether we notify, so repeated observations of the
        // same level never fire twice. The first sighting of an already-low or critical device
        // still notifies once (useful at app start); Untracked compares as Normal.
        _lastTiers[key] = tier;

        var previousComparable = previous == BatteryTier.Untracked ? BatteryTier.Normal : previous;
        if (tier <= previousComparable) return;

        bool notified = TrySendNotification(device, tier == BatteryTier.Critical ? criticalThreshold : lowThreshold, tier == BatteryTier.Critical);

        if (!notified)
        {
            // Restore the previous tier so the next observation retries the transition
            // (e.g. the toast service was temporarily unavailable).
            _lastTiers[key] = previous;
        }
    }

    protected internal virtual bool TrySendNotification(BluetoothDeviceModel device, int threshold, bool isCritical)
    {
        return ShowToast(device, isCritical);
    }

    protected virtual bool ShowToast(BluetoothDeviceModel device, bool isCritical)    {
        int? displayLevel = device.EffectiveBatteryLevel ?? device.BatteryLevel;

        string title = isCritical
            ? string.Format(Localization.LocalizationService.GetString("Toast_CriticalTitle"), device.DisplayName)
            : string.Format(Localization.LocalizationService.GetString("Toast_LowTitle"), device.DisplayName);

        string message = isCritical
            ? string.Format(Localization.LocalizationService.GetString("Toast_CriticalBody"), device.DisplayName, displayLevel)
            : string.Format(Localization.LocalizationService.GetString("Toast_LowBody"), device.DisplayName, displayLevel);

        try
        {
            new ToastContentBuilder()
                .AddText(title)
                .AddText(message)
                .AddAttributionText(Localization.LocalizationService.GetString("App_Title"))
                .Show();
            return true;
        }
        catch
        {
            // Suppress error if notification permissions are denied or shell service is unavailable
            return false;
        }
    }

    /// <summary>
    /// Plain informational toast (update finished / update did not finish). Ignores the battery
    /// thresholds and the notification preference: these are one-off messages about the app itself.
    /// </summary>
    public virtual bool ShowMessage(string title, string message)
    {
        try
        {
            new ToastContentBuilder()
                .AddText(title)
                .AddText(message)
                .AddAttributionText(Localization.LocalizationService.GetString("App_Title"))
                .Show();
            return true;
        }
        catch
        {
            return false;
        }
    }
}
