using System;
using Microsoft.Win32;
using Wpf.Ui.Appearance;

namespace BluetoothBatteryMonitor.App.Services.System;

public static class ThemeManager
{
    private const string RegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string RegistryValueName = "AppsUseLightTheme";

    public static event EventHandler<bool>? ThemeChanged;

    private static bool _isInitialized;

    public static void Initialize()
    {
        if (_isInitialized) return;
        _isInitialized = true;
        global::System.Windows.EventManager.RegisterClassHandler(typeof(Wpf.Ui.Controls.FluentWindow),global::System.Windows.FrameworkElement.LoadedEvent,
            new global::System.Windows.RoutedEventHandler((sender,_)=>GlassAppearance.ApplyWindow((global::System.Windows.Window)sender)));
        global::System.Windows.SystemParameters.StaticPropertyChanged += (_,e)=>
        {
            if(e.PropertyName==nameof(global::System.Windows.SystemParameters.HighContrast))
                global::System.Windows.Application.Current.Dispatcher.Invoke(ApplyCurrentTheme);
        };

        SystemEvents.UserPreferenceChanged += (s, e) =>
        {
            if (e.Category == UserPreferenceCategory.General)
            {
                ApplyCurrentTheme();
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

    public static void ApplyTheme(string themePreference)
    {
        if(global::System.Windows.Application.Current is { } app && !app.Dispatcher.CheckAccess())
        {app.Dispatcher.Invoke(()=>ApplyTheme(themePreference));return;}
        bool isDark = themePreference switch
        {
            "Light" => false,
            "Dark" => true,
            "液态玻璃" or "液态玻璃（低透明）" => false,
            "普通版" => true,
            _ => IsWindowsDarkTheme()
        };

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
