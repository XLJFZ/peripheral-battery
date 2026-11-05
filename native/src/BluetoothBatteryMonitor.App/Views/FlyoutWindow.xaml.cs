using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using BluetoothBatteryMonitor.App.Helpers;
using BluetoothBatteryMonitor.App.Services.System;
using BluetoothBatteryMonitor.App.ViewModels;
using Wpf.Ui.Controls;

namespace BluetoothBatteryMonitor.App.Views;

public partial class FlyoutWindow : FluentWindow
{
    internal RealtimeGlassCapture RealtimeGlass {get;}
    private Point _dragStart;
    private void GlassCardEnter(object sender,System.Windows.Input.MouseEventArgs e)=>AnimateGlassCard(sender,true);
    private void GlassCardLeave(object sender,System.Windows.Input.MouseEventArgs e)=>AnimateGlassCard(sender,false);
    private static void AnimateGlassCard(object sender,bool hover)
    {
        if(sender is not System.Windows.Controls.Border card)return;
        card.RenderTransform=System.Windows.Media.Transform.Identity;
        // Hover tint comes from the active palette, so dark glass stays dark on hover.
        card.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty,
            hover && GlassAppearance.Enabled && !SystemParameters.HighContrast ? "PeripheralCardHoverBrush" : "PeripheralCardBrush");
    }    private void DeviceDragStart(object sender, System.Windows.Input.MouseButtonEventArgs e) => _dragStart=e.GetPosition(this);
    private void DeviceDragMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if(e.LeftButton!=System.Windows.Input.MouseButtonState.Pressed || sender is not FrameworkElement card || card.DataContext is not DeviceItemViewModel item) return;
        var delta=e.GetPosition(this)-_dragStart;
        if(Math.Abs(delta.X)<SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y)<SystemParameters.MinimumVerticalDragDistance) return;
        // Interactive controls retain their normal click/slider behavior.
        var node=e.OriginalSource as DependencyObject;
        while(node!=null && node!=card)
        {
            if(node is System.Windows.Controls.Primitives.ButtonBase or System.Windows.Controls.Primitives.RangeBase or System.Windows.Controls.TextBox) return;
            node=node is FrameworkContentElement content?content.Parent:System.Windows.Media.VisualTreeHelper.GetParent(node);
        }
        DragDrop.DoDragDrop(card,new DataObject("PeripheralDeviceId",item.Id),DragDropEffects.Move);
        ResetIdle();
    }
    private void DeviceDrop(object sender, DragEventArgs e)
    {
        if(DataContext is not MainViewModel vm || sender is not FrameworkElement card || card.DataContext is not DeviceItemViewModel target || e.Data.GetData("PeripheralDeviceId") is not string id) return;
        var source=vm.Devices.FirstOrDefault(d=>d.Id==id);
        if(source==null || source==target) return;
        vm.Devices.Move(vm.Devices.IndexOf(source),vm.Devices.IndexOf(target));
        var settings=Models.AppSettings.Load();
        settings.DeviceOrder=vm.Devices.Select(d=>d.Id).Concat(settings.DeviceOrder ?? new()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        settings.Save(); e.Handled=true;
    }
    private void OnRenameDeviceClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not DeviceItemViewModel item) return;
        var dialog = new RenameDeviceWindow(item.Name) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            item.ApplyCustomName(string.IsNullOrWhiteSpace(dialog.DeviceName) ? null : dialog.DeviceName);
        }
    }

    private void OnHideDeviceClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not DeviceItemViewModel item) return;
        if (DataContext is MainViewModel vm)
        {
            vm.HideDevice(item);
        }
    }

    private bool _isOpening;
    private bool _isHiding;
    private int _animationGeneration;
    private bool _hasRendered;
    private bool _isChildContextMenuOpen;
    private DateTime _lastDeactivatedTime = DateTime.MinValue;

    public bool IsFlyoutOpen => Visibility == Visibility.Visible && !_isHiding;
    public DateTime LastDeactivatedTime => _lastDeactivatedTime;
    public bool DisableAutoClose { get; set; }
    private readonly System.Windows.Threading.DispatcherTimer _idleTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private DateTime _lastInteraction = DateTime.UtcNow;
    /// <summary>Inactivity budget for the open flyout, refreshed on every show. 0 disables auto-hide.</summary>
    private int _autoHideSeconds = 5;
    private Models.AppSettings? _cachedSettings;
    private DateTime _settingsStampUtc = DateTime.MinValue;

    /// <summary>
    /// Reads FlyoutAutoHideSeconds through a stamp-checked cache. ShowFlyout runs on the UI
    /// thread on every tray click, so it must not pay a disk read plus JSON deserialize each
    /// time; the cache re-loads only when settings.json's last-write time changed.
    /// </summary>
    private int ReadAutoHideSeconds()
    {
        try
        {
            DateTime stamp = Models.AppSettings.GetSettingsStampUtc();
            if (_cachedSettings == null || stamp != _settingsStampUtc)
            {
                _cachedSettings = Models.AppSettings.Load();
                _settingsStampUtc = stamp;
            }
            return Models.AppSettings.NormalizeAutoHide(_cachedSettings.FlyoutAutoHideSeconds);
        }
        catch
        {
            return 5;
        }
    }

    private void ResetIdle(object? sender = null, EventArgs? e = null) => _lastInteraction = DateTime.UtcNow;

    public FlyoutWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        RealtimeGlass=new RealtimeGlassCapture(this);
        DataContext = viewModel;
        _autoHideSeconds = ReadAutoHideSeconds();
        PreviewMouseMove += (s,e) => ResetIdle();
        PreviewMouseDown += (s,e) => ResetIdle();
        PreviewMouseWheel += (s,e) => ResetIdle();
        PreviewKeyDown += (s,e) => ResetIdle();
        _idleTimer.Tick += (s,e) => CheckIdle();
        _idleTimer.Start();
        Closed += (s,e) => _idleTimer.Stop();

        try
        {
            // An ICO decoded through BitmapImage can select its 16px entry. Supply a
            // full-resolution source so WPF can generate both taskbar and titlebar HICONs.
            Icon = System.Windows.Media.Imaging.BitmapFrame.Create(
                new Uri("pack://application:,,,/BluetoothBatteryMonitor.App;component/Assets/app-taskbar.png", UriKind.Absolute),
                System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat,
                System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
        }
        catch { }

        Loaded += OnLoaded;
        ContentRendered += OnContentRendered;
        SizeChanged += OnSizeChanged;

        // SizeToContent alone leaves the panel at its first measured height: with a single
        // device the `*` row kept a whole extra card of empty space. Fit explicitly whenever
        // the device list changes.
        ((System.Collections.Specialized.INotifyCollectionChanged)DeviceList.Items).CollectionChanged += (s, e) => ScheduleFit();

        // Track opening and closing of child context menus (e.g. device card threshold menu)
        AddHandler(System.Windows.Controls.ContextMenuService.ContextMenuOpeningEvent,
            new System.Windows.Controls.ContextMenuEventHandler((s, e) => _isChildContextMenuOpen = true));
        AddHandler(System.Windows.Controls.ContextMenuService.ContextMenuClosingEvent,
            new System.Windows.Controls.ContextMenuEventHandler((s, e) =>
            {
                _isChildContextMenuOpen = false;
                // When child menu closes, hide flyout if it lost focus (clicked outside)
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() =>
                {
                    if (!_isChildContextMenuOpen && !IsActive && !IsKeyboardFocusWithin)
                    {
                        ResetIdle();
                    }
                }));
            }));

        // Support instant dismissal with Escape key
        PreviewKeyDown += (s, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                HideFlyout();
                e.Handled = true;
            }
        };

        // Pre-calculate window position before first render (avoid CW_USEDEFAULT)
        RepositionFlyout();
    }

    private void CheckIdle()
    {
        // An opening request can be cancelled without a corresponding closing event.
        if(_isChildContextMenuOpen&&!HasOpenContextMenu(this))_isChildContextMenuOpen=false;
        if(IsVisible&&!DisableAutoClose&&!_isChildContextMenuOpen&&_autoHideSeconds>0&&
            DateTime.UtcNow-_lastInteraction>=TimeSpan.FromSeconds(_autoHideSeconds))HideFlyout();
    }
    private static bool HasOpenContextMenu(DependencyObject root)
    {
        if(root is FrameworkElement element&&element.ContextMenu?.IsOpen==true)return true;
        int count=global::System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for(int i=0;i<count;i++)
            if(HasOpenContextMenu(global::System.Windows.Media.VisualTreeHelper.GetChild(root,i)))return true;
        return false;
    }
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyModernWindowStyling();
        FitToContent();
    }

    private bool _fitQueued;

    private void ScheduleFit()
    {
        if (_fitQueued) return;
        _fitQueued = true;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
        {
            _fitQueued = false;
            FitToContent();
        }));
    }

    /// <summary>
    /// Sets the window height to the measured content height (capped by MaxHeight) instead of
    /// relying on SizeToContent, which keeps the height it measured first and leaves the
    /// remaining space to the `*` device row.
    /// </summary>
    /// <summary>Applies a panel size and re-fits the height to the new width.</summary>
    public void ApplyDensity(double width)
    {
        Width = width;
        FitToContent(width);
    }

    public double FitToContent(double? widthOverride = null)
    {
        if (Content is not FrameworkElement content) return 0;
        double width = widthOverride ?? (ActualWidth > 0 ? ActualWidth : (Width > 0 ? Width : 416));
        content.Measure(new Size(width, double.PositiveInfinity));
        double desired = Math.Ceiling(content.DesiredSize.Height);
        if (desired <= 0 || double.IsInfinity(desired)) return 0;

        double target = Math.Min(desired, MaxHeight);
        if (SizeToContent != SizeToContent.Manual) SizeToContent = SizeToContent.Manual;
        if (Math.Abs(Height - target) > 0.5) Height = target;
        UpdateLayout();
        RepositionFlyout();
        return target;
    }

    private void OnContentRendered(object? sender, EventArgs e)
    {
        _hasRendered = true;
        if (IsVisible)
        {
            RepositionFlyout();
        }
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (IsVisible && Visibility == Visibility.Visible)
        {
            RepositionFlyout();
        }
    }

    private void ApplyModernWindowStyling()
    {
        var helper = new WindowInteropHelper(this);
        if (helper.Handle == IntPtr.Zero) return;

        // Enforce Windows 11 rounded corners via DWM
        int preference = (int)NativeMethods.DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_ROUND;
        NativeMethods.DwmSetWindowAttribute(
            helper.Handle,
            NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE,
            ref preference,
            sizeof(int));
    }

    /// <summary>
    /// Repositions the flyout anchored cleanly adjacent to the taskbar.
    /// </summary>
    public void RepositionFlyout()
    {
        double w = ActualWidth > 0 ? ActualWidth : (Width > 0 ? Width : 416);
        double h = ActualHeight;

        if (h <= 0)
        {
            if (Content is UIElement content)
            {
                try
                {
                    content.Measure(new Size(w, double.PositiveInfinity));
                    h = content.DesiredSize.Height > 0 ? content.DesiredSize.Height : 320;
                }
                catch
                {
                    h = 320;
                }
            }
            else
            {
                h = 320;
            }
        }

        var pos = TaskbarPositionHelper.CalculateFlyoutPosition(w, h, margin: 12);
        Left = pos.X;
        Top = pos.Y;
    }

    public void ShowFlyout()
    {
        // Re-read on every open so a change in Settings applies without a restart; the
        // stamp-checked cache keeps this off the disk when settings.json did not change.
        _autoHideSeconds = ReadAutoHideSeconds();
        ResetIdle();
        if (IsVisible && Opacity > 0.95 && !_isOpening && !_isHiding)
        {
            BringToFront();
            return;
        }

        _isHiding = false;
        _isOpening = true;
        int currentGen = ++_animationGeneration;
        BeginAnimation(OpacityProperty, null);

        // Pre-calculate window coordinates
        RepositionFlyout();

        Opacity = 0;
        if (!IsVisible)
        {
            Show();
        }
        else
        {
            Visibility = Visibility.Visible;
        }

        DeviceScrollViewer?.ScrollToTop();
        (DataContext as MainViewModel)?.RefreshAudioVolumes();
        (DataContext as MainViewModel)?.RefreshOnOpenIfEmpty();

        if (!_hasRendered || ActualHeight <= 0)
        {
            // On initial show, position and display after layout finishes measuring
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
            {
                FitToContent();
                RepositionFlyout();
                Activate();
                BringToFront();
                Focus();
                AnimateFadeIn(currentGen);
            }));
        }
        else
        {
            FitToContent();
            UpdateLayout();
            RepositionFlyout();
            Activate();
            BringToFront();
            Focus();
            AnimateFadeIn(currentGen);
        }
    }

    private void BringToFront()
    {
        var helper = new WindowInteropHelper(this);
        if (helper.Handle != IntPtr.Zero)
        {
            NativeMethods.SetWindowPos(helper.Handle, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_SHOWWINDOW);
            NativeMethods.SetForegroundWindow(helper.Handle);
        }
    }

    private void AnimateFadeIn(int currentGen)
    {
        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        fadeIn.Completed += (s, e) =>
        {
            if (_animationGeneration == currentGen)
            {
                _isOpening = false;
            }
        };
        BeginAnimation(OpacityProperty, fadeIn);
    }

    public void HideFlyout()
    {
        if (Visibility != Visibility.Visible || _isHiding) return;

        _isHiding = true;
        _isOpening = false;
        int currentGen = ++_animationGeneration;
        BeginAnimation(OpacityProperty, null);

        var fadeOut = new DoubleAnimation(Opacity > 0 ? Opacity : 1.0, 0, TimeSpan.FromMilliseconds(100))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };

        fadeOut.Completed += (s, e) =>
        {
            if (_animationGeneration == currentGen)
            {
                Hide();
                Opacity = 1;
                _isHiding = false;
            }
        };

        BeginAnimation(OpacityProperty, fadeOut);
    }

    /// <summary>
    /// Hides the flyout instantly without animation (used when context menu opens or tray exit clicked).
    /// </summary>
    public void HideImmediately()
    {
        _isOpening = false;
        _isHiding = false;
        ++_animationGeneration;
        BeginAnimation(OpacityProperty, null);
        Hide();
        Opacity = 1;
    }

    public void ToggleFlyout()
    {
        if (IsFlyoutOpen)
        {
            HideFlyout();
        }
        else
        {
            ShowFlyout();
        }
    }

    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
        if (DisableAutoClose) return;

        _lastDeactivatedTime = DateTime.UtcNow;

        // Do not dismiss flyout if a child context menu is currently active
        if (_isChildContextMenuOpen)
        {
            return;
        }

        // Keep visible after focus changes; the inactivity timer handles hiding.
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        HideFlyout();
    }
}
