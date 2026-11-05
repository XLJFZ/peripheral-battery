using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using BluetoothBatteryMonitor.App.Helpers;
using BluetoothBatteryMonitor.App.Models;
using BluetoothBatteryMonitor.App.Services.System;
using BluetoothBatteryMonitor.App.Services.Localization;
using BluetoothBatteryMonitor.App.ViewModels;
using BluetoothBatteryMonitor.App.Views;

namespace BluetoothBatteryMonitor.App.Services.Tray;

public class TrayIconManager : IDisposable
{
    private const int MaxPerDeviceIcons = 4;
    private const int PulseIntervalMs = 110;

    private sealed class DeviceTrayIcon : IDisposable
    {
        public TaskbarIcon TrayIcon { get; }
        public Icon? GdiIcon { get; set; }
        public ContextMenu Menu { get; }

        public DeviceTrayIcon(TaskbarIcon trayIcon, ContextMenu menu)
        {
            TrayIcon = trayIcon;
            Menu = menu;
        }

        public void Dispose()
        {
            TrayIcon.Dispose();
            GdiIcon?.Dispose();
        }
    }

    private readonly MainViewModel _mainViewModel;
    private readonly FlyoutWindow _flyoutWindow;
    private TaskbarIcon? _taskbarIcon;
    private ContextMenu? _contextMenu;
    private Icon? _currentIcon;
    private DateTime _lastContextMenuClosedTime = DateTime.MinValue;
    private bool _flyoutWasOpenOnMouseDown;
    private IntPtr _contextMenuHwnd = IntPtr.Zero;
    private readonly Dictionary<string, DeviceTrayIcon> _deviceIcons = new(StringComparer.OrdinalIgnoreCase);
    private readonly global::System.Windows.Threading.DispatcherTimer? _pulseTimer;
    private int _pulseTick;

    public TrayIconManager(MainViewModel mainViewModel, FlyoutWindow flyoutWindow)
    {
        _mainViewModel = mainViewModel;
        _flyoutWindow = flyoutWindow;

        _mainViewModel.BatteryStateChanged += OnBatteryStateChanged;
        ThemeManager.ThemeChanged += OnThemeChanged;
        LocalizationService.LanguageChanged += OnLanguageChanged;

        _pulseTimer = new global::System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(PulseIntervalMs)
        };
        _pulseTimer.Tick += (_, _) =>
        {
            _pulseTick++;
            UpdateTrayIcon();
        };
    }

    private void OnLanguageChanged(object? sender, string lang)
    {
        Application.Current?.Dispatcher?.Invoke(() =>
        {
            _contextMenu = CreateContextMenu();
            if (_taskbarIcon != null)
            {
                _taskbarIcon.ContextMenu = _contextMenu;
            }
            // Per-device icons embed their own context menus; rebuild from scratch.
            DisposeDeviceIcons();
            UpdateTrayIcon();
        });
    }

    public void Initialize()
    {
        _contextMenu = CreateContextMenu();

        _taskbarIcon = new TaskbarIcon
        {
            ToolTipText = "Bluetooth Pil Monitörü",
            ContextMenu = _contextMenu,
            NoLeftClickDelay = true,
            MenuActivation = PopupActivationMode.RightClick
        };

        // Close active Flyout immediately before displaying context menu
        _taskbarIcon.TrayRightMouseDown += (s, e) =>
        {
            _flyoutWindow.HideImmediately();
        };

        _taskbarIcon.PreviewTrayContextMenuOpen += (s, e) =>
        {
            _flyoutWindow.HideImmediately();
        };

        // Left mouse button down: Record whether the flyout was already open when clicked
        _taskbarIcon.TrayLeftMouseDown += (s, e) =>
        {
            _flyoutWasOpenOnMouseDown = _flyoutWindow.IsFlyoutOpen;
            if (_flyoutWasOpenOnMouseDown)
            {
                // User clicked to dismiss the currently open window: Hide immediately
                _flyoutWindow.HideImmediately();
            }
        };

        // Left mouse button up: Toggle flyout window (MouseUp avoids Windows shell focus race condition)
        _taskbarIcon.TrayLeftMouseUp += (s, e) =>
        {
            // If ContextMenu is open or was just closed, dismiss menu and display flyout
            if (_contextMenu != null && (_contextMenu.IsOpen || (DateTime.UtcNow - _lastContextMenuClosedTime).TotalMilliseconds < 250))
            {
                _contextMenu.IsOpen = false;
                _flyoutWindow.ShowFlyout();
                _flyoutWasOpenOnMouseDown = false;
                return;
            }

            // If Flyout was open when clicked, the intent was dismissal (do not reopen)
            if (_flyoutWasOpenOnMouseDown)
            {
                _flyoutWasOpenOnMouseDown = false;
                return;
            }

            // Flyout was closed, show it
            _flyoutWindow.ShowFlyout();
        };

        // Double click: Open flyout window
        _taskbarIcon.TrayMouseDoubleClick += (s, e) =>
        {
            _flyoutWindow.ShowFlyout();
        };

        UpdateTrayIcon();
        try
        {
            _taskbarIcon.ForceCreate();
        }
        catch (Exception ex)
        {
            global::System.Diagnostics.Debug.WriteLine($"[TrayIconManager] Failed to force create tray icon on initial launch: {ex.Message}");
        }
    }

    private ContextMenu CreateContextMenu()
    {
        var menu = new ContextMenu();

        menu.Opened += (s, e) =>
        {
            // Dismiss flyout immediately if open
            _flyoutWindow.HideImmediately();

            // Win32 NotifyIcon standard: Set foreground window to ensure context menu dismisses when clicking outside
            if (PresentationSource.FromVisual(menu) is HwndSource hwndSource)
            {
                _contextMenuHwnd = hwndSource.Handle;
                NativeMethods.SetForegroundWindow(_contextMenuHwnd);
            }
        };

        menu.Closed += (s, e) =>
        {
            _lastContextMenuClosedTime = DateTime.UtcNow;
            if (_contextMenuHwnd != IntPtr.Zero)
            {
                NativeMethods.PostMessage(_contextMenuHwnd, NativeMethods.WM_NULL, IntPtr.Zero, IntPtr.Zero);
                _contextMenuHwnd = IntPtr.Zero;
            }
        };

        var openItem = new MenuItem { Header = LocalizationService.GetString("Tray_OpenHide") };
        openItem.Click += (s, e) => _flyoutWindow.ToggleFlyout();
        menu.Items.Add(openItem);

        var refreshItem = new MenuItem { Header = LocalizationService.GetString("Tray_Refresh") };
        refreshItem.Click += async (s, e) => await _mainViewModel.RefreshDevicesAsync();
        menu.Items.Add(refreshItem);

        menu.Items.Add(new Separator());

        var startupItem = new MenuItem
        {
            Header = LocalizationService.GetString("Tray_LaunchAtStartup"),
            IsCheckable = true,
            IsChecked = StartupManager.IsStartupEnabled()
        };
        // Sync actual startup state each time menu opens
        menu.Opened += (s, e) =>
        {
            startupItem.IsChecked = StartupManager.IsStartupEnabled();
        };
        startupItem.Click += (s, e) =>
        {
            // WPF MenuItem IsCheckable=true automatically toggles IsChecked upon click
            bool newState = startupItem.IsChecked;
            StartupManager.SetStartup(newState);
            var settings = AppSettings.Load();
            settings.LaunchAtStartup = newState;
            settings.Save();
        };
        menu.Items.Add(startupItem);

        var settingsItem = new MenuItem { Header = LocalizationService.GetString("Tray_Settings") };
        settingsItem.Click += (s, e) => MainViewModel.OpenSettingsWindow();
        menu.Items.Add(settingsItem);

        var aboutItem = new MenuItem { Header = LocalizationService.GetString("Tray_About") };
        aboutItem.Click += (s, e) => MainViewModel.OpenAboutWindow();
        menu.Items.Add(aboutItem);

        menu.Items.Add(new Separator());

        var exitItem = new MenuItem { Header = LocalizationService.GetString("Tray_Exit") };
        exitItem.Click += (s, e) => MainViewModel.ExitApp();
        menu.Items.Add(exitItem);

        return menu;
    }

    private void OnBatteryStateChanged(object? sender, EventArgs e)
    {
        Application.Current?.Dispatcher?.Invoke(UpdateTrayIcon);
    }

    private void OnThemeChanged(object? sender, bool isDark)
    {
        Application.Current?.Dispatcher?.Invoke(UpdateTrayIcon);
    }

    public void UpdateTrayIcon()
    {
        if (_taskbarIcon == null) return;

        bool isDark = ThemeManager.IsWindowsDarkTheme();
        int? lowest = _mainViewModel.LowestBatteryLevel;
        bool isCharging = _mainViewModel.IsLowestCharging;

        var settings=AppSettings.Load();
        // Lowest batteries remain visible when more than three devices are connected.
        var ringDevices=_mainViewModel.Devices.Where(d=>d.IsConnected&&d.HasBattery)
            .OrderBy(d=>settings.DeviceRank(d.Id)).ThenBy(d=>d.BatteryProgress).ThenBy(d=>d.Id,StringComparer.OrdinalIgnoreCase).Take(3).ToList();
        // While a visible device charges, its ring breathes (same rhythm as per-device icons).
        int mainPulseTick=settings.TrayChargingPulse&&ringDevices.Any(d=>d.IsCharging)?_pulseTick:-1;
        var newIcon = TrayIconGenerator.GenerateMultiple(
            ringDevices.Select(d=>(d.BatteryProgress,d.IsCharging)).ToList(),isDark,settings,mainPulseTick);

        _taskbarIcon.Icon = newIcon;

        _currentIcon?.Dispose();
        _currentIcon = newIcon;

        if (!_taskbarIcon.IsCreated)
        {
            try
            {
                _taskbarIcon.ForceCreate();
            }
            catch { }
        }

        // Update tooltip text: one short line per ring, no verbose ring description.
        if (_mainViewModel.Devices.Count > 0)
        {
            int withBattery = _mainViewModel.Devices.Count(d => d.IsConnected && d.HasBattery);
            var summary = BuildTooltipBody(ringDevices.Select(d => (d.Name, d.BatteryText)).ToList(), withBattery);
            if (ringDevices.Count == 0) summary = string.Join("\n", _mainViewModel.Devices.Take(4).Select(d => $"{ShortDeviceName(d.Name)} {d.BatteryText}"));
            _taskbarIcon.ToolTipText = $"{LocalizationService.GetString("Tray_TooltipTitle")}\n{summary}";
        }
        else
        {
            _taskbarIcon.ToolTipText = LocalizationService.GetString("Tray_TooltipNoDevices");
        }

        UpdatePerDeviceIcons(settings, isDark);
        UpdatePulseTimer(settings);
    }

    /// <summary>
    /// Devices that get their own tray icon: connected with a battery reading, lowest battery
    /// first, capped so the notification area cannot flood. Static + pure for unit tests.
    /// </summary>
    public static IReadOnlyList<DeviceItemViewModel> SelectPerDeviceIcons(IEnumerable<DeviceItemViewModel> devices, int max = MaxPerDeviceIcons)
        => devices
            .Where(d => d.IsConnected && d.HasBattery)
            .OrderBy(d => d.BatteryProgress)
            .ThenBy(d => d.Id, StringComparer.OrdinalIgnoreCase)
            .Take(max)
            .ToList();

    private void UpdatePerDeviceIcons(AppSettings settings, bool isDark)
    {
        if (_taskbarIcon == null) return;

        var desired = settings.TrayPerDeviceIcons
            ? SelectPerDeviceIcons(_mainViewModel.Devices)
            : (IReadOnlyList<DeviceItemViewModel>)Array.Empty<DeviceItemViewModel>();

        // Remove icons for devices that vanished, disconnected or fell out of the top-N.
        var desiredIds = desired.Select(d => d.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var stale in _deviceIcons.Where(kvp => !desiredIds.Contains(kvp.Key)).Select(kvp => kvp.Key).ToList())
        {
            if (_deviceIcons.Remove(stale, out var removed)) removed.Dispose();
        }

        int pulseTick = settings.TrayChargingPulse ? _pulseTick : -1;

        foreach (var device in desired)
        {
            bool charging = device.IsCharging;
            if (!_deviceIcons.TryGetValue(device.Id, out var entry))
            {
                var trayIcon = new TaskbarIcon
                {
                    ToolTipText = $"{ShortDeviceName(device.Name)} {device.BatteryText}",
                    NoLeftClickDelay = true,
                    MenuActivation = PopupActivationMode.RightClick,
                    ContextMenu = CreateContextMenu()
                };
                trayIcon.TrayLeftMouseUp += (s, e) => _flyoutWindow.ShowFlyout();
                trayIcon.TrayMouseDoubleClick += (s, e) => _flyoutWindow.ShowFlyout();
                entry = new DeviceTrayIcon(trayIcon, (ContextMenu)trayIcon.ContextMenu);
                _deviceIcons[device.Id] = entry;
            }

            var newIcon = TrayIconGenerator.GeneratePerDevice(device.BatteryProgress, charging, device.Model.Type, isDark, settings, charging ? pulseTick : -1);
            entry.TrayIcon.Icon = newIcon;
            entry.GdiIcon?.Dispose();
            entry.GdiIcon = newIcon;
            entry.TrayIcon.ToolTipText = $"{ShortDeviceName(device.Name)} {device.BatteryText}";

            if (!entry.TrayIcon.IsCreated)
            {
                try { entry.TrayIcon.ForceCreate(); } catch { }
            }
        }
    }

    private void UpdatePulseTimer(AppSettings settings)
    {
        if (_pulseTimer == null) return;

        bool anyCharging = settings.TrayChargingPulse &&
            (settings.TrayPerDeviceIcons
                ? SelectPerDeviceIcons(_mainViewModel.Devices).Any(d => d.IsCharging)
                : _mainViewModel.Devices.Any(d => d.IsConnected && d.HasBattery && d.IsCharging));

        if (anyCharging && !_pulseTimer.IsEnabled)
        {
            _pulseTick = 0;
            _pulseTimer.Start();
        }
        else if (!anyCharging && _pulseTimer.IsEnabled)
        {
            _pulseTimer.Stop();
            // Repaint once without the pulse so the ring does not freeze at a faded alpha.
            _pulseTick = 0;
            if (settings.TrayPerDeviceIcons) UpdatePerDeviceIcons(settings, ThemeManager.IsWindowsDarkTheme());
        }
    }

    private void DisposeDeviceIcons()
    {
        foreach (var entry in _deviceIcons.Values) entry.Dispose();
        _deviceIcons.Clear();
    }

    /// <summary>One short line per tray ring; extra connected devices collapse into one hint.</summary>
    public static string BuildTooltipBody(IReadOnlyList<(string Name, string Battery)> rings, int connectedWithBattery)
    {
        var summary = string.Join("\n", rings.Select((r, i) => $"{RingMarker(i)} {ShortDeviceName(r.Name)} {r.Battery}"));
        if (connectedWithBattery > 3) summary += "\n" + LocalizationService.GetString("Tray_MoreDevices", connectedWithBattery - 3);
        return summary;
    }

    private static readonly string[] RingMarkers = { "①", "②", "③" };

    private static string RingMarker(int index)
        => index >= 0 && index < RingMarkers.Length ? RingMarkers[index] : $"{index + 1}.";

    /// <summary>
    /// Keeps the tooltip narrow: drops a trailing model suffix such as "(Wireless)" and
    /// truncates unusually long names.
    /// </summary>
    public static string ShortDeviceName(string? name)
    {
        const int maxLength = 26;
        if (string.IsNullOrWhiteSpace(name)) return "?";
        string trimmed = global::System.Text.RegularExpressions.Regex
            .Replace(name.Trim(), @"\s*[\(（][^\)）]*[\)）]\s*$", string.Empty)
            .Trim();
        if (trimmed.Length == 0) trimmed = name.Trim();
        return trimmed.Length > maxLength ? trimmed[..(maxLength - 1)].TrimEnd() + "…" : trimmed;
    }

    public void Dispose()
    {
        _mainViewModel.BatteryStateChanged -= OnBatteryStateChanged;
        ThemeManager.ThemeChanged -= OnThemeChanged;
        LocalizationService.LanguageChanged -= OnLanguageChanged;

        if (_pulseTimer != null)
        {
            _pulseTimer.Stop();
        }
        DisposeDeviceIcons();

        _taskbarIcon?.Dispose();
        _currentIcon?.Dispose();
    }
}
