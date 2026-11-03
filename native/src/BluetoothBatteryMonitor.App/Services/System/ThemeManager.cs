using System;
using Microsoft.Win32;
using Wpf.Ui.Appearance;

namespace BluetoothBatteryMonitor.App.Services.System;

public static class ThemeManager
{
    public const string AppearanceGlass = "液态玻璃";
    public const string AppearanceFrosted = "毛玻璃";
    public const string AppearanceNormal = "普通版";

    /// <summary>
    /// Collapses legacy values (System / Dark / Light / 液态玻璃（低透明）) into the three
    /// supported appearance modes. Anything unknown falls back to the default 普通版.
    /// </summary>
    public static string Normalize(string? theme)
    {
        if (string.IsNullOrWhiteSpace(theme)) return AppearanceNormal;
        if (theme.StartsWith(AppearanceGlass, StringComparison.Ordinal)) return AppearanceGlass;
        return theme.Equals(AppearanceFrosted, StringComparison.Ordinal) ? AppearanceFrosted : AppearanceNormal;
    }

    private const string RegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string RegistryValueName = "AppsUseLightTheme";

    public static event EventHandler<bool>? ThemeChanged;

    private static bool _isInitialized;

    public static void Initialize()
    {
        if (_isInitialized) return;
        _isInitialized = true;
        // The FluentWindow Loaded handler must not apply the backdrop synchronously: Loaded can
        // fire while the Dispatcher is inside a CompositionTarget.Rendering walk, and changing
        // the WPF-UI backdrop property then throws (Freezable context) — crash fixed in v0.3.18.
        global::System.Windows.EventManager.RegisterClassHandler(typeof(Wpf.Ui.Controls.FluentWindow),global::System.Windows.FrameworkElement.LoadedEvent,
            new global::System.Windows.RoutedEventHandler((sender,_)=>{
                if(sender is not global::System.Windows.Window window)return;
                window.Dispatcher.BeginInvoke(new Action(()=>GlassAppearance.ApplyWindow(window)),global::System.Windows.Threading.DispatcherPriority.Background);
            }));
        global::System.Windows.SystemParameters.StaticPropertyChanged += (_,e)=>
        {
            if(e.PropertyName==nameof(global::System.Windows.SystemParameters.HighContrast))
                // BeginInvoke (not Invoke): this can be raised mid-render; a synchronous
                // re-entry would apply the theme inside the render pass.
                global::System.Windows.Application.Current?.Dispatcher.BeginInvoke(ApplyCurrentTheme,global::System.Windows.Threading.DispatcherPriority.Background);
        };

        SystemEvents.UserPreferenceChanged += (s, e) =>
        {
            if (e.Category == UserPreferenceCategory.General)
            {
                // SystemEvents can be raised from any thread (including inside a render pass on
                // the UI thread); route through the dispatcher queue instead of running inline.
                var app=global::System.Windows.Application.Current;
                app?.Dispatcher.BeginInvoke(ApplyCurrentTheme,global::System.Windows.Threading.DispatcherPriority.Background);
            }
        };

        ApplyCurrentTheme();
    }

    public static bool IsWindowsDarkTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
            var value = key?.GetValue(RegistryValueName);
            if (value is int lightThemeEnabled)
            {
                return lightThemeEnabled == 0;
            }
        }
        catch
        {
            // Fallback
        }

        return true; // Default to Dark Theme
    }

    public static void ApplyTheme(string themePreference) => ApplyTheme(themePreference, null);

    /// <param name="forcedDark">Preview/testing override; production callers pass null.</param>
    public static void ApplyTheme(string themePreference, bool? forcedDark)
    {
        if(global::System.Windows.Application.Current is { } app && !app.Dispatcher.CheckAccess())
        {
            // BeginInvoke, not Invoke: the caller's frame may be inside a WPF render pass and a
            // synchronous re-entry would apply the theme mid-render (Freezable context crash).
            app.Dispatcher.BeginInvoke(()=>ApplyTheme(themePreference,forcedDark),global::System.Windows.Threading.DispatcherPriority.Background);
            return;
        }
        // 普通版 keeps its dark desktop UI; both glass materials follow the Windows app theme.
        bool isDark = forcedDark ?? (Normalize(themePreference) != AppearanceNormal ? IsWindowsDarkTheme() : true);

        try
        {
            ApplicationThemeManager.Apply(isDark ? ApplicationTheme.Dark : ApplicationTheme.Light);
        }
        catch
        {
            // Suppress if outside UI thread or window is not loaded yet
        }

        GlassAppearance.Apply(themePreference,isDark);
        ThemeChanged?.Invoke(null, isDark);
    }

    public static void ApplyCurrentTheme()
    {
        var settings = Models.AppSettings.Load();
        ApplyTheme(settings.Theme);
    }
}
