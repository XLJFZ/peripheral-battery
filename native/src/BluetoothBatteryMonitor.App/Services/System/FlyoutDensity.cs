using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using BluetoothBatteryMonitor.App.Views;

namespace BluetoothBatteryMonitor.App.Services.System;

/// <summary>
/// Three panel sizes. Every knob lives in application resources so the flyout can resize
/// without rebuilding its template; the window height is re-fitted afterwards.
/// </summary>
public static class FlyoutDensity
{
    public const string Compact = "紧凑";
    public const string Standard = "标准";
    public const string Spacious = "宽松";

    public static IReadOnlyList<string> Options { get; } = new[] { Compact, Standard, Spacious };

    public static string Normalize(string? value)
        => value == Compact || value == Spacious ? value : Standard;

    private sealed record Profile(
        double Width,
        Thickness HeaderPadding,
        Thickness ListPadding,
        Thickness CardPadding,
        Thickness CardMargin,
        Thickness FooterPadding,
        double IconSize,
        double TitleSize,
        double StatusSize,
        double NameSize,
        double MetaSize,
        double BatterySize);

    private static readonly Profile CompactProfile = new(
        Width: 344,
        HeaderPadding: new Thickness(12, 10, 12, 8),
        ListPadding: new Thickness(8, 6, 8, 6),
        CardPadding: new Thickness(10, 8, 10, 8),
        CardMargin: new Thickness(0, 3, 0, 4),
        FooterPadding: new Thickness(10, 7, 10, 7),
        IconSize: 30,
        TitleSize: 14,
        StatusSize: 11,
        NameSize: 12.5,
        MetaSize: 10,
        BatterySize: 13);

    private static readonly Profile StandardProfile = new(
        Width: 416,
        HeaderPadding: new Thickness(16, 14, 16, 10),
        ListPadding: new Thickness(12, 8, 12, 8),
        CardPadding: new Thickness(14, 12, 14, 12),
        CardMargin: new Thickness(0, 4, 0, 6),
        FooterPadding: new Thickness(14, 10, 14, 10),
        IconSize: 36,
        TitleSize: 16,
        StatusSize: 12,
        NameSize: 13.5,
        MetaSize: 11,
        BatterySize: 14);

    private static readonly Profile SpaciousProfile = new(
        Width: 472,
        HeaderPadding: new Thickness(20, 18, 20, 14),
        ListPadding: new Thickness(16, 10, 16, 10),
        CardPadding: new Thickness(18, 16, 18, 16),
        CardMargin: new Thickness(0, 6, 0, 8),
        FooterPadding: new Thickness(18, 12, 18, 12),
        IconSize: 42,
        TitleSize: 17,
        StatusSize: 12.5,
        NameSize: 14.5,
        MetaSize: 11.5,
        BatterySize: 16);

    private static Profile Resolve(string? density) => Normalize(density) switch
    {
        Compact => CompactProfile,
        Spacious => SpaciousProfile,
        _ => StandardProfile
    };

    public static double WidthOf(string? density) => Resolve(density).Width;

    /// <summary>Writes the resource set and re-fits every open flyout.</summary>
    public static void Apply(string? density)
    {
        var profile = Resolve(density);
        var resources = Application.Current?.Resources;
        if (resources == null) return;

        resources["FlyoutWidth"] = profile.Width;
        resources["FlyoutHeaderPadding"] = profile.HeaderPadding;
        resources["FlyoutListPadding"] = profile.ListPadding;
        resources["FlyoutCardPadding"] = profile.CardPadding;
        resources["FlyoutCardMargin"] = profile.CardMargin;
        resources["FlyoutFooterPadding"] = profile.FooterPadding;
        resources["FlyoutIconSize"] = profile.IconSize;
        resources["FlyoutIconRadius"] = new CornerRadius(profile.IconSize / 2);
        resources["FlyoutTitleSize"] = profile.TitleSize;
        resources["FlyoutStatusSize"] = profile.StatusSize;
        resources["FlyoutNameSize"] = profile.NameSize;
        resources["FlyoutMetaSize"] = profile.MetaSize;
        resources["FlyoutBatterySize"] = profile.BatterySize;

        foreach (Window window in Application.Current.Windows)
        {
            if (window is not FlyoutWindow flyout) continue;
            flyout.ApplyDensity(profile.Width);
        }
    }
}
