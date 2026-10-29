using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
using ToolDock.Interop;
using ToolDock.Models;
using ToolDock.Services;
using WpfDragEventArgs = System.Windows.DragEventArgs;
using WpfDataFormats = System.Windows.DataFormats;
using WpfDataObject = System.Windows.DataObject;
using WpfDragDropEffects = System.Windows.DragDropEffects;
using WpfMessageBox = System.Windows.MessageBox;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfPoint = System.Windows.Point;

namespace ToolDock;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private const double RevealPixels = 4;
    private const double FirstIconInsetFromDock = 11;
    private const double IconCrossAxisFactor = 2.00;
    private const double MinIconOverflowPadding = 26;
    private const double PointerTrackerIdleIntervalMs = 33;
    private const double PointerTrackerHoverIntervalMs = 16;
    private readonly ConfigService _configService;
    private readonly string _instanceLabel;
    private readonly DispatcherTimer _autoHideTimer;
    private readonly DispatcherTimer _autoShowTimer;
    private readonly DispatcherTimer _pointerTrackerTimer;
    private readonly DispatcherTimer _saveDebounceTimer;
    private readonly EventHandler _displaySettingsChangedHandler;
    private DockItemViewModel? _draggedItem;
    private WpfPoint _dragStartPoint;
    private bool _isDraggingInternal;
    private bool _isHiddenByAutohide;
    /// <summary>
    /// Skips recomputing the width on every Clear()+Add step while syncing <see cref="DockConfig.Items"/>;
    /// an intermediate Clear leaves the collection empty and the minimum became that of 4 icons (~9.1%), which flickered with auto-fit.
    /// </summary>
    private bool _suppressConfigItemsDockWidthSync;

    private const int HotkeyShowHideId = 0xB001;
    private const int HotkeyShowDockId = 0xB002;
    private const int HotkeyDisableDockId = 0xB003;
    private MonitorInfo _activeMonitor = null!;
    private HwndSource? _hwndSource;
    private bool _appBarRegistered;
    private bool _hotkeyHidden;
    private bool _hiddenByFullscreen;
    private bool _isUpdatingDockLayout;
    private bool _isDockLayoutUpdateQueued;
    private readonly Microsoft.Win32.UserPreferenceChangedEventHandler _userPreferenceChangedHandler;

    private DockEdge _reservedEdge;
    private User32.RECT _requestedAppBarRect;
    private int _requestedAppBarEdge = -1;
    private bool _appBarPositionApplied;

    /// <summary>Rectangle (physical px) the shell granted to the dock AppBar after ABM_SETPOS.</summary>
    private User32.RECT _grantedAppBarRect;
    private bool _hasGrantedAppBarRect;
    private DateTime _lastAppBarApplyUtc = DateTime.MinValue;

    /// <summary>Open dock tooltips/menus must stay above, so they pause Z-order updates.</summary>
    private int _openPopupCount;
    private int _zOrderTicksSinceReassert;
    private WpfPoint _lastTrackedPointerWindow;
    private bool _fisheyeNeedsReset = true;
    private bool _pointerTrackerInHoverZone;

    public event PropertyChangedEventHandler? PropertyChanged;

    public DockConfig Config { get; }

    /// <summary>
    /// Work area used to place the dock, subtracting the space the dock itself reserved:
    /// without that compensation, the reservation pushes the dock into the gap it just created.
    /// </summary>
    private Rect ActiveWorkArea => CompensateOwnReservation(_activeMonitor.WorkAreaDip);

    private Rect ActiveBounds => _activeMonitor.BoundsDip;

    private void RefreshActiveMonitor()
    {
        _activeMonitor = DisplayService.WithFreshWorkArea(DisplayService.GetMonitorForConfig(Config));
    }

    /// <summary>
    /// Puts the anchored work-area edge back into the gap the dock itself reserved, using the
    /// rectangle granted by the shell. The dock returns to the edge it had before reserving
    /// (which may not be the physical edge: next to the taskbar the gap starts after the
    /// taskbar) and only the axis of the anchored edge is touched.
    /// </summary>
    private Rect CompensateOwnReservation(Rect workArea)
    {
        if (!_appBarRegistered || !_hasGrantedAppBarRect)
        {
            return workArea;
        }

        var scale = DisplayService.GlobalDipScale();
        if (scale <= 0)
        {
            scale = 1.0;
        }

        var granted = new Rect(
            _grantedAppBarRect.Left / scale,
            _grantedAppBarRect.Top / scale,
            _grantedAppBarRect.Width / scale,
            _grantedAppBarRect.Height / scale);

        // It can only grow toward the edge: if the shell did not reserve space, nothing moves.
        var compensated = _reservedEdge switch
        {
            DockEdge.Top => WithTop(workArea, Math.Min(workArea.Top, granted.Top)),
            DockEdge.Bottom => WithBottom(workArea, Math.Max(workArea.Bottom, granted.Bottom)),
            DockEdge.Left => WithLeft(workArea, Math.Min(workArea.Left, granted.Left)),
            DockEdge.Right => WithRight(workArea, Math.Max(workArea.Right, granted.Right)),
            _ => workArea
        };

        compensated.Intersect(_activeMonitor.BoundsDip);
        return compensated.IsEmpty ? workArea : compensated;
    }

    private static Rect WithTop(Rect rect, double top) => new(rect.Left, top, rect.Width, rect.Bottom - top);

    private static Rect WithBottom(Rect rect, double bottom) => new(rect.Left, rect.Top, rect.Width, bottom - rect.Top);

    private static Rect WithLeft(Rect rect, double left) => new(left, rect.Top, rect.Right - left, rect.Height);

    private static Rect WithRight(Rect rect, double right) => new(rect.Left, rect.Top, right - rect.Left, rect.Height);

    public ObservableCollection<DockItemViewModel> Items { get; } = [];

    public Thickness ItemMargin => new(Config.IconSpacing / 2.0);

    public CornerRadius DockCornerRadius
    {
        get
        {
            if (!Config.RoundedCorners)
            {
                return new CornerRadius(0);
            }

            // The radius cannot exceed half of the frame's shorter side, or the corners overlap.
            var maxRadius = Math.Max(0, Math.Min(DockFrameWidth, DockFrameHeight) / 2.0);
            var r = DockMetrics.SnapLogicalToWholePhysical(Math.Min(Config.CornerRadius, maxRadius));
            return new CornerRadius(
                Config.RoundedCornerTopLeft ? r : 0,
                Config.RoundedCornerTopRight ? r : 0,
                Config.RoundedCornerBottomRight ? r : 0,
                Config.RoundedCornerBottomLeft ? r : 0);
        }
    }

    public System.Windows.Media.Brush DockBackgroundBrush =>
        FrozenBrush(ApplyOpacity(ParseSolidColor(), Config.DockOpacity));

    public System.Windows.Media.Brush DockBorderBrush =>
        Config.ShowOutline
            ? FrozenBrush(ApplyOpacity(ParseOutlineColor(), Config.OutlineOpacity))
            : FrozenBrush(System.Windows.Media.Colors.Transparent);

    public Thickness DockBorderThickness
    {
        get
        {
            if (!Config.ShowOutline)
            {
                return new Thickness(0);
            }

            var t = Config.OutlineThickness;
            return new Thickness(
                Config.OutlineEdgeLeft ? t : 0,
                Config.OutlineEdgeTop ? t : 0,
                Config.OutlineEdgeRight ? t : 0,
                Config.OutlineEdgeBottom ? t : 0);
        }
    }

    public System.Windows.Media.Brush DockToolTipBackgroundBrush => Config.Theme == AppTheme.Dark
        ? FrozenBrush(System.Windows.Media.Color.FromRgb(36, 36, 36))
        : FrozenBrush(System.Windows.Media.Color.FromRgb(246, 246, 246));

    public System.Windows.Media.Brush DockToolTipForegroundBrush => Config.Theme == AppTheme.Dark
        ? FrozenBrush(System.Windows.Media.Color.FromRgb(235, 235, 235))
        : FrozenBrush(System.Windows.Media.Color.FromRgb(20, 20, 20));

    public System.Windows.Media.Brush DockToolTipBorderBrush => Config.Theme == AppTheme.Dark
        ? FrozenBrush(System.Windows.Media.Color.FromRgb(82, 82, 82))
        : FrozenBrush(System.Windows.Media.Color.FromRgb(180, 180, 180));

    public bool IsVerticalDock => Config.Edge is DockEdge.Left or DockEdge.Right;

    /// <summary>Places the tooltip toward the desktop (the side opposite the edge) so it does not sit behind the dock.</summary>
    public System.Windows.Controls.Primitives.PlacementMode DockToolTipPlacement => Config.Edge switch
    {
        DockEdge.Top => System.Windows.Controls.Primitives.PlacementMode.Bottom,
        DockEdge.Bottom => System.Windows.Controls.Primitives.PlacementMode.Top,
        DockEdge.Left => System.Windows.Controls.Primitives.PlacementMode.Right,
        DockEdge.Right => System.Windows.Controls.Primitives.PlacementMode.Left,
        _ => System.Windows.Controls.Primitives.PlacementMode.Bottom
    };

    public System.Windows.Controls.Orientation DockItemsOrientation =>
        IsVerticalDock ? System.Windows.Controls.Orientation.Vertical : System.Windows.Controls.Orientation.Horizontal;

    public System.Windows.HorizontalAlignment ItemsHorizontalAlignment =>
        IsVerticalDock
            ? System.Windows.HorizontalAlignment.Center
            : ResolveMajorAxisHorizontalAlignment();

    public System.Windows.VerticalAlignment ItemsVerticalAlignment =>
        IsVerticalDock
            ? ResolveMajorAxisVerticalAlignment()
            : System.Windows.VerticalAlignment.Center;

    public Thickness DockItemsPresenterMargin
    {
        get
        {
            var sidePaddingX = Math.Max(0, (IconLayerWidth - DockFrameWidth) / 2.0);
            var sidePaddingY = Math.Max(0, (IconLayerHeight - DockFrameHeight) / 2.0);
            var edgeAdjustment = FirstIconInsetFromDock - (Config.IconSpacing / 2.0);

            if (!IsVerticalDock)
            {
                return new Thickness(
                    sidePaddingX + edgeAdjustment,
                    0,
                    sidePaddingX + edgeAdjustment,
                    0);
            }

            return new Thickness(
                0,
                sidePaddingY + edgeAdjustment,
                0,
                sidePaddingY + edgeAdjustment);
        }
    }

    public double DockFrameWidth => DockMetrics.SnapLogicalToWholePhysical(IsVerticalDock ? Config.DockHeight : Config.DockWidth);

    public double DockFrameHeight => DockMetrics.SnapLogicalToWholePhysical(IsVerticalDock ? Config.DockWidth : Config.DockHeight);

    public System.Windows.HorizontalAlignment DockFrameHorizontalAlignment =>
        System.Windows.HorizontalAlignment.Center;

    public System.Windows.VerticalAlignment DockFrameVerticalAlignment =>
        System.Windows.VerticalAlignment.Center;

    public double IconLayerWidth =>
        DockMetrics.SnapLogicalToWholePhysical(Math.Min(ComputeIconLayerWidthUncapped(), ActiveBounds.Width));

    public double IconLayerHeight =>
        DockMetrics.SnapLogicalToWholePhysical(Math.Min(ComputeIconLayerHeightUncapped(), ActiveBounds.Height));

    /// <summary>Desired logical size of the icon layer before clamping to the monitor (hover/margins).</summary>
    private double ComputeIconLayerWidthUncapped()
    {
        var maxScale = GetMaxHoverScale();
        var magnifyOverflow = ((Config.IconSize * maxScale) - Config.IconSize) / 2.0;
        var neighborShiftOverflow = Math.Max(0, (maxScale - 1.0) * Config.IconSize);
        var extra = Math.Max(0, magnifyOverflow) + MinIconOverflowPadding;

        if (IsVerticalDock)
        {
            var requiredByMagnify = DockFrameWidth + (extra * 2.0);
            var requiredByDesign = DockFrameWidth * IconCrossAxisFactor;
            return Math.Max(requiredByDesign, requiredByMagnify);
        }

        return DockFrameWidth + ((Math.Max(0, magnifyOverflow) + neighborShiftOverflow + DockMetrics.IconMajorAxisPaddingForMetrics) * 2.0);
    }

    /// <summary>Desired logical size of the icon layer before clamping to the monitor (hover/margins).</summary>
    private double ComputeIconLayerHeightUncapped()
    {
        var maxScale = GetMaxHoverScale();
        var magnifyOverflow = ((Config.IconSize * maxScale) - Config.IconSize) / 2.0;
        var neighborShiftOverflow = Math.Max(0, (maxScale - 1.0) * Config.IconSize);
        var extra = Math.Max(0, magnifyOverflow) + MinIconOverflowPadding;

        if (IsVerticalDock)
        {
            return DockFrameHeight + ((Math.Max(0, magnifyOverflow) + neighborShiftOverflow + DockMetrics.IconMajorAxisPaddingForMetrics) * 2.0);
        }

        var requiredByMagnify = DockFrameHeight + (extra * 2.0);
        var requiredByDesign = DockFrameHeight * IconCrossAxisFactor;
        return Math.Max(requiredByDesign, requiredByMagnify);
    }

    private double EffectiveDockWidth => IsVerticalDock ? Config.DockHeight : Config.DockWidth;

    private double EffectiveDockHeight => IsVerticalDock ? Config.DockWidth : Config.DockHeight;

    public MainWindow(ConfigService configService, DockConfig config, string instanceLabel)
    {
        _configService = configService;
        Config = config;
        _instanceLabel = instanceLabel;

        InitializeComponent();
        DataContext = this;

        RefreshActiveMonitor();
        DockMetrics.WorkAreaSpanLogicalProvider = edge =>
            edge is DockEdge.Left or DockEdge.Right
                ? ActiveWorkArea.Height
                : ActiveWorkArea.Width;

        _autoHideTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(700)
        };
        _autoHideTimer.Tick += AutoHideTimer_Tick;

        _autoShowTimer = new DispatcherTimer();
        _autoShowTimer.Tick += AutoShowTimer_Tick;

        _pointerTrackerTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(PointerTrackerIdleIntervalMs)
        };
        _pointerTrackerTimer.Tick += PointerTrackerTimer_Tick;

        _saveDebounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _saveDebounceTimer.Tick += SaveDebounceTimer_Tick;

        _displaySettingsChangedHandler = (_, _) =>
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!IsLoaded)
                {
                    return;
                }

                DockMetrics.GetDpiPixelsPerInchX = () => VisualTreeHelper.GetDpi(this).PixelsPerInchX;
                Config.NotifyShellMetricsChanged();
                UpdateDockVisualsAndLayout();
            }));
        };
        SystemEvents.DisplaySettingsChanged += _displaySettingsChangedHandler;

        _userPreferenceChangedHandler = (_, args) =>
        {
            if (args.Category is not (UserPreferenceCategory.Desktop or UserPreferenceCategory.General))
            {
                return;
            }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!IsLoaded)
                {
                    return;
                }

                RefreshActiveMonitor();
                UpdateDockVisualsAndLayout();
            }));
        };
        SystemEvents.UserPreferenceChanged += _userPreferenceChangedHandler;

        DpiChanged += MainWindow_DpiChanged;
        LocationChanged += (_, _) => UpdateMonitorClip();
        SizeChanged += (_, _) => UpdateMonitorClip();
        Activated += MainWindow_Activated;
        Closed += MainWindow_Closed;

        Config.PropertyChanged += Config_PropertyChanged;
        Config.Items.CollectionChanged += ConfigItems_CollectionChanged;
        LoadItemsFromConfig();
    }

    private void ConfigItems_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_suppressConfigItemsDockWidthSync)
        {
            return;
        }

        if (Config.AutoAdjustDockWidth)
        {
            Config.SyncDockWidthLimits();
        }
        else
        {
            EnsureDockWidthFitsItems();
        }
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        Config.Items.CollectionChanged -= ConfigItems_CollectionChanged;
        SystemEvents.DisplaySettingsChanged -= _displaySettingsChangedHandler;
        SystemEvents.UserPreferenceChanged -= _userPreferenceChangedHandler;
        DpiChanged -= MainWindow_DpiChanged;
        Activated -= MainWindow_Activated;
        Closed -= MainWindow_Closed;
        UnregisterAppBar();
        UnregisterHotkeys();
        if (_hwndSource is not null)
        {
            _hwndSource.RemoveHook(WndProc);
            _hwndSource = null;
        }
    }

    private void MainWindow_DpiChanged(object sender, System.Windows.DpiChangedEventArgs e)
    {
        DockMetrics.GetDpiPixelsPerInchX = () => VisualTreeHelper.GetDpi(this).PixelsPerInchX;
        Config.NotifyShellMetricsChanged();
        UpdateDockVisualsAndLayout();
    }

    public void OpenSettings(SettingsCategory initialCategory = SettingsCategory.General)
    {
        if (System.Windows.Application.Current.Windows.OfType<SettingsWindow>().FirstOrDefault() is { } existing)
        {
            existing.NavigateTo(initialCategory);
            existing.Activate();
            return;
        }

        var settings = new SettingsWindow(Config, initialCategory, _instanceLabel)
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Width = Config.SettingsWindowWidth,
            Height = Config.SettingsWindowHeight
        };
        settings.Show();
    }

    public void SaveNow()
    {
        _saveDebounceTimer.Stop();
        SaveItemsIntoConfig();
        _configService.Save(Config);
    }

    public void ApplyDefaultPresetAndSave()
    {
        DockPresetReset.ApplyDefaultPreset(Config);
        SaveNow();
        ReloadDock();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        DockMetrics.GetDpiPixelsPerInchX = () => VisualTreeHelper.GetDpi(this).PixelsPerInchX;
        RefreshActiveMonitor();

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            _hwndSource = HwndSource.FromHwnd(hwnd);
            _hwndSource?.AddHook(WndProc);
            ExcludeFromAltTab(hwnd);
        }

        RegisterHotkeys();
        Config.NotifyShellMetricsChanged();
        UpdateDockVisualsAndLayout();
        EnsureVisibleFromAutohide();
        if (!Config.DockDisabled)
        {
            _pointerTrackerTimer.Start();
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case User32.WM_HOTKEY:
                HandleHotkey(wParam.ToInt32());
                break;
            case User32.WM_SETTINGCHANGE:
                if (wParam.ToInt32() == (int)User32.SPI_SETWORKAREA || wParam == IntPtr.Zero)
                {
                    ScheduleWorkAreaReposition();
                }

                break;
            case User32.WM_DISPLAYCHANGE:
                ScheduleWorkAreaReposition();
                break;
        }

        return IntPtr.Zero;
    }

    private void ScheduleWorkAreaReposition()
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (!IsLoaded)
            {
                return;
            }

            InvalidateAppBarPosition();
            RefreshActiveMonitor();
            UpdateDockVisualsAndLayout();
        }));
    }

    private void Config_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DockConfig.AutoHide))
        {
            if (Config.AutoHide)
            {
                _hotkeyHidden = false;
            }
            else
            {
                _autoHideTimer.Stop();
                _autoShowTimer.Stop();
                EnsureVisibleFromAutohide();
            }

            // The show/hide shortcut exists only when autohide is off.
            RefreshHotkeys();
        }

        if (e.PropertyName is nameof(DockConfig.MonitorDeviceName))
        {
            RefreshActiveMonitor();
        }

        if (e.PropertyName == nameof(DockConfig.HighQualityIcons))
        {
            LoadItemsFromConfig();
        }

        if (e.PropertyName == nameof(DockConfig.DockDisabled))
        {
            ApplyDockDisabledState();
        }

        UpdateDockVisualsAndLayout();
        if (!Config.IsRaisingShellMetricRefresh && e.PropertyName != nameof(DockConfig.DockDisabled))
        {
            ScheduleSave();
        }
    }

    private static void ExcludeFromAltTab(IntPtr hwnd)
    {
        var exStyle = User32.GetWindowLong(hwnd, User32.GWL_EXSTYLE);
        exStyle |= User32.WS_EX_TOOLWINDOW;
        exStyle &= ~User32.WS_EX_APPWINDOW;
        User32.SetWindowLongPtr(hwnd, User32.GWL_EXSTYLE, new IntPtr(exStyle));
        User32.SetWindowPos(
            hwnd,
            IntPtr.Zero,
            0,
            0,
            0,
            0,
            User32.SWP_NOMOVE | User32.SWP_NOSIZE | User32.SWP_NOZORDER | User32.SWP_FRAMECHANGED);
    }

    private void ApplyDockDisabledState()
    {
        if (Config.DockDisabled)
        {
            _pointerTrackerTimer.Stop();
            _autoHideTimer.Stop();
            _autoShowTimer.Stop();
            UnregisterAppBar();
            ResetFisheyeScale();
            Visibility = Visibility.Hidden;
            return;
        }

        if (!IsLoaded)
        {
            return;
        }

        _hotkeyHidden = false;
        EnsureVisibleFromAutohide();
        _pointerTrackerTimer.Start();
        UpdateAppBarReservation();
        ApplyZOrder();
    }

    /// <summary>
    /// Resizing the window makes WPF process the resize synchronously, and that can change settings
    /// that land here again. A nested pass would size the window while WPF is still finishing the
    /// previous resize, which leaves the content laid out with the old size, off the edge.
    /// So the nested request is deferred until the current pass has finished.
    /// </summary>
    private void UpdateDockVisualsAndLayout()
    {
        if (_isUpdatingDockLayout)
        {
            if (!_isDockLayoutUpdateQueued)
            {
                _isDockLayoutUpdateQueued = true;
                Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
                {
                    _isDockLayoutUpdateQueued = false;
                    UpdateDockVisualsAndLayout();
                }));
            }

            return;
        }

        _isUpdatingDockLayout = true;
        try
        {
            ApplyDockVisualsAndLayout();
        }
        finally
        {
            _isUpdatingDockLayout = false;
        }
    }

    private void ApplyDockVisualsAndLayout()
    {
        RefreshActiveMonitor();

        Width = IconLayerWidth;
        Height = IconLayerHeight;

        Topmost = Config.AlwaysOnTop;

        Visibility = Config.DockDisabled || (_hotkeyHidden && !Config.AutoHide) || _hiddenByFullscreen
            ? Visibility.Hidden
            : Visibility.Visible;

        // The reservation is applied before placement: it changes the work area the dock
        // position depends on, so that area must be read again before computing the compensation.
        if (UpdateAppBarReservation())
        {
            RefreshActiveMonitor();
        }

        PositionDockWindow();
        UpdateMonitorClip();

        NotifyPropertyChanged(nameof(ItemMargin));
        NotifyPropertyChanged(nameof(DockCornerRadius));
        NotifyPropertyChanged(nameof(DockBackgroundBrush));
        NotifyPropertyChanged(nameof(DockBorderBrush));
        NotifyPropertyChanged(nameof(DockBorderThickness));
        NotifyPropertyChanged(nameof(DockToolTipBackgroundBrush));
        NotifyPropertyChanged(nameof(DockToolTipForegroundBrush));
        NotifyPropertyChanged(nameof(DockToolTipBorderBrush));
        NotifyPropertyChanged(nameof(IsVerticalDock));
        NotifyPropertyChanged(nameof(DockToolTipPlacement));
        NotifyPropertyChanged(nameof(DockItemsOrientation));
        NotifyPropertyChanged(nameof(ItemsHorizontalAlignment));
        NotifyPropertyChanged(nameof(ItemsVerticalAlignment));
        NotifyPropertyChanged(nameof(DockItemsPresenterMargin));
        NotifyPropertyChanged(nameof(DockFrameWidth));
        NotifyPropertyChanged(nameof(DockFrameHeight));
        NotifyPropertyChanged(nameof(DockFrameHorizontalAlignment));
        NotifyPropertyChanged(nameof(DockFrameVerticalAlignment));
        NotifyPropertyChanged(nameof(IconLayerWidth));
        NotifyPropertyChanged(nameof(IconLayerHeight));

        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(ApplyZOrder));
    }

    /// <summary>The dock must sit below the taskbar instead of above it.</summary>
    private bool IsBehindTaskbarMode => Config.HideBehindTaskbar && Config.AlwaysOnTop && Config.AutoHide;

    /// <summary>
    /// Single source of truth for the dock Z-order: the topmost band when <c>AlwaysOnTop</c> is
    /// on and, if <c>HideBehindTaskbar</c> is also on, just below the taskbar. The dock tooltip
    /// is placed separately, always above (see DockItemToolTip_Opened).
    /// </summary>
    private void ApplyZOrder()
    {
        if (!IsLoaded || Config.DockDisabled)
        {
            return;
        }

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        if (!Config.AlwaysOnTop)
        {
            // Only when the flag must be cleared: HWND_NOTOPMOST also raises the window above
            // normal windows, and without AlwaysOnTop the dock must not jump in front of anyone.
            if (HasTopmostStyle(hwnd))
            {
                SetDockWindowZOrder(hwnd, User32.HWND_NOTOPMOST);
            }

            return;
        }

        // Insert after the shell window itself (not after GW_HWNDNEXT): if the next window is not
        // topmost, Windows strips WS_EX_TOPMOST from the dock and it ends up behind maximized windows.
        var taskbar = IsBehindTaskbarMode ? FindTaskbarWindowForActiveMonitor() : IntPtr.Zero;
        SetDockWindowZOrder(hwnd, taskbar != IntPtr.Zero ? taskbar : User32.HWND_TOPMOST);
    }

    private static bool HasTopmostStyle(IntPtr hwnd) =>
        (User32.GetWindowLong(hwnd, User32.GWL_EXSTYLE) & User32.WS_EX_TOPMOST) != 0;

    private static void SetDockWindowZOrder(IntPtr hwnd, IntPtr insertAfter)
    {
        User32.SetWindowPos(
            hwnd,
            insertAfter,
            0,
            0,
            0,
            0,
            User32.SWP_NOMOVE | User32.SWP_NOSIZE | User32.SWP_NOACTIVATE);
    }

    private IntPtr FindTaskbarWindowForActiveMonitor()
    {
        var bounds = _activeMonitor.BoundsPx;
        var fallback = IntPtr.Zero;

        foreach (var candidate in EnumerateTaskbarWindows())
        {
            if (fallback == IntPtr.Zero)
            {
                fallback = candidate;
            }

            if (!User32.GetWindowRect(candidate, out var rc))
            {
                continue;
            }

            if (rc.Left < bounds.Right && rc.Right > bounds.Left &&
                rc.Top < bounds.Bottom && rc.Bottom > bounds.Top)
            {
                return candidate;
            }
        }

        return fallback;
    }

    private static IEnumerable<IntPtr> EnumerateTaskbarWindows()
    {
        var primary = User32.FindWindow("Shell_TrayWnd", null);
        if (primary != IntPtr.Zero)
        {
            yield return primary;
        }

        var secondary = IntPtr.Zero;
        while ((secondary = User32.FindWindowEx(IntPtr.Zero, secondary, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero)
        {
            yield return secondary;
        }
    }

    /// <summary>
    /// Reasserts Z-order after events that change it (activating the dock on click, maximizing, or
    /// switching windows). Skipped while a tooltip is open or the dock is active, so it does not
    /// cover the tooltip or context menus, which must stay above the dock.
    /// </summary>
    private void MaintainZOrder()
    {
        if (!IsLoaded || !Config.AlwaysOnTop || _openPopupCount > 0 || IsActive || Visibility != Visibility.Visible)
        {
            return;
        }

        ApplyZOrder();
    }

    private void MainWindow_Activated(object? sender, EventArgs e)
    {
        // Activating the dock (for example by clicking an icon) raises it to the front of the
        // topmost band, which would put the dock above the taskbar: it is placed back immediately.
        if (IsBehindTaskbarMode)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(ApplyZOrder));
        }
    }

    private void DockPopup_Opened(object? sender, RoutedEventArgs e) => _openPopupCount++;

    private void DockPopup_Closed(object? sender, RoutedEventArgs e) =>
        _openPopupCount = Math.Max(0, _openPopupCount - 1);

    private void PositionDockWindow(bool animate = false)
    {
        GetDockLayout(out var windowLeft, out var windowTop, out var contentOffsetX, out var contentOffsetY);
        ApplyDockLayout(windowLeft, windowTop, contentOffsetX, contentOffsetY, animate);
    }

    /// <summary>
    /// Computes the window position and the content offset inside it.
    /// The window always stays anchored at the visible position and never leaves the assigned monitor, so
    /// Windows does not reassign it the DPI of a neighboring monitor; showing or hiding the dock only moves
    /// the content, and what falls outside the window is exactly the hidden part.
    /// </summary>
    private void GetDockLayout(out double windowLeft, out double windowTop, out double contentOffsetX, out double contentOffsetY)
    {
        var workArea = ActiveWorkArea;
        var bounds = ActiveBounds;
        var frameWidth = DockFrameWidth;
        var frameHeight = DockFrameHeight;
        var frameOffsetX = ResolveAlignedOffset(Width, frameWidth, DockFrameHorizontalAlignment);
        var frameOffsetY = ResolveAlignedOffset(Height, frameHeight, DockFrameVerticalAlignment);

        GetFrameTargetPosition(workArea, frameWidth, frameHeight, hidden: false, out var restLeft, out var restTop);
        GetFrameTargetPosition(workArea, frameWidth, frameHeight, hidden: _isHiddenByAutohide, out var targetLeft, out var targetTop);

        windowLeft = SnapAxisToMonitor(
            ClampWindowAxis(restLeft - frameOffsetX, Width, bounds.Left, bounds.Right),
            bounds.Left);
        windowTop = SnapAxisToMonitor(
            ClampWindowAxis(restTop - frameOffsetY, Height, bounds.Top, bounds.Bottom),
            bounds.Top);

        contentOffsetX = DockMetrics.SnapLogicalToWholePhysical(targetLeft - (windowLeft + frameOffsetX));
        contentOffsetY = DockMetrics.SnapLogicalToWholePhysical(targetTop - (windowTop + frameOffsetY));
    }

    private void GetFrameTargetPosition(
        Rect workArea,
        double frameWidth,
        double frameHeight,
        bool hidden,
        out double frameLeft,
        out double frameTop)
    {
        var offset = Config.Offset;

        var edgeGap = Math.Max(0, Config.AlignmentOffset);

        switch (Config.Edge)
        {
            case DockEdge.Top:
                frameLeft = workArea.Left + ((workArea.Width - frameWidth) / 2) + offset;
                frameTop = hidden
                    ? workArea.Top - frameHeight + RevealPixels
                    : workArea.Top + edgeGap;
                break;
            case DockEdge.Bottom:
                frameLeft = workArea.Left + ((workArea.Width - frameWidth) / 2) + offset;
                frameTop = hidden
                    ? workArea.Bottom - RevealPixels
                    : workArea.Bottom - frameHeight - edgeGap;
                break;
            case DockEdge.Left:
                frameLeft = hidden
                    ? workArea.Left - frameWidth + RevealPixels
                    : workArea.Left + edgeGap;
                frameTop = workArea.Top + ((workArea.Height - frameHeight) / 2) + offset;
                break;
            case DockEdge.Right:
                frameLeft = hidden
                    ? workArea.Right - RevealPixels
                    : workArea.Right - frameWidth - edgeGap;
                frameTop = workArea.Top + ((workArea.Height - frameHeight) / 2) + offset;
                break;
            default:
                frameLeft = workArea.Left;
                frameTop = workArea.Top;
                break;
        }

        // Clamp the frame to the assigned monitor's work area (keeps it off adjacent monitors).
        if (hidden)
        {
            return;
        }

        frameLeft = Math.Clamp(frameLeft, workArea.Left, Math.Max(workArea.Left, workArea.Right - frameWidth));
        frameTop = Math.Clamp(frameTop, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - frameHeight));
    }

    private static double ClampWindowAxis(double origin, double size, double min, double max)
    {
        return size >= max - min ? min : Math.Clamp(origin, min, max - size);
    }

    private static double SnapAxisToMonitor(double valueDip, double monitorOriginDip)
    {
        return monitorOriginDip + DockMetrics.SnapLogicalToWholePhysical(valueDip - monitorOriginDip);
    }

    /// <summary>Clips dock drawing strictly to the assigned monitor's bounds.</summary>
    private void UpdateMonitorClip()
    {
        if (!IsLoaded || double.IsNaN(Left) || double.IsNaN(Top) || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var bounds = ActiveBounds;
        var localX = bounds.Left - Left;
        var localY = bounds.Top - Top;
        Clip = new RectangleGeometry(new Rect(localX, localY, bounds.Width, bounds.Height));
    }

    private void ApplyDockLayout(
        double windowLeft,
        double windowTop,
        double contentOffsetX,
        double contentOffsetY,
        bool requestedAnimation)
    {
        BeginAnimation(Window.LeftProperty, null);
        BeginAnimation(Window.TopProperty, null);
        Left = windowLeft;
        Top = windowTop;

        var canAnimate = requestedAnimation &&
                         Config.AutoHide &&
                         Config.AutoHideSlideAnimation &&
                         Config.AutoHideSlideDurationMs > 0;
        if (!canAnimate)
        {
            DockContentTranslate.BeginAnimation(TranslateTransform.XProperty, null);
            DockContentTranslate.BeginAnimation(TranslateTransform.YProperty, null);
            DockContentTranslate.X = contentOffsetX;
            DockContentTranslate.Y = contentOffsetY;
            return;
        }

        var duration = TimeSpan.FromMilliseconds(Config.AutoHideSlideDurationMs);
        var xAnim = new DoubleAnimation(contentOffsetX, duration)
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        var yAnim = new DoubleAnimation(contentOffsetY, duration)
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        DockContentTranslate.BeginAnimation(TranslateTransform.XProperty, xAnim, HandoffBehavior.SnapshotAndReplace);
        DockContentTranslate.BeginAnimation(TranslateTransform.YProperty, yAnim, HandoffBehavior.SnapshotAndReplace);
    }

    private void Window_MouseEnter(object sender, WpfMouseEventArgs e)
    {
        // The pointer tracker handles exact edge contact; the hidden strip is wider.
        if (_isHiddenByAutohide && Config.AutoHideRevealMode == AutoHideRevealMode.EdgeTouch)
        {
            return;
        }

        RequestRevealFromAutohide();
    }

    private void Window_MouseLeave(object sender, WpfMouseEventArgs e)
    {
        if (!Config.AutoHide || !ShouldAutohideHideForWindows())
        {
            return;
        }

        if (Config.AutoHideRevealMode == AutoHideRevealMode.Proximity &&
            TryGetPointerPoints(out var pointerWindow, out _, out _) &&
            IsPointerInAutoHideProximity(pointerWindow))
        {
            return;
        }

        StartAutoHideCountdown();
    }

    private void StartAutoHideCountdown()
    {
        _autoShowTimer.Stop();
        _autoHideTimer.Stop();
        _autoHideTimer.Interval = TimeSpan.FromMilliseconds(Config.AutoHideHideDelayMs);
        _autoHideTimer.Start();
    }

    private void AutoHideTimer_Tick(object? sender, EventArgs e)
    {
        _autoHideTimer.Stop();
        if (!Config.AutoHide || IsMouseOver || !ShouldAutohideHideForWindows())
        {
            return;
        }

        _isHiddenByAutohide = true;
        PositionDockWindow(animate: true);
        ResetFisheyeScale();
    }

    private void RequestRevealFromAutohide()
    {
        _autoHideTimer.Stop();
        if (!_isHiddenByAutohide || !Config.AutoHide)
        {
            EnsureVisibleFromAutohide();
            return;
        }

        if (Config.AutoHideRevealDelayMs <= 0)
        {
            EnsureVisibleFromAutohide(animate: true);
            return;
        }

        if (_autoShowTimer.IsEnabled)
        {
            return;
        }

        _autoShowTimer.Stop();
        _autoShowTimer.Interval = TimeSpan.FromMilliseconds(Config.AutoHideRevealDelayMs);
        _autoShowTimer.Start();
    }

    private void AutoShowTimer_Tick(object? sender, EventArgs e)
    {
        _autoShowTimer.Stop();
        if ((Config.AutoHideRevealMode == AutoHideRevealMode.EdgeTouch || Config.HideWhenWindowIntersects) &&
            ShouldAutohideHideForWindows())
        {
            if (!TryGetPointerPoints(out var pointerWindow, out _, out var pointerScreenPx))
            {
                return;
            }

            var wantReveal = Config.AutoHideRevealMode == AutoHideRevealMode.Proximity
                ? IsPointerInAutoHideProximity(pointerWindow)
                : IsPointerTouchingScreenEdge(pointerScreenPx);
            if (!wantReveal)
            {
                return;
            }
        }

        EnsureVisibleFromAutohide(animate: true);
    }

    private void EnsureVisibleFromAutohide(bool animate = false)
    {
        _autoShowTimer.Stop();
        _autoHideTimer.Stop();
        if (!_isHiddenByAutohide)
        {
            return;
        }

        _isHiddenByAutohide = false;
        PositionDockWindow(animate);
    }

    private void DockList_DragOver(object sender, WpfDragEventArgs e)
    {
        if (e.Data.GetDataPresent(WpfDataFormats.FileDrop) || e.Data.GetDataPresent(typeof(DockItemViewModel)))
        {
            e.Effects = WpfDragDropEffects.Move;
            RevealForDrag(e);
        }
        else
        {
            e.Effects = WpfDragDropEffects.None;
        }

        e.Handled = true;
    }

    private void Window_DragOver(object sender, WpfDragEventArgs e) => RevealForDrag(e);

    /// <summary>Shows the hidden dock when a file or shortcut is dragged toward it.</summary>
    private void RevealForDrag(WpfDragEventArgs e)
    {
        if (!_isHiddenByAutohide || !e.Data.GetDataPresent(WpfDataFormats.FileDrop))
        {
            return;
        }

        EnsureVisibleFromAutohide(animate: true);
    }

    private void DockList_Drop(object sender, WpfDragEventArgs e)
    {
        EnsureVisibleFromAutohide();

        if (e.Data.GetDataPresent(WpfDataFormats.FileDrop))
        {
            if (e.Data.GetData(WpfDataFormats.FileDrop) is string[] paths)
            {
                var added = false;
                foreach (var path in paths)
                {
                    added |= TryAddDockItem(path);
                }

                if (added)
                {
                    SaveItemsIntoConfig();
                    ScheduleSave();
                }
            }

            return;
        }

        if (!e.Data.GetDataPresent(typeof(DockItemViewModel)))
        {
            return;
        }

        if (Config.LockShortcuts)
        {
            return;
        }

        if (e.Data.GetData(typeof(DockItemViewModel)) is not DockItemViewModel movedItem)
        {
            return;
        }

        var currentIndex = Items.IndexOf(movedItem);
        if (currentIndex < 0)
        {
            return;
        }

        var targetItem = TryGetItemUnderCursor(e.GetPosition(DockList));
        var targetIndex = targetItem is null ? Items.Count - 1 : Items.IndexOf(targetItem);
        if (targetIndex < 0)
        {
            return;
        }

        if (currentIndex != targetIndex)
        {
            Items.Move(currentIndex, targetIndex);
            SaveItemsIntoConfig();
            ScheduleSave();
        }
    }

    private void DockList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(DockList);
        _draggedItem = Config.LockShortcuts ? null : TryGetItemUnderCursor(_dragStartPoint);
    }

    private void DockList_PreviewMouseMove(object sender, WpfMouseEventArgs e)
    {
        if (_draggedItem is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var position = e.GetPosition(DockList);
        var dragThresholdX = Math.Abs(position.X - _dragStartPoint.X);
        var dragThresholdY = Math.Abs(position.Y - _dragStartPoint.Y);

        if (dragThresholdX < SystemParameters.MinimumHorizontalDragDistance &&
            dragThresholdY < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _isDraggingInternal = true;
        DragDrop.DoDragDrop(DockList, new WpfDataObject(typeof(DockItemViewModel), _draggedItem), WpfDragDropEffects.Move);
        _isDraggingInternal = false;
    }

    private void DockList_MouseMove(object sender, WpfMouseEventArgs e)
    {
        if (_isDraggingInternal || Items.Count == 0)
        {
            return;
        }

        var pointer = e.GetPosition(DockList);
        UpdateFisheyeScale(pointer);
    }

    private void DockList_MouseLeave(object sender, WpfMouseEventArgs e)
    {
        ResetFisheyeScale();
    }

    private void DockList_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not System.Windows.Controls.ListBox listBox)
        {
            return;
        }

        var position = Mouse.GetPosition(listBox);
        var hoveredItem = TryGetItemUnderCursor(position);
        if (hoveredItem is not null)
        {
            listBox.ContextMenu = null;
            return;
        }

        var menu = new ContextMenu();
        BuildEmptyDockAreaMenu(menu);
        listBox.ContextMenu = menu;
    }

    private void DockList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not System.Windows.Controls.ListBox listBox)
        {
            return;
        }

        if (TryGetItemUnderCursor(e.GetPosition(listBox)) is not null)
        {
            return;
        }

        e.Handled = true;
        var menu = new ContextMenu();
        BuildEmptyDockAreaMenu(menu);
        listBox.ContextMenu = menu;
        menu.PlacementTarget = listBox;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    private void BuildEmptyDockAreaMenu(ContextMenu menu)
    {
        var settingsItem = new MenuItem
        {
            Header = LocalizationService.Get(Config.Language, "TraySettings")
        };
        settingsItem.Click += (_, _) => OpenSettings(SettingsCategory.QuickAccess);
        menu.Items.Add(settingsItem);

        var reloadItem = new MenuItem
        {
            Header = LocalizationService.Get(Config.Language, "DockReload")
        };
        reloadItem.Click += (_, _) => ReloadDock();
        menu.Items.Add(reloadItem);

        menu.Items.Add(new Separator());

        var edgeMenu = new MenuItem { Header = LocalizationService.Get(Config.Language, "Edge") };
        AddEdgeMenuItem(edgeMenu, DockEdge.Top, "Top");
        AddEdgeMenuItem(edgeMenu, DockEdge.Bottom, "Bottom");
        AddEdgeMenuItem(edgeMenu, DockEdge.Left, "Left");
        AddEdgeMenuItem(edgeMenu, DockEdge.Right, "Right");
        menu.Items.Add(edgeMenu);

        var alignMenu = new MenuItem { Header = LocalizationService.Get(Config.Language, "ButtonAlignment") };
        var isVertical = IsVerticalDock;
        AddAlignmentMenuItem(alignMenu, Models.ButtonAlignment.Start, isVertical ? "Top" : "Left");
        AddAlignmentMenuItem(alignMenu, Models.ButtonAlignment.Center, isVertical ? "Middle" : "Center");
        AddAlignmentMenuItem(alignMenu, Models.ButtonAlignment.End, isVertical ? "Bottom" : "Right");
        menu.Items.Add(alignMenu);

        var autohideItem = new MenuItem
        {
            Header = LocalizationService.Get(Config.Language, "AutoHide"),
            IsCheckable = true,
            IsChecked = Config.AutoHide
        };
        autohideItem.Click += (_, _) => Config.AutoHide = !Config.AutoHide;
        menu.Items.Add(autohideItem);

        menu.Items.Add(new Separator());

        var lockShortcutsItem = new MenuItem
        {
            Header = LocalizationService.Get(Config.Language, "LockShortcuts"),
            IsCheckable = true,
            IsChecked = Config.LockShortcuts
        };
        lockShortcutsItem.Click += (_, _) => Config.LockShortcuts = !Config.LockShortcuts;
        menu.Items.Add(lockShortcutsItem);

        ApplyDockItemContextMenuTheme(menu);
    }

    private void AddEdgeMenuItem(MenuItem parent, DockEdge edge, string labelKey)
    {
        var item = new MenuItem
        {
            Header = LocalizationService.Get(Config.Language, labelKey),
            IsCheckable = true,
            IsChecked = Config.Edge == edge
        };
        item.Click += (_, _) => Config.Edge = edge;
        parent.Items.Add(item);
    }

    private void AddAlignmentMenuItem(MenuItem parent, Models.ButtonAlignment alignment, string labelKey)
    {
        var item = new MenuItem
        {
            Header = LocalizationService.Get(Config.Language, labelKey),
            IsCheckable = true,
            IsChecked = Config.ButtonAlignment == alignment
        };
        item.Click += (_, _) => Config.ButtonAlignment = alignment;
        parent.Items.Add(item);
    }

    public void ReloadDock()
    {
        DockMetrics.GetDpiPixelsPerInchX = () => VisualTreeHelper.GetDpi(this).PixelsPerInchX;
        Config.NotifyShellMetricsChanged();
        _autoHideTimer.Stop();
        _autoShowTimer.Stop();
        if (_isHiddenByAutohide)
        {
            _isHiddenByAutohide = false;
        }

        LoadItemsFromConfig();
        UpdateDockVisualsAndLayout();
        PositionDockWindow(animate: false);
        ResetFisheyeScale();
        InvalidateVisual();
        DockList.InvalidateVisual();
        UpdateLayout();
    }

    private void UpdateFisheyeScale(WpfPoint pointer)
    {
        if (!Config.EnableHoverEffects || Config.HoverMagnification <= 0)
        {
            ResetFisheyeScale();
            return;
        }

        var range = Math.Max(1, Config.HoverEffectRadius);
        var magnification = Config.HoverMagnification / 100.0;

        var analysis = new List<(DockItemViewModel Item, WpfPoint Center, double Scale)>(Items.Count);
        foreach (var item in Items)
        {
            var container = DockList.ItemContainerGenerator.ContainerFromItem(item) as ListBoxItem;
            if (container is null)
            {
                continue;
            }

            var center = container.TranslatePoint(
                new WpfPoint(container.ActualWidth / 2, container.ActualHeight / 2),
                DockList);

            var dx = center.X - pointer.X;
            var dy = center.Y - pointer.Y;
            var distance = Math.Sqrt((dx * dx) + (dy * dy));
            var normalized = Math.Clamp(1.0 - (distance / range), 0, 1);

            var scale = 1.0 + (normalized * magnification);
            analysis.Add((item, center, scale));
        }

        for (var i = 0; i < analysis.Count; i++)
        {
            var current = analysis[i];
            current.Item.Scale = current.Scale;

            var crossAxisOffset = ((current.Scale - 1.0) * Config.IconSize) / 2.0;
            var majorAxisOffset = 0.0;
            for (var j = 0; j < analysis.Count; j++)
            {
                if (i == j)
                {
                    continue;
                }

                var other = analysis[j];
                var majorDistance = IsVerticalDock
                    ? Math.Abs(other.Center.Y - current.Center.Y)
                    : Math.Abs(other.Center.X - current.Center.X);
                var influence = Math.Clamp(1.0 - (majorDistance / range), 0, 1);
                if (influence <= 0.0)
                {
                    continue;
                }

                var otherGrowth = Math.Max(0, (other.Scale - 1.0) * Config.IconSize);
                if (otherGrowth <= 0.0)
                {
                    continue;
                }

                var direction = IsVerticalDock
                    ? Math.Sign(current.Center.Y - other.Center.Y)
                    : Math.Sign(current.Center.X - other.Center.X);
                majorAxisOffset += direction * otherGrowth * 0.5 * influence;
            }

            if (IsVerticalDock)
            {
                current.Item.OffsetY = majorAxisOffset;
                current.Item.OffsetX = Config.Edge == DockEdge.Left ? crossAxisOffset : -crossAxisOffset;
            }
            else
            {
                current.Item.OffsetX = majorAxisOffset;
                current.Item.OffsetY = Config.Edge == DockEdge.Top ? crossAxisOffset : -crossAxisOffset;
            }
        }
    }

    private void ResetFisheyeScale()
    {
        foreach (var item in Items)
        {
            item.Scale = 1.0;
            item.OffsetX = 0;
            item.OffsetY = 0;
        }
    }

    private DockItemViewModel? TryGetItemUnderCursor(WpfPoint listPoint)
    {
        var hit = VisualTreeHelper.HitTest(DockList, listPoint)?.VisualHit;
        while (hit is not null)
        {
            if (hit is FrameworkElement element && element.DataContext is DockItemViewModel vm)
            {
                return vm;
            }

            hit = VisualTreeHelper.GetParent(hit);
        }

        return null;
    }

    private void DockItem_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDraggingInternal)
        {
            return;
        }

        if (sender is not FrameworkElement element || element.DataContext is not DockItemViewModel item)
        {
            return;
        }

        try
        {
            if (item.IsUwp && !string.IsNullOrWhiteSpace(item.AppUserModelId))
            {
                UwpAppService.Launch(item.AppUserModelId);
            }
            else
            {
                Process.Start(new ProcessStartInfo(item.Path)
                {
                    UseShellExecute = true
                });
            }

            if (Config.OpenAppsOnDockMonitor)
            {
                DockLaunchPlacement.Begin(new WindowInteropHelper(this).Handle);
            }
        }
        catch
        {
            var message = Config.Language == AppLanguage.English
                ? $"Could not open:\n{item.Path}"
                : $"No se pudo abrir:\n{item.Path}";
            WpfMessageBox.Show(message, "ToolDock", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void DockItem_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not FrameworkElement element || element.ContextMenu is null)
        {
            return;
        }

        if (element.ContextMenu.Items.Count == 0)
        {
            return;
        }

        if (element.ContextMenu.Items[0] is MenuItem menuItem)
        {
            menuItem.Header = LocalizationService.Get(Config.Language, "RemoveDockItem");
        }

        // Rebuild the custom-icon options (avoids duplicates when the menu opens again).
        for (var i = element.ContextMenu.Items.Count - 1; i >= 1; i--)
        {
            element.ContextMenu.Items.RemoveAt(i);
        }

        if (element.DataContext is DockItemViewModel vm)
        {
            element.ContextMenu.Items.Add(new Separator());

            var assign = new MenuItem { Header = LocalizationService.Get(Config.Language, "AssignCustomIcon") };
            assign.Click += (_, _) => AssignCustomIcon(vm);
            element.ContextMenu.Items.Add(assign);

            if (!string.IsNullOrWhiteSpace(vm.IconPath))
            {
                var reset = new MenuItem { Header = LocalizationService.Get(Config.Language, "ResetCustomIcon") };
                reset.Click += (_, _) => ResetCustomIcon(vm);
                element.ContextMenu.Items.Add(reset);
            }
        }

        ApplyDockItemContextMenuTheme(element.ContextMenu);
    }

    private void AssignCustomIcon(DockItemViewModel vm)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Icon / image (*.ico;*.png;*.exe;*.dll;*.jpg)|*.ico;*.png;*.exe;*.dll;*.jpg;*.jpeg|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog() != true)
        {
            return;
        }

        UpdateItemIconPath(vm, dlg.FileName);
    }

    private void ResetCustomIcon(DockItemViewModel vm) => UpdateItemIconPath(vm, null);

    private void UpdateItemIconPath(DockItemViewModel vm, string? iconPath)
    {
        var index = Items.IndexOf(vm);
        if (index < 0)
        {
            return;
        }

        var item = new DockItemConfig
        {
            Path = vm.Path,
            Name = vm.Name,
            IconPath = iconPath,
            AppUserModelId = vm.AppUserModelId,
            IsUwp = vm.IsUwp
        };
        var icon = IconService.LoadIconForItem(item, ResolveIconExtractionSize());
        Items[index] = new DockItemViewModel(vm.Path, vm.Name, icon, iconPath, vm.AppUserModelId, vm.IsUwp);
        SaveItemsIntoConfig();
        ScheduleSave();
    }

    private void DockItem_ToolTipOpening(object sender, ToolTipEventArgs e)
    {
        if (sender is not FrameworkElement element)
        {
            return;
        }

        if (element.ToolTip is not System.Windows.Controls.ToolTip toolTip)
        {
            return;
        }

        var isDark = Config.Theme == AppTheme.Dark;
        toolTip.Background = isDark
            ? FrozenBrush(System.Windows.Media.Color.FromRgb(36, 36, 36))
            : FrozenBrush(System.Windows.Media.Color.FromRgb(246, 246, 246));
        toolTip.Foreground = isDark
            ? FrozenBrush(System.Windows.Media.Color.FromRgb(235, 235, 235))
            : FrozenBrush(System.Windows.Media.Color.FromRgb(20, 20, 20));
        toolTip.BorderBrush = isDark
            ? FrozenBrush(System.Windows.Media.Color.FromRgb(82, 82, 82))
            : FrozenBrush(System.Windows.Media.Color.FromRgb(180, 180, 180));
        toolTip.BorderThickness = new Thickness(1);
        toolTip.Padding = new Thickness(8, 4, 8, 4);
    }

    /// <summary>
    /// The app name must always be the topmost element of the dock. With AlwaysOnTop on, the
    /// dock shares the topmost band with the popup, so the tooltip is raised and the dock is
    /// also explicitly lowered just below it instead of reordering the whole band.
    /// </summary>
    private void DockItemToolTip_Opened(object sender, RoutedEventArgs e)
    {
        _openPopupCount++;

        if (sender is not System.Windows.Controls.ToolTip toolTip)
        {
            return;
        }

        if (PresentationSource.FromVisual(toolTip) is not HwndSource source || source.Handle == IntPtr.Zero)
        {
            return;
        }

        SetDockWindowZOrder(source.Handle, User32.HWND_TOPMOST);

        // With "behind the taskbar" the dock is already below the shell (and the tooltip),
        // so its position is left alone to keep it there.
        if (IsBehindTaskbarMode)
        {
            return;
        }

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            SetDockWindowZOrder(hwnd, source.Handle);
        }
    }

    private void DockItemToolTip_Closed(object sender, RoutedEventArgs e)
    {
        _openPopupCount = Math.Max(0, _openPopupCount - 1);
        if (_openPopupCount == 0)
        {
            ApplyZOrder();
        }
    }

    private void RemoveItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem || menuItem.CommandParameter is not DockItemViewModel item)
        {
            return;
        }

        Items.Remove(item);
        SaveItemsIntoConfig();
        ScheduleSave();
    }

    private bool TryAddDockItem(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var extension = System.IO.Path.GetExtension(path).ToLowerInvariant();
        if (extension != ".exe" && extension != ".lnk")
        {
            return false;
        }

        if (!System.IO.File.Exists(path))
        {
            return false;
        }

        if (Items.Any(existing => string.Equals(existing.Path, path, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var icon = IconService.TryLoadIcon(path, ResolveIconExtractionSize());
        var name = System.IO.Path.GetFileNameWithoutExtension(path);
        Items.Add(new DockItemViewModel(path, name, icon));
        SaveItemsIntoConfig();
        return true;
    }

    /// <summary>Adds shortcuts to the dock from paths chosen in Explorer (same rules as drag and drop).</summary>
    public void AddShortcutsFromPaths(IEnumerable<string> paths)
    {
        var added = false;
        foreach (var path in paths)
        {
            added |= TryAddDockItem(path);
        }

        if (added)
        {
            SaveItemsIntoConfig();
            ScheduleSave();
        }
    }

    public void AddUwpApp(string aumid, string name)
    {
        if (string.IsNullOrWhiteSpace(aumid))
        {
            return;
        }

        var shellPath = UwpAppService.ToShellPath(aumid);
        if (Items.Any(existing => existing.IsUwp && string.Equals(existing.AppUserModelId, aumid, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var config = new DockItemConfig
        {
            Path = shellPath,
            Name = name,
            AppUserModelId = aumid,
            IsUwp = true
        };
        var icon = IconService.LoadIconForItem(config, ResolveIconExtractionSize());
        Items.Add(new DockItemViewModel(shellPath, name, icon, null, aumid, true));
        SaveItemsIntoConfig();
        ScheduleSave();
        EnsureVisibleFromAutohide();
    }

    private void LoadItemsFromConfig()
    {
        Items.Clear();
        foreach (var item in Config.Items)
        {
            if (!item.IsUwp && !System.IO.File.Exists(item.Path))
            {
                continue;
            }

            var icon = IconService.LoadIconForItem(item, ResolveIconExtractionSize());
            var name = string.IsNullOrWhiteSpace(item.Name)
                ? System.IO.Path.GetFileNameWithoutExtension(item.Path)
                : item.Name;
            Items.Add(new DockItemViewModel(item.Path, name, icon, item.IconPath, item.AppUserModelId, item.IsUwp));
        }

        EnsureDockWidthFitsItems();
    }

    private void SaveItemsIntoConfig()
    {
        _suppressConfigItemsDockWidthSync = true;
        try
        {
            Config.Items.Clear();
            foreach (var item in Items)
            {
                Config.Items.Add(item.ToConfig());
            }
        }
        finally
        {
            _suppressConfigItemsDockWidthSync = false;
        }

        if (Config.AutoAdjustDockWidth)
        {
            Config.SyncDockWidthLimits();
        }
        else
        {
            EnsureDockWidthFitsItems();
        }
    }

    private void ScheduleSave()
    {
        _saveDebounceTimer.Stop();
        _saveDebounceTimer.Start();
    }

    private void SaveDebounceTimer_Tick(object? sender, EventArgs e)
    {
        _saveDebounceTimer.Stop();
        SaveItemsIntoConfig();
        _configService.Save(Config);
    }

    private int ResolveIconExtractionSize()
    {
        var dpi = 1.0;
        try
        {
            dpi = IsLoaded
                ? VisualTreeHelper.GetDpi(this).DpiScaleX
                : DisplayService.GlobalDipScale();
        }
        catch
        {
            dpi = DisplayService.GlobalDipScale();
        }

        if (dpi <= 0)
        {
            dpi = 1.0;
        }

        return IconService.GetExtractionPixelSize(Config.IconSize, dpi, GetMaxHoverScale(), Config.HighQualityIcons);
    }

    private static System.Windows.Media.SolidColorBrush FrozenBrush(System.Windows.Media.Color color)
    {
        var brush = new System.Windows.Media.SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private void NotifyPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private System.Windows.Media.Color ParseSolidColor()
    {
        try
        {
            var converted = System.Windows.Media.ColorConverter.ConvertFromString(Config.SolidColorHex);
            if (converted is System.Windows.Media.Color color)
            {
                if (color.A == 0)
                {
                    color.A = 255;
                }

                return color;
            }
        }
        catch
        {
            // fallback below
        }

        return System.Windows.Media.Color.FromArgb(255, 43, 43, 43);
    }

    private System.Windows.Media.Color ParseOutlineColor()
    {
        try
        {
            var converted = System.Windows.Media.ColorConverter.ConvertFromString(Config.OutlineColorHex);
            if (converted is System.Windows.Media.Color color)
            {
                if (color.A == 0)
                {
                    color.A = 255;
                }

                return color;
            }
        }
        catch
        {
            // fallback below
        }

        return System.Windows.Media.Color.FromArgb(255, 255, 255, 255);
    }

    private static System.Windows.Media.Color ApplyOpacity(System.Windows.Media.Color color, int percentOpacity)
    {
        var alpha = (byte)Math.Clamp((int)Math.Round((percentOpacity / 100.0) * 255), 0, 255);
        return System.Windows.Media.Color.FromArgb(alpha, color.R, color.G, color.B);
    }

    private static double ResolveAlignedOffset(double outerSize, double innerSize, System.Windows.HorizontalAlignment alignment)
    {
        return alignment switch
        {
            System.Windows.HorizontalAlignment.Left => 0,
            System.Windows.HorizontalAlignment.Right => Math.Max(0, outerSize - innerSize),
            _ => Math.Max(0, (outerSize - innerSize) / 2.0)
        };
    }

    private static double ResolveAlignedOffset(double outerSize, double innerSize, System.Windows.VerticalAlignment alignment)
    {
        return alignment switch
        {
            System.Windows.VerticalAlignment.Top => 0,
            System.Windows.VerticalAlignment.Bottom => Math.Max(0, outerSize - innerSize),
            _ => Math.Max(0, (outerSize - innerSize) / 2.0)
        };
    }

    private void EnsureDockWidthFitsItems()
    {
        if (Config.AutoAdjustDockWidth)
        {
            Config.SyncDockWidthLimits();
            return;
        }

        var minWidth = DockConfig.GetDockWidthMin(Config.IconSize, Config.IconSpacing, Config.Items.Count);
        if (Config.DockWidth < minWidth)
        {
            Config.DockWidth = minWidth;
        }
    }

    private void ApplyDockItemContextMenuTheme(ContextMenu contextMenu)
    {
        // Idempotent: this method runs every time the menu opens.
        contextMenu.Opened -= DockPopup_Opened;
        contextMenu.Opened += DockPopup_Opened;
        contextMenu.Closed -= DockPopup_Closed;
        contextMenu.Closed += DockPopup_Closed;
        DockContextMenuTheme.Apply(contextMenu, Config.Theme);
    }

    private System.Windows.HorizontalAlignment ResolveMajorAxisHorizontalAlignment()
    {
        return Config.ButtonAlignment switch
        {
            Models.ButtonAlignment.Start => System.Windows.HorizontalAlignment.Left,
            Models.ButtonAlignment.End => System.Windows.HorizontalAlignment.Right,
            _ => System.Windows.HorizontalAlignment.Center
        };
    }

    private System.Windows.VerticalAlignment ResolveMajorAxisVerticalAlignment()
    {
        return Config.ButtonAlignment switch
        {
            Models.ButtonAlignment.Start => System.Windows.VerticalAlignment.Top,
            Models.ButtonAlignment.End => System.Windows.VerticalAlignment.Bottom,
            _ => System.Windows.VerticalAlignment.Center
        };
    }

    private double GetMaxHoverScale()
    {
        return 1.0 + (Math.Max(0, Config.HoverMagnification) / 100.0);
    }

    private const double EdgeRevealSpanPadding = 10.0;

    /// <summary>Tracker ticks (~30 fps when idle) between Z-order reassertions.</summary>
    private const int ZOrderReassertIntervalTicks = 30;

    private void PointerTrackerTimer_Tick(object? sender, EventArgs e)
    {
        if (!IsLoaded || Config.DockDisabled)
        {
            return;
        }

        if (++_zOrderTicksSinceReassert >= ZOrderReassertIntervalTicks)
        {
            _zOrderTicksSinceReassert = 0;
            MaintainZOrder();
        }

        if (!TryGetPointerPoints(out var pointerWindow, out var pointerDock, out var pointerScreenPx))
        {
            return;
        }

        var pointerMoved =
            Math.Abs(pointerWindow.X - _lastTrackedPointerWindow.X) >= 0.5 ||
            Math.Abs(pointerWindow.Y - _lastTrackedPointerWindow.Y) >= 0.5;
        _lastTrackedPointerWindow = pointerWindow;

        var pointerScreen = new WpfPoint(Left + pointerWindow.X, Top + pointerWindow.Y);

        // "Don't show over fullscreen apps": applies with or without autohide and with or without
        // AlwaysOnTop, but only while the fullscreen app has focus.
        UpdateFullscreenSuppression();
        if (_hiddenByFullscreen)
        {
            SetPointerTrackerInterval(hovering: false);
            return;
        }

        if (Config.AutoHide)
        {
            var shouldHideForWindows = ShouldAutohideHideForWindows();
            var wantReveal = shouldHideForWindows && (Config.AutoHideRevealMode == AutoHideRevealMode.Proximity
                ? IsPointerInDockEdgeRegion(pointerScreen, Config.AutoHideProximityDistance)
                : IsPointerTouchingScreenEdge(pointerScreenPx));
            UpdateAutohideState(shouldHideForWindows, wantReveal, User32.IsLeftOrRightMouseButtonDown());
        }

        if (_isHiddenByAutohide || _isDraggingInternal || Items.Count == 0)
        {
            SetPointerTrackerInterval(hovering: false);
            return;
        }

        var inHoverZone = IsPointerWithinHoverActivationZone(pointerWindow);
        SetPointerTrackerInterval(inHoverZone);
        if (inHoverZone)
        {
            if (pointerMoved || !_pointerTrackerInHoverZone)
            {
                UpdateFisheyeScale(pointerDock);
                _fisheyeNeedsReset = true;
            }
        }
        else if (_fisheyeNeedsReset)
        {
            ResetFisheyeScale();
            _fisheyeNeedsReset = false;
        }

        _pointerTrackerInHoverZone = inHoverZone;
    }

    private void UpdateAutohideState(bool shouldHideForWindows, bool wantReveal, bool buttonHeld)
    {
        if (!shouldHideForWindows)
        {
            if (_isHiddenByAutohide)
            {
                RequestRevealFromAutohide();
            }

            _autoHideTimer.Stop();
        }
        else if (_isHiddenByAutohide)
        {
            if (wantReveal)
            {
                RequestRevealFromAutohide();
            }
            else if (Config.AutoHideRevealMode == AutoHideRevealMode.EdgeTouch || Config.HideWhenWindowIntersects)
            {
                _autoShowTimer.Stop();
            }
        }
        else if (IsMouseOver || wantReveal)
        {
            _autoHideTimer.Stop();
        }
        // Moving an intersecting window must not wait for the mouse button to be released.
        else if ((Config.HideWhenWindowIntersects || !buttonHeld) && !_autoHideTimer.IsEnabled)
        {
            StartAutoHideCountdown();
        }
    }

    private void SetPointerTrackerInterval(bool hovering)
    {
        var ms = hovering ? PointerTrackerHoverIntervalMs : PointerTrackerIdleIntervalMs;
        var desired = TimeSpan.FromMilliseconds(ms);
        if (_pointerTrackerTimer.Interval != desired)
        {
            _pointerTrackerTimer.Interval = desired;
        }
    }

    private void UpdateFullscreenSuppression()
    {
        var shouldHide = Config.DoNotShowOverFullscreen && IsForegroundFullscreenOnActiveMonitor();
        if (shouldHide == _hiddenByFullscreen)
        {
            return;
        }

        _hiddenByFullscreen = shouldHide;
        Visibility = Config.DockDisabled || (_hotkeyHidden && !Config.AutoHide) || _hiddenByFullscreen
            ? Visibility.Hidden
            : Visibility.Visible;
        if (_hiddenByFullscreen)
        {
            ResetFisheyeScale();
            _fisheyeNeedsReset = false;
        }
    }

    private bool IsForegroundFullscreenOnActiveMonitor()
    {
        try
        {
            var foreground = User32.GetForegroundWindow();
            if (foreground == IntPtr.Zero)
            {
                return false;
            }

            var self = new WindowInteropHelper(this).Handle;
            if (foreground == self)
            {
                return false;
            }

            // The desktop and the taskbar also cover the monitor; they are not fullscreen apps.
            if (IsShellWindowClass(foreground))
            {
                return false;
            }

            if (!User32.GetWindowRect(foreground, out var rect))
            {
                return false;
            }

            var bounds = _activeMonitor.BoundsPx;
            var coversMonitor = rect.Left <= bounds.Left + 2 &&
                                rect.Top <= bounds.Top + 2 &&
                                rect.Right >= bounds.Right - 2 &&
                                rect.Bottom >= bounds.Bottom - 2;
            if (!coversMonitor)
            {
                return false;
            }

            // Covering the monitor is not enough: a maximized window does that too when the taskbar
            // is hidden or when the dock reserves space, and it must not hide the dock. It counts
            // as real fullscreen if it has no window frame (exclusive fullscreen or borderless)
            // or if the shell itself reports a fullscreen app (for example F11).
            return IsBorderlessWindow(foreground) || Shell32.IsShellReportingFullscreenApp();
        }
        catch
        {
            return false;
        }
    }

    private static bool IsBorderlessWindow(IntPtr hwnd)
    {
        var style = User32.GetWindowLong(hwnd, User32.GWL_STYLE);
        return style != 0 && (style & (User32.WS_CAPTION | User32.WS_THICKFRAME)) == 0;
    }

    private static bool IsShellWindowClass(IntPtr hwnd)
    {
        var buffer = new System.Text.StringBuilder(64);
        if (User32.GetClassName(hwnd, buffer, buffer.Capacity) == 0)
        {
            return false;
        }

        var className = buffer.ToString();
        return className is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";
    }

    /// <summary>
    /// Without a conditional mode, normal autohide is kept; if one is active, it must be satisfied.
    /// </summary>
    private bool ShouldAutohideHideForWindows()
    {
        if (!Config.HideWhenMaximized && !Config.HideWhenWindowIntersects)
        {
            return true;
        }

        var self = new WindowInteropHelper(this).Handle;
        var monitor = self != IntPtr.Zero
            ? User32.MonitorFromWindow(self, User32.MONITOR_DEFAULTTONEAREST)
            : IntPtr.Zero;
        if (monitor == IntPtr.Zero)
        {
            var bounds = _activeMonitor.BoundsPx;
            var center = new User32.POINT
            {
                X = (int)Math.Round(bounds.Left + (bounds.Width / 2)),
                Y = (int)Math.Round(bounds.Top + (bounds.Height / 2))
            };
            monitor = User32.MonitorFromPoint(center, User32.MONITOR_DEFAULTTONEAREST);
        }

        var dockBounds = Config.HideWhenWindowIntersects ? GetVisibleDockBounds() : Rect.Empty;
        var found = false;
        User32.EnumWindows((hwnd, _) =>
        {
            found = WindowRequiresAutohide(hwnd, self, monitor, dockBounds);
            return !found;
        }, IntPtr.Zero);

        return found;
    }

    private bool WindowRequiresAutohide(IntPtr hwnd, IntPtr self, IntPtr monitor, Rect dockBounds)
    {
        if (hwnd == self || !User32.IsWindowVisible(hwnd) || User32.IsIconic(hwnd) ||
            User32.IsWindowCloaked(hwnd) || IsShellWindowClass(hwnd))
        {
            return false;
        }

        if (Config.HideWhenMaximized &&
            (User32.GetWindowLong(hwnd, User32.GWL_STYLE) & User32.WS_MAXIMIZE) != 0 &&
            User32.MonitorFromWindow(hwnd, User32.MONITOR_DEFAULTTONEAREST) == monitor)
        {
            return true;
        }

        if (!Config.HideWhenWindowIntersects || dockBounds.IsEmpty)
        {
            return false;
        }

        // Popups and other docks in the same process are not windows that should hide us.
        if ((User32.GetWindowLong(hwnd, User32.GWL_EXSTYLE) & User32.WS_EX_TOOLWINDOW) != 0)
        {
            User32.GetWindowThreadProcessId(hwnd, out var processId);
            if (processId == (uint)Environment.ProcessId)
            {
                return false;
            }
        }

        return User32.TryGetVisibleWindowRect(hwnd, out var rect) &&
               rect.Width > 0 && rect.Height > 0 &&
               rect.Left < dockBounds.Right && rect.Right > dockBounds.Left &&
               rect.Top < dockBounds.Bottom && rect.Bottom > dockBounds.Top;
    }

    private Rect GetVisibleDockBounds()
    {
        // Always measuring the resting position avoids toggling show/hide during the slide.
        GetFrameTargetPosition(ActiveWorkArea, DockFrameWidth, DockFrameHeight, hidden: false,
            out var frameLeft, out var frameTop);
        var frameOffsetX = ResolveAlignedOffset(Width, DockFrameWidth, DockFrameHorizontalAlignment);
        var frameOffsetY = ResolveAlignedOffset(Height, DockFrameHeight, DockFrameVerticalAlignment);
        var localLeft = frameOffsetX + DockMetrics.SnapLogicalToWholePhysical(frameLeft - (Left + frameOffsetX));
        var localTop = frameOffsetY + DockMetrics.SnapLogicalToWholePhysical(frameTop - (Top + frameOffsetY));
        var topLeft = PointToScreen(new WpfPoint(localLeft, localTop));
        var bottomRight = PointToScreen(new WpfPoint(localLeft + DockFrameWidth, localTop + DockFrameHeight));
        var bounds = new Rect(topLeft, bottomRight);
        bounds.Intersect(_activeMonitor.BoundsPx);
        return bounds;
    }

    private (double Start, double End) GetDockMajorAxisScreenSpan()
    {
        var wa = ActiveWorkArea;
        var frameWidth = DockFrameWidth;
        var frameHeight = DockFrameHeight;
        var offset = Config.Offset;
        if (IsVerticalDock)
        {
            var top = wa.Top + ((wa.Height - frameHeight) / 2) + offset;
            return (top, top + frameHeight);
        }

        var left = wa.Left + ((wa.Width - frameWidth) / 2) + offset;
        return (left, left + frameWidth);
    }

    private bool IsPointerInDockEdgeRegion(WpfPoint pointerScreen, double edgeDistance)
    {
        var wa = ActiveWorkArea;
        var (spanStart, spanEnd) = GetDockMajorAxisScreenSpan();
        var pad = EdgeRevealSpanPadding;

        return Config.Edge switch
        {
            DockEdge.Top => pointerScreen.Y >= wa.Top - 2 && pointerScreen.Y <= wa.Top + edgeDistance &&
                            pointerScreen.X >= Math.Max(wa.Left, spanStart - pad) && pointerScreen.X <= Math.Min(wa.Right, spanEnd + pad),
            DockEdge.Bottom => pointerScreen.Y <= wa.Bottom + 2 && pointerScreen.Y >= wa.Bottom - edgeDistance &&
                               pointerScreen.X >= Math.Max(wa.Left, spanStart - pad) && pointerScreen.X <= Math.Min(wa.Right, spanEnd + pad),
            DockEdge.Left => pointerScreen.X >= wa.Left - 2 && pointerScreen.X <= wa.Left + edgeDistance &&
                             pointerScreen.Y >= Math.Max(wa.Top, spanStart - pad) && pointerScreen.Y <= Math.Min(wa.Bottom, spanEnd + pad),
            DockEdge.Right => pointerScreen.X <= wa.Right + 2 && pointerScreen.X >= wa.Right - edgeDistance &&
                              pointerScreen.Y >= Math.Max(wa.Top, spanStart - pad) && pointerScreen.Y <= Math.Min(wa.Bottom, spanEnd + pad),
            _ => false
        };
    }

    private bool IsPointerTouchingScreenEdge(WpfPoint pointerScreenPx)
    {
        var bounds = _activeMonitor.BoundsPx;
        if (pointerScreenPx.X < bounds.Left || pointerScreenPx.X >= bounds.Right ||
            pointerScreenPx.Y < bounds.Top || pointerScreenPx.Y >= bounds.Bottom)
        {
            return false;
        }

        // Win32's right and bottom bounds are exclusive: the last pixel is one less.
        var touchesEdge = Config.Edge switch
        {
            DockEdge.Top => pointerScreenPx.Y == bounds.Top,
            DockEdge.Bottom => pointerScreenPx.Y == bounds.Bottom - 1,
            DockEdge.Left => pointerScreenPx.X == bounds.Left,
            DockEdge.Right => pointerScreenPx.X == bounds.Right - 1,
            _ => false
        };
        if (!touchesEdge)
        {
            return false;
        }

        var frame = GetVisibleDockBounds();
        var pad = DockMetrics.LogicalToPhysicalExact(EdgeRevealSpanPadding);
        return !frame.IsEmpty && (IsVerticalDock
            ? pointerScreenPx.Y >= frame.Top - pad && pointerScreenPx.Y <= frame.Bottom + pad
            : pointerScreenPx.X >= frame.Left - pad && pointerScreenPx.X <= frame.Right + pad);
    }

    private bool TryGetPointerPoints(out WpfPoint pointerWindow, out WpfPoint pointerDock, out WpfPoint pointerScreenPx)
    {
        pointerWindow = default;
        pointerDock = default;
        pointerScreenPx = default;

        if (!User32.GetCursorPos(out var screenPosition))
        {
            return false;
        }

        pointerScreenPx = new WpfPoint(screenPosition.X, screenPosition.Y);
        pointerWindow = PointFromScreen(pointerScreenPx);
        var dockOrigin = DockList.TranslatePoint(new WpfPoint(0, 0), this);
        pointerDock = new WpfPoint(pointerWindow.X - dockOrigin.X, pointerWindow.Y - dockOrigin.Y);
        return true;
    }

    private bool IsPointerWithinHoverActivationZone(WpfPoint pointerWindow)
    {
        var frameLeft = ResolveAlignedOffset(Width, DockFrameWidth, DockFrameHorizontalAlignment) + DockContentTranslate.X;
        var frameTop = ResolveAlignedOffset(Height, DockFrameHeight, DockFrameVerticalAlignment) + DockContentTranslate.Y;
        var frameRect = new Rect(frameLeft, frameTop, DockFrameWidth, DockFrameHeight);
        var activationDistance = Math.Max(1, Config.HoverEffectRadius);

        var dx = 0.0;
        if (pointerWindow.X < frameRect.Left)
        {
            dx = frameRect.Left - pointerWindow.X;
        }
        else if (pointerWindow.X > frameRect.Right)
        {
            dx = pointerWindow.X - frameRect.Right;
        }

        var dy = 0.0;
        if (pointerWindow.Y < frameRect.Top)
        {
            dy = frameRect.Top - pointerWindow.Y;
        }
        else if (pointerWindow.Y > frameRect.Bottom)
        {
            dy = pointerWindow.Y - frameRect.Bottom;
        }

        var distance = Math.Sqrt((dx * dx) + (dy * dy));
        return distance <= activationDistance;
    }

    private bool IsPointerInAutoHideProximity(WpfPoint pointerWindow)
    {
        var pointerScreen = new WpfPoint(Left + pointerWindow.X, Top + pointerWindow.Y);
        return IsPointerInDockEdgeRegion(pointerScreen, Config.AutoHideProximityDistance);
    }

    // ---------------------------------------------------------------------
    // Global hotkeys
    // ---------------------------------------------------------------------

    private void RegisterHotkeys()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        UnregisterHotkeys();

        // With autohide the pointer owns the dock: the shortcut is saved, but it is not registered.
        if (!Config.AutoHide && Config.ShowHideHotkey.IsAssigned)
        {
            User32.RegisterHotKey(hwnd, HotkeyShowHideId,
                (uint)Config.ShowHideHotkey.Modifiers | User32.MOD_NOREPEAT,
                (uint)Config.ShowHideHotkey.Key);
        }

        if (Config.ShowDockHotkey.IsAssigned)
        {
            User32.RegisterHotKey(hwnd, HotkeyShowDockId,
                (uint)Config.ShowDockHotkey.Modifiers | User32.MOD_NOREPEAT,
                (uint)Config.ShowDockHotkey.Key);
        }

        if (Config.DisableDockHotkey.IsAssigned)
        {
            User32.RegisterHotKey(hwnd, HotkeyDisableDockId,
                (uint)Config.DisableDockHotkey.Modifiers | User32.MOD_NOREPEAT,
                (uint)Config.DisableDockHotkey.Key);
        }
    }

    private void UnregisterHotkeys()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        User32.UnregisterHotKey(hwnd, HotkeyShowHideId);
        User32.UnregisterHotKey(hwnd, HotkeyShowDockId);
        User32.UnregisterHotKey(hwnd, HotkeyDisableDockId);
    }

    public void RefreshHotkeys() => RegisterHotkeys();

    private void HandleHotkey(int id)
    {
        switch (id)
        {
            case HotkeyShowHideId:
                ToggleDockVisibilityFromHotkey();
                break;
            case HotkeyShowDockId:
                ShowDock();
                break;
            case HotkeyDisableDockId:
                Config.DockDisabled = !Config.DockDisabled;
                break;
        }
    }

    private void ToggleDockVisibilityFromHotkey()
    {
        if (Config.DockDisabled || Config.AutoHide)
        {
            return;
        }

        _hotkeyHidden = !_hotkeyHidden;
        Visibility = _hotkeyHidden ? Visibility.Hidden : Visibility.Visible;
    }

    /// <summary>
    /// Shows the dock without toggling its state (same action as "Show Dock" in the tray menu).
    /// </summary>
    public void ShowDock()
    {
        if (Config.DockDisabled)
        {
            return;
        }

        _hotkeyHidden = false;
        _hiddenByFullscreen = false;
        Visibility = Visibility.Visible;
        if (!IsVisible)
        {
            Show();
        }

        EnsureVisibleFromAutohide(animate: true);
        Activate();
        ApplyZOrder();
    }

    // ---------------------------------------------------------------------
    // Work-area reservation (AppBar, taskbar style)
    // ---------------------------------------------------------------------

    /// <summary>Returns true if the reservation changed, so the work area is read again before placement.</summary>
    private bool UpdateAppBarReservation()
    {
        var shouldReserve = Config.ReserveWorkArea && !Config.AutoHide && !Config.DockDisabled;
        return shouldReserve ? RegisterAppBar() : UnregisterAppBar();
    }

    private bool RegisterAppBar()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        var data = new Shell32.APPBARDATA
        {
            cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Shell32.APPBARDATA>(),
            hWnd = hwnd
        };

        var justRegistered = false;
        if (!_appBarRegistered)
        {
            Shell32.SHAppBarMessage(Shell32.ABM_NEW, ref data);
            _appBarRegistered = true;
            justRegistered = true;
        }

        var bounds = _activeMonitor.BoundsPx;
        var scale = DisplayService.GlobalDipScale();
        if (scale <= 0)
        {
            scale = 1.0;
        }

        // Dock thickness is always Dock Height: horizontal on Top/Bottom, width on Left/Right.
        var thicknessPx = (int)Math.Round((IsVerticalDock ? DockFrameWidth : DockFrameHeight) * scale);

        var edge = Config.Edge switch
        {
            DockEdge.Top => Shell32.ABE_TOP,
            DockEdge.Bottom => Shell32.ABE_BOTTOM,
            DockEdge.Left => Shell32.ABE_LEFT,
            _ => Shell32.ABE_RIGHT
        };

        var rc = new User32.RECT
        {
            Left = (int)bounds.Left,
            Top = (int)bounds.Top,
            Right = (int)bounds.Right,
            Bottom = (int)bounds.Bottom
        };

        switch (Config.Edge)
        {
            case DockEdge.Top:
                rc.Bottom = rc.Top + thicknessPx;
                break;
            case DockEdge.Bottom:
                rc.Top = rc.Bottom - thicknessPx;
                break;
            case DockEdge.Left:
                rc.Right = rc.Left + thicknessPx;
                break;
            case DockEdge.Right:
                rc.Left = rc.Right - thicknessPx;
                break;
        }

        _reservedEdge = Config.Edge;

        // Without this cutoff, every ABM_SETPOS raises a WM_SETTINGCHANGE that recomputes the
        // layout and reserves again, in a loop.
        if (_appBarPositionApplied &&
            !justRegistered &&
            _requestedAppBarEdge == edge &&
            _requestedAppBarRect.Left == rc.Left &&
            _requestedAppBarRect.Top == rc.Top &&
            _requestedAppBarRect.Right == rc.Right &&
            _requestedAppBarRect.Bottom == rc.Bottom)
        {
            return false;
        }

        _requestedAppBarEdge = edge;
        _requestedAppBarRect = rc;
        _appBarPositionApplied = true;

        data.uEdge = edge;
        data.rc = rc;
        Shell32.SHAppBarMessage(Shell32.ABM_QUERYPOS, ref data);

        // ABM_QUERYPOS clips the free side against the taskbar and other appbars instead of
        // shifting the rectangle, so it can return a thickness of 0 (and then nothing is
        // reserved). The thickness must be restored on the already-adjusted edge before ABM_SETPOS.
        var granted = data.rc;
        switch (Config.Edge)
        {
            case DockEdge.Top:
                granted.Bottom = granted.Top + thicknessPx;
                break;
            case DockEdge.Bottom:
                granted.Top = granted.Bottom - thicknessPx;
                break;
            case DockEdge.Left:
                granted.Right = granted.Left + thicknessPx;
                break;
            case DockEdge.Right:
                granted.Left = granted.Right - thicknessPx;
                break;
        }

        data.rc = granted;
        Shell32.SHAppBarMessage(Shell32.ABM_SETPOS, ref data);

        _grantedAppBarRect = data.rc;
        _hasGrantedAppBarRect = true;
        _lastAppBarApplyUtc = DateTime.UtcNow;
        return true;
    }

    /// <summary>
    /// Forces the reservation to be recomputed when the work area changes for an external reason (the
    /// taskbar moves, another app reserves space). The time window avoids feeding back on the
    /// WM_SETTINGCHANGE raised by our own ABM_SETPOS.
    /// </summary>
    private void InvalidateAppBarPosition()
    {
        if ((DateTime.UtcNow - _lastAppBarApplyUtc).TotalMilliseconds < 1500)
        {
            return;
        }

        _appBarPositionApplied = false;
    }

    private bool UnregisterAppBar()
    {
        _appBarPositionApplied = false;
        _requestedAppBarEdge = -1;
        _hasGrantedAppBarRect = false;

        if (!_appBarRegistered)
        {
            return false;
        }

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        var data = new Shell32.APPBARDATA
        {
            cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Shell32.APPBARDATA>(),
            hWnd = hwnd
        };
        Shell32.SHAppBarMessage(Shell32.ABM_REMOVE, ref data);
        _appBarRegistered = false;
        return true;
    }

    /// <summary>Builds a diagnostic report (config, monitors, DPI, work areas, state).</summary>
    public string BuildDiagnosticReport()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("ToolDock — Detailed Log");
        sb.AppendLine($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Instance: {(string.IsNullOrWhiteSpace(_instanceLabel) ? "(primary)" : _instanceLabel)}");
        sb.AppendLine($"Config schema version: {Config.ConfigSchemaVersion}");
        sb.AppendLine();
        sb.AppendLine("== DPI ==");
        sb.AppendLine($"Window DPI X: {DockMetrics.GetDpiPixelsPerInchX():0.#} (scale {DockMetrics.GetDpiScale():0.###})");
        sb.AppendLine($"Global DIP scale: {DisplayService.GlobalDipScale():0.###}");
        sb.AppendLine();
        sb.AppendLine("== Monitors ==");
        foreach (var m in DisplayService.GetMonitors())
        {
            sb.AppendLine($"- {m.FriendlyName} [{m.DeviceName}]{(m.IsPrimary ? " (primary)" : string.Empty)}");
            sb.AppendLine($"    Bounds px: {m.BoundsPx}");
            sb.AppendLine($"    WorkArea px: {m.WorkAreaPx}");
            sb.AppendLine($"    WorkArea DIP: {m.WorkAreaDip}");
        }

        sb.AppendLine();
        sb.AppendLine("== Active monitor ==");
        sb.AppendLine($"Assigned: {Config.MonitorDeviceName ?? "(primary)"}");
        sb.AppendLine($"Resolved: {_activeMonitor.FriendlyName} [{_activeMonitor.DeviceName}]");
        sb.AppendLine();
        sb.AppendLine("== Dock state ==");
        sb.AppendLine($"Edge: {Config.Edge}");
        sb.AppendLine($"Window: Left={Left:0.#} Top={Top:0.#} Width={ActualWidth:0.#} Height={ActualHeight:0.#}");
        sb.AppendLine($"Content offset: X={DockContentTranslate.X:0.#} Y={DockContentTranslate.Y:0.#}");
        sb.AppendLine($"Work area (positioning): {ActiveWorkArea}");
        sb.AppendLine($"Z-order: AlwaysOnTop={Config.AlwaysOnTop} behindTaskbar={IsBehindTaskbarMode}");
        sb.AppendLine($"Frame (logical): {DockFrameWidth:0.#} x {DockFrameHeight:0.#}");
        sb.AppendLine($"Frame (physical): {DockMetrics.LogicalToPhysical(DockFrameWidth)} x {DockMetrics.LogicalToPhysical(DockFrameHeight)}");
        sb.AppendLine($"AutoHide: {Config.AutoHide} (hidden={_isHiddenByAutohide}, mode={Config.AutoHideRevealMode})");
        sb.AppendLine($"ReserveWorkArea: {Config.ReserveWorkArea} (registered={_appBarRegistered}, edge={_reservedEdge})");
        if (_hasGrantedAppBarRect)
        {
            sb.AppendLine($"AppBar granted px: {_grantedAppBarRect.Left},{_grantedAppBarRect.Top} -> {_grantedAppBarRect.Right},{_grantedAppBarRect.Bottom}");
        }
        sb.AppendLine();
        sb.AppendLine("== Sizes (logical px / real px) ==");
        sb.AppendLine($"Icon size: {Config.IconSize:0} / {DockMetrics.LogicalToPhysical(Config.IconSize)}");
        sb.AppendLine($"High quality icons: {Config.HighQualityIcons}");
        sb.AppendLine($"Icon spacing: {Config.IconSpacing:0} / {DockMetrics.LogicalToPhysical(Config.IconSpacing)}");
        sb.AppendLine($"Dock width: {Config.DockWidth:0} / {DockMetrics.LogicalToPhysical(Config.DockWidth)}");
        sb.AppendLine($"Dock height: {Config.DockHeight:0} / {DockMetrics.LogicalToPhysical(Config.DockHeight)}");
        sb.AppendLine($"Hover radius: {Config.HoverEffectRadius} / {DockMetrics.LogicalToPhysical(Config.HoverEffectRadius)} ({Config.HoverEffectRadiusCm:0.##} cm)");
        sb.AppendLine($"AutoHide proximity: {Config.AutoHideProximityDistance} / {DockMetrics.LogicalToPhysical(Config.AutoHideProximityDistance)} ({Config.AutoHideProximityDistanceCm:0.##} cm)");
        sb.AppendLine($"Items: {Config.Items.Count}");
        return sb.ToString();
    }
}
