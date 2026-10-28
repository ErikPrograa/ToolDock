using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ToolDock.Models;

public enum DockEdge
{
    Top,
    Bottom,
    Left,
    Right
}

public enum ButtonAlignment
{
    Start,
    Center,
    End
}

public enum AppTheme
{
    Light,
    Dark
}

public enum AppLanguage
{
    Spanish,
    English
}

public enum AutoHideRevealMode
{
    EdgeTouch,
    Proximity
}

public sealed class DockItemConfig
{
    public string Path { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Optional path to a custom icon (.ico/.png/.exe/.dll) chosen by the user.</summary>
    public string? IconPath { get; set; }

    /// <summary>AppUserModelID for UWP/Microsoft Store apps (when <see cref="IsUwp"/> is true).</summary>
    public string? AppUserModelId { get; set; }

    public bool IsUwp { get; set; }
}

/// <summary>Key plus modifiers for a global hotkey.</summary>
public sealed class HotkeyConfig
{
    /// <summary>Modificadores MOD_ALT(1)|MOD_CONTROL(2)|MOD_SHIFT(4)|MOD_WIN(8).</summary>
    public int Modifiers { get; set; }

    /// <summary>Virtual-key code (0 = unassigned).</summary>
    public int Key { get; set; }

    [JsonIgnore]
    public bool IsAssigned => Key != 0;
}

public sealed class DockConfig : INotifyPropertyChanged
{
    public const int CurrentSchemaVersion = 3;
    public const string DefaultPresetName = "Default";
    public const string CustomPresetName = "Custom";

    private static int _loadDepth;
    private bool _raisingShellMetricRefresh;

    /// <summary>True while deserializing. Setters must not clamp against still-incomplete data.</summary>
    public static bool IsLoading => _loadDepth > 0;

    /// <summary>Read-only notifications (cm, %) that must not rewrite the JSON.</summary>
    public bool IsRaisingShellMetricRefresh => _raisingShellMetricRefresh;

    public static void BeginLoad() => _loadDepth++;

    public static void EndLoad()
    {
        if (_loadDepth > 0)
        {
            _loadDepth--;
        }
    }

    private const double DefaultDockWidthLogicalPx = 990d;
    private const int DockEdgeInset = 11;
    private const int MinDockItemsForWidth = 4;

    private const int IconSizeMin = 16;
    private const int IconSizeMax = 32;
    private const int IconSizeDefault = 24;
    private const int IconSpacingMin = 5;
    private const int IconSpacingMax = 44;
    private const int IconSpacingDefault = 19;
    private const int DockHeightMin = 32;
    private const int DockHeightMax = 64;
    private const int DockHeightDefault = 48;
    private const int HoverRadiusMin = 20;
    private const int HoverRadiusHardMax = 400;
    private const int HoverRadiusDefault = 40;
    private const int AutoHideProximityDistanceMin = 1;
    private const int AutoHideProximityDistanceMax = 400;
    private const int AutoHideProximityDistanceDefault = 60;
    private const int CornerRadiusMin = 0;
    private const int CornerRadiusMax = 32;
    private const int CornerRadiusDefault = 18;
    private const double OutlineThicknessMin = 0.25;
    private const double OutlineThicknessMax = 6;
    private const double OutlineThicknessDefault = 1;

    private int _dockOpacity = 60;
    private bool _enableHoverEffects = true;
    private int _hoverMagnification = 90;
    private ButtonAlignment _buttonAlignment = ButtonAlignment.Center;
    private bool _roundedCorners = true;
    private double _cornerRadius = CornerRadiusDefault;
    private bool _roundedCornerTopLeft = true;
    private bool _roundedCornerTopRight = true;
    private bool _roundedCornerBottomLeft = true;
    private bool _roundedCornerBottomRight = true;
    private DockEdge _edge = DockEdge.Top;
    private double _offset;
    private double _alignmentOffset;
    private bool _autoHide;
    private bool _autoHideSlideAnimation = true;
    private int _autoHideSlideDurationMs = 150;
    private int _autoHideRevealDelayMs = 150;
    private int _autoHideHideDelayMs = 250;
    private AutoHideRevealMode _autoHideRevealMode = AutoHideRevealMode.Proximity;
    private int _autoHideProximityDistance = AutoHideProximityDistanceDefault;
    private bool _alwaysOnTop = true;
    private bool _hideBehindTaskbar;
    private bool _reserveWorkArea;
    private bool _doNotShowOverFullscreen = true;
    private bool _openAppsOnDockMonitor;
    private bool _hideWhenMaximized;
    private bool _hideWhenWindowIntersects;
    private bool _dockDisabled;
    private bool _lockShortcuts;
    private bool _highQualityIcons;
    private string? _monitorDeviceName;
    private double _settingsWindowWidth = 850;
    private double _settingsWindowHeight = 700;
    private AppTheme _theme = AppTheme.Dark;
    private AppLanguage _language = AppLanguage.English;
    private string _solidColorHex = "#1F1F1F";
    private bool _showOutline = true;
    private bool _outlineEdgeTop = true;
    private bool _outlineEdgeBottom = true;
    private bool _outlineEdgeLeft = true;
    private bool _outlineEdgeRight = true;
    private string _outlineColorHex = "#FFFFFF";
    private int _outlineOpacity = 20;
    private double _outlineThickness = OutlineThicknessDefault;

    private double _iconSize = IconSizeDefault;
    private double _iconSpacing = IconSpacingDefault;
    private double _dockHeight = DockHeightDefault;
    private double _dockWidth = DefaultDockWidthLogicalPx;
    private int _hoverEffectRadius = HoverRadiusDefault;
    private bool _autoAdjustDockWidth;
    private string _activePresetName = DefaultPresetName;
    private string _lastNonCustomPresetName = DefaultPresetName;
    private double _startupDockWidth = DefaultDockWidthLogicalPx;
    private DateTime _deferNarrowDockWidthClampUntilUtc = DateTime.MinValue;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Serialized model version. ≥3 = logical pixel as the canonical unit.</summary>
    public int ConfigSchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>Preset the UI should show: Default, Custom, or the name of a saved preset.</summary>
    public string ActivePresetName
    {
        get => _activePresetName;
        set => SetField(ref _activePresetName, NormalizePresetName(value, allowCustom: true));
    }

    /// <summary>Last real preset (not Custom). Reset returns to this one, not to a guessed Default.</summary>
    public string LastNonCustomPresetName
    {
        get => _lastNonCustomPresetName;
        set => SetField(ref _lastNonCustomPresetName, NormalizePresetName(value, allowCustom: false));
    }

    // ---------------------------------------------------------------------
    // Sizes in logical pixels (DIP at 96). WPF scales them with the monitor DPI.
    // ---------------------------------------------------------------------

    public double IconSize
    {
        get => _iconSize;
        set
        {
            var clamped = Math.Clamp(Math.Round(value), GetIconSizeMin(), GetIconSizeMax());
            if (!SetField(ref _iconSize, clamped))
            {
                return;
            }

            if (!IsLoading)
            {
                ClampDockWidthToCurrentLimits();
            }
        }
    }

    public double IconSpacing
    {
        get => _iconSpacing;
        set
        {
            var clamped = Math.Clamp(Math.Round(value), GetIconSpacingMin(), GetIconSpacingMax());
            if (!SetField(ref _iconSpacing, clamped))
            {
                return;
            }

            if (!IsLoading)
            {
                ClampDockWidthToCurrentLimits();
            }
        }
    }

    public double DockHeight
    {
        get => _dockHeight;
        set => SetField(ref _dockHeight, Math.Clamp(Math.Round(value), GetDockHeightMin(), GetDockHeightMax()));
    }

    public double DockWidth
    {
        get => _dockWidth;
        set
        {
            if (IsLoading)
            {
                SetField(ref _dockWidth, Math.Clamp(value, 1, 20000));
                return;
            }

            var minPx = GetDockWidthMin(_iconSize, _iconSpacing, Items.Count);
            var maxPx = GetDockWidthMax(_edge);
            if (minPx > maxPx)
            {
                minPx = maxPx;
            }

            var clamped = _autoAdjustDockWidth ? minPx : Math.Clamp(value, minPx, maxPx);
            if (!SetField(ref _dockWidth, clamped))
            {
                return;
            }

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DockWidthPercent)));
        }
    }

    /// <summary>Dock width as a percentage of the monitor span (presentation and unit picker only).</summary>
    [JsonIgnore]
    public double DockWidthPercent
    {
        get => DockMetrics.LogicalToSpanPercent(_dockWidth, _edge);
        set => DockWidth = DockMetrics.SpanPercentToLogical(value, _edge);
    }

    public bool AutoAdjustDockWidth
    {
        get => _autoAdjustDockWidth;
        set
        {
            if (!SetField(ref _autoAdjustDockWidth, value))
            {
                return;
            }

            if (!IsLoading)
            {
                ClampDockWidthToCurrentLimits();
            }
        }
    }

    public void SyncDockWidthLimits() => ClampDockWidthToCurrentLimits();

    public int HoverEffectRadius
    {
        get => _hoverEffectRadius;
        set
        {
            var clamped = Math.Clamp(value, GetHoverRadiusMin(), GetHoverRadiusHardMax());
            if (!SetField(ref _hoverEffectRadius, clamped))
            {
                return;
            }

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HoverEffectRadiusCm)));
        }
    }

    /// <summary>Hover radius in cm (presentation only; stored in logical px).</summary>
    [JsonIgnore]
    public double HoverEffectRadiusCm
    {
        get => DockMetrics.LogicalToCm(_hoverEffectRadius);
        set => HoverEffectRadius = (int)Math.Round(DockMetrics.CmToLogical(value));
    }

    public int AutoHideProximityDistance
    {
        get => _autoHideProximityDistance;
        set
        {
            var clamped = Math.Clamp(value, GetAutoHideProximityDistanceMin(), GetAutoHideProximityDistanceMax());
            if (!SetField(ref _autoHideProximityDistance, clamped))
            {
                return;
            }

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AutoHideProximityDistanceCm)));
        }
    }

    /// <summary>Proximity distance in cm (presentation only; stored in logical px).</summary>
    [JsonIgnore]
    public double AutoHideProximityDistanceCm
    {
        get => DockMetrics.LogicalToCm(_autoHideProximityDistance);
        set => AutoHideProximityDistance = (int)Math.Round(DockMetrics.CmToLogical(value));
    }

    public double Offset
    {
        get => _offset;
        set => SetField(ref _offset, Math.Clamp(Math.Round(value), -4000, 4000));
    }

    /// <summary>Gap between the dock and the screen edge (logical px).</summary>
    public double AlignmentOffset
    {
        get => _alignmentOffset;
        set => SetField(ref _alignmentOffset, Math.Clamp(Math.Round(value), 0, 2000));
    }

    public int DockOpacity
    {
        get => _dockOpacity;
        set => SetField(ref _dockOpacity, Math.Clamp(value, 0, 100));
    }

    public bool EnableHoverEffects
    {
        get => _enableHoverEffects;
        set => SetField(ref _enableHoverEffects, value);
    }

    public int HoverMagnification
    {
        get => _hoverMagnification;
        set => SetField(ref _hoverMagnification, Math.Clamp(value, 0, 150));
    }

    public ButtonAlignment ButtonAlignment
    {
        get => _buttonAlignment;
        set => SetField(ref _buttonAlignment, value);
    }

    public bool RoundedCorners
    {
        get => _roundedCorners;
        set => SetField(ref _roundedCorners, value);
    }

    /// <summary>Rounded-corner radius (logical px). Applies only when <see cref="RoundedCorners"/> is on.</summary>
    public double CornerRadius
    {
        get => _cornerRadius;
        set => SetField(ref _cornerRadius, Math.Clamp(Math.Round(value), GetCornerRadiusMin(), GetCornerRadiusMax()));
    }

    public bool RoundedCornerTopLeft
    {
        get => _roundedCornerTopLeft;
        set => SetField(ref _roundedCornerTopLeft, value);
    }

    public bool RoundedCornerTopRight
    {
        get => _roundedCornerTopRight;
        set => SetField(ref _roundedCornerTopRight, value);
    }

    public bool RoundedCornerBottomLeft
    {
        get => _roundedCornerBottomLeft;
        set => SetField(ref _roundedCornerBottomLeft, value);
    }

    public bool RoundedCornerBottomRight
    {
        get => _roundedCornerBottomRight;
        set => SetField(ref _roundedCornerBottomRight, value);
    }

    public DockEdge Edge
    {
        get => _edge;
        set
        {
            if (_edge == value)
            {
                return;
            }

            _edge = value;
            if (!IsLoading)
            {
                ClampDockWidthToCurrentLimits();
            }

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Edge)));
        }
    }

    public bool AutoHide
    {
        get => _autoHide;
        set
        {
            if (!SetField(ref _autoHide, value))
            {
                return;
            }

            ClearHideBehindTaskbarIfDisabled();
        }
    }

    public bool AutoHideSlideAnimation
    {
        get => _autoHideSlideAnimation;
        set => SetField(ref _autoHideSlideAnimation, value);
    }

    public int AutoHideSlideDurationMs
    {
        get => _autoHideSlideDurationMs;
        set => SetField(ref _autoHideSlideDurationMs, Math.Clamp(value, 0, 1200));
    }

    public int AutoHideRevealDelayMs
    {
        get => _autoHideRevealDelayMs;
        set => SetField(ref _autoHideRevealDelayMs, Math.Clamp(value, 0, 4000));
    }

    public int AutoHideHideDelayMs
    {
        get => _autoHideHideDelayMs;
        set => SetField(ref _autoHideHideDelayMs, Math.Clamp(value, 0, 4000));
    }

    public AutoHideRevealMode AutoHideRevealMode
    {
        get => _autoHideRevealMode;
        set => SetField(ref _autoHideRevealMode, value);
    }

    public bool AlwaysOnTop
    {
        get => _alwaysOnTop;
        set
        {
            if (!SetField(ref _alwaysOnTop, value))
            {
                return;
            }

            ClearHideBehindTaskbarIfDisabled();
        }
    }

    public bool HideBehindTaskbar
    {
        get => _hideBehindTaskbar;
        set
        {
            if (!IsLoading && value && (!_autoHide || !_alwaysOnTop))
            {
                return;
            }

            SetField(ref _hideBehindTaskbar, value);
        }
    }

    /// <summary>Reserves work-area space (like the taskbar). Mutually exclusive with autohide.</summary>
    public bool ReserveWorkArea
    {
        get => _reserveWorkArea;
        set
        {
            if (!IsLoading && value && _autoHide)
            {
                return;
            }

            SetField(ref _reserveWorkArea, value);
        }
    }

    public bool DoNotShowOverFullscreen
    {
        get => _doNotShowOverFullscreen;
        set => SetField(ref _doNotShowOverFullscreen, value);
    }

    /// <summary>When a dock shortcut opens, moves that window to the monitor where the dock is.</summary>
    public bool OpenAppsOnDockMonitor
    {
        get => _openAppsOnDockMonitor;
        set => SetField(ref _openAppsOnDockMonitor, value);
    }

    /// <summary>
    /// With autohide: allows hiding when a window is maximized on its monitor.
    /// </summary>
    public bool HideWhenMaximized
    {
        get => _hideWhenMaximized;
        set
        {
            if (!IsLoading && value && !_autoHide)
            {
                return;
            }

            if (!IsLoading && value)
            {
                HideWhenWindowIntersects = false;
            }

            SetField(ref _hideWhenMaximized, value);
        }
    }

    /// <summary>With autohide: allows hiding when a window overlaps the visible dock.</summary>
    public bool HideWhenWindowIntersects
    {
        get => _hideWhenWindowIntersects;
        set
        {
            if (!IsLoading && value && !_autoHide)
            {
                return;
            }

            if (!IsLoading && value)
            {
                HideWhenMaximized = false;
            }

            SetField(ref _hideWhenWindowIntersects, value);
        }
    }

    /// <summary>Hides the dock for this session (not serialized). It is active again after restart.</summary>
    [JsonIgnore]
    public bool DockDisabled
    {
        get => _dockDisabled;
        set => SetField(ref _dockDisabled, value);
    }

    /// <summary>Locks dock shortcut order (blocks reordering by drag and drop).</summary>
    public bool LockShortcuts
    {
        get => _lockShortcuts;
        set => SetField(ref _lockShortcuts, value);
    }

    /// <summary>
    /// Extracts icons at 256 px instead of the on-screen size. Improves hover on some
    /// shortcuts at the cost of a little more memory.
    /// </summary>
    public bool HighQualityIcons
    {
        get => _highQualityIcons;
        set => SetField(ref _highQualityIcons, value);
    }

    /// <summary>Assigned monitor device name (Screen.DeviceName), or null for the primary monitor.</summary>
    public string? MonitorDeviceName
    {
        get => _monitorDeviceName;
        set => SetField(ref _monitorDeviceName, string.IsNullOrWhiteSpace(value) ? null : value);
    }

    public HotkeyConfig ShowHideHotkey { get; set; } = new();

    /// <summary>Hotkey that only shows the dock (same as «Show Dock» in the context menu).</summary>
    public HotkeyConfig ShowDockHotkey { get; set; } = new();

    /// <summary>Hotkey that toggles Disable dock (this session only).</summary>
    public HotkeyConfig DisableDockHotkey { get; set; } = new();

    private void ClearHideBehindTaskbarIfDisabled()
    {
        if (IsLoading)
        {
            return;
        }

        if (_hideBehindTaskbar && (!_autoHide || !_alwaysOnTop))
        {
            SetField(ref _hideBehindTaskbar, false, nameof(HideBehindTaskbar));
        }

        if (_hideWhenMaximized && !_autoHide)
        {
            SetField(ref _hideWhenMaximized, false, nameof(HideWhenMaximized));
        }

        if (_hideWhenWindowIntersects && !_autoHide)
        {
            SetField(ref _hideWhenWindowIntersects, false, nameof(HideWhenWindowIntersects));
        }

        if (_reserveWorkArea && _autoHide)
        {
            SetField(ref _reserveWorkArea, false, nameof(ReserveWorkArea));
        }
    }

    private static string NormalizePresetName(string? value, bool allowCustom)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return DefaultPresetName;
        }

        var trimmed = value.Trim();
        if (string.Equals(trimmed, DefaultPresetName, StringComparison.OrdinalIgnoreCase))
        {
            return DefaultPresetName;
        }

        if (string.Equals(trimmed, CustomPresetName, StringComparison.OrdinalIgnoreCase))
        {
            return allowCustom ? CustomPresetName : DefaultPresetName;
        }

        return trimmed;
    }

    public void CoerceAfterLoad()
    {
        ActivePresetName = _activePresetName;
        LastNonCustomPresetName = _lastNonCustomPresetName;
        ClearHideBehindTaskbarIfDisabled();
        // Older configs could enable both modes; keep intersection.
        if (_hideWhenWindowIntersects)
        {
            HideWhenMaximized = false;
        }
    }

    /// <summary>
    /// For the first few seconds the work area can be wrong (shell still starting,
    /// monitor off). Do not clamp the saved width until it fits or the grace period ends.
    /// </summary>
    public void ArmStartupWidthGuard()
    {
        _startupDockWidth = _dockWidth;
        _deferNarrowDockWidthClampUntilUtc = DateTime.UtcNow.AddSeconds(12);
    }

    public double SettingsWindowWidth
    {
        get => _settingsWindowWidth;
        set => SetField(ref _settingsWindowWidth, Math.Clamp(value, 620, 2200));
    }

    public double SettingsWindowHeight
    {
        get => _settingsWindowHeight;
        set => SetField(ref _settingsWindowHeight, Math.Clamp(value, 460, 1600));
    }

    public AppTheme Theme
    {
        get => _theme;
        set => SetField(ref _theme, value);
    }

    public AppLanguage Language
    {
        get => _language;
        set => SetField(ref _language, value);
    }

    public string SolidColorHex
    {
        get => _solidColorHex;
        set
        {
            var normalized = NormalizeColorInput(value, _solidColorHex);
            if (!IsValidHexColor(normalized))
            {
                return;
            }

            SetField(ref _solidColorHex, normalized);
        }
    }

    public bool ShowOutline
    {
        get => _showOutline;
        set => SetField(ref _showOutline, value);
    }

    public bool OutlineEdgeTop
    {
        get => _outlineEdgeTop;
        set => SetField(ref _outlineEdgeTop, value);
    }

    public bool OutlineEdgeBottom
    {
        get => _outlineEdgeBottom;
        set => SetField(ref _outlineEdgeBottom, value);
    }

    public bool OutlineEdgeLeft
    {
        get => _outlineEdgeLeft;
        set => SetField(ref _outlineEdgeLeft, value);
    }

    public bool OutlineEdgeRight
    {
        get => _outlineEdgeRight;
        set => SetField(ref _outlineEdgeRight, value);
    }

    public string OutlineColorHex
    {
        get => _outlineColorHex;
        set
        {
            var normalized = NormalizeColorInput(value, _outlineColorHex);
            if (!IsValidHexColor(normalized))
            {
                return;
            }

            SetField(ref _outlineColorHex, normalized);
        }
    }

    public int OutlineOpacity
    {
        get => _outlineOpacity;
        set => SetField(ref _outlineOpacity, Math.Clamp(value, 0, 100));
    }

    /// <summary>Outline thickness in logical px. Below 1 the stroke is drawn with
    /// antialiasing (partial coverage of the physical pixel), the only way to go under 1 real px.</summary>
    public double OutlineThickness
    {
        get => _outlineThickness;
        set => SetField(ref _outlineThickness, Math.Clamp(Math.Round(value, 2), GetOutlineThicknessMin(), GetOutlineThicknessMax()));
    }

    public ObservableCollection<DockItemConfig> Items { get; set; } = [];

    public ObservableCollection<string> QuickAccessPinnedOptions { get; set; } = [];

    public void ResetToDefaults()
    {
        Edge = DockEdge.Top;
        ButtonAlignment = ButtonAlignment.Center;
        ResetPosition();
        ResetAppearance();
        ResetShortcuts();
        ResetBehaviour();
        ResetGeneral();
    }

    public void ResetGeneral()
    {
        Theme = AppTheme.Dark;
        Language = AppLanguage.English;
    }

    public void ResetPosition()
    {
        AutoAdjustDockWidth = false;
        DockWidth = GetDefaultDockWidthLogical(_edge);
        DockHeight = GetDockHeightDefault();
        Offset = 0;
        AlignmentOffset = 0;
    }

    public void ResetAppearance()
    {
        DockOpacity = 60;
        RoundedCorners = true;
        CornerRadius = GetCornerRadiusDefault();
        RoundedCornerTopLeft = true;
        RoundedCornerTopRight = true;
        RoundedCornerBottomLeft = true;
        RoundedCornerBottomRight = true;
        SolidColorHex = "#1F1F1F";
        ShowOutline = true;
        OutlineEdgeTop = true;
        OutlineEdgeBottom = true;
        OutlineEdgeLeft = true;
        OutlineEdgeRight = true;
        OutlineColorHex = "#FFFFFF";
        OutlineOpacity = 20;
        OutlineThickness = GetOutlineThicknessDefault();
    }

    public void ResetShortcuts()
    {
        IconSize = GetIconSizeDefault();
        IconSpacing = GetIconSpacingDefault();
        EnableHoverEffects = true;
        HoverMagnification = 90;
        HoverEffectRadius = GetHoverRadiusDefault();
        HighQualityIcons = false;
    }

    public void ResetBehaviour()
    {
        AutoHide = false;
        AutoHideSlideAnimation = true;
        AutoHideSlideDurationMs = 150;
        AutoHideRevealDelayMs = 150;
        AutoHideHideDelayMs = 250;
        AutoHideRevealMode = AutoHideRevealMode.Proximity;
        AutoHideProximityDistance = GetAutoHideProximityDistanceDefault();
        AlwaysOnTop = true;
        HideBehindTaskbar = false;
        ReserveWorkArea = false;
        DoNotShowOverFullscreen = true;
        OpenAppsOnDockMonitor = false;
        HideWhenMaximized = false;
        HideWhenWindowIntersects = false;
    }

    private static string NormalizeColorInput(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var hex = value.Trim().ToUpperInvariant();
        if (!hex.StartsWith('#'))
        {
            hex = $"#{hex}";
        }

        return hex;
    }

    private static bool IsValidHexColor(string value)
    {
        return Regex.IsMatch(value, "^#([0-9A-F]{6}|[0-9A-F]{3})$");
    }

    private bool SetField<T>(ref T backingField, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(backingField, value))
        {
            return false;
        }

        backingField = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    /// <summary>Recomputes limits and notifies after a DPI or display-configuration change.</summary>
    public void NotifyShellMetricsChanged()
    {
        ClampDockWidthToCurrentLimits();
        // cm/% equivalents depend on DPI and the work area, so they refresh even when the
        // logical values did not change. They are not a configuration change: rewriting the
        // JSON here, at Windows startup, used to overwrite the file.
        _raisingShellMetricRefresh = true;
        try
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HoverEffectRadiusCm)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AutoHideProximityDistanceCm)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DockWidthPercent)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IconSize)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IconSpacing)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DockHeight)));
        }
        finally
        {
            _raisingShellMetricRefresh = false;
        }
    }

    private void ClampDockWidthToCurrentLimits()
    {
        if (IsLoading)
        {
            return;
        }

        var maxPx = GetDockWidthMax(_edge);
        if (DateTime.UtcNow < _deferNarrowDockWidthClampUntilUtc && maxPx + 1 < _startupDockWidth)
        {
            return;
        }

        var minPx = GetDockWidthMin(_iconSize, _iconSpacing, Items.Count);
        if (minPx > maxPx)
        {
            minPx = maxPx;
        }

        var newWidth = _autoAdjustDockWidth ? minPx : Math.Clamp(_dockWidth, minPx, maxPx);
        if (!SetField(ref _dockWidth, newWidth, nameof(DockWidth)))
        {
            return;
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DockWidthPercent)));
    }

    // ---------------------------------------------------------------------
    // Limits and defaults (all in logical pixels except where cm/% is noted).
    // ---------------------------------------------------------------------

    public static double GetDockWidthMin(double iconSize, double iconSpacing) =>
        GetDockWidthMin(iconSize, iconSpacing, MinDockItemsForWidth);

    public static double GetDockWidthMin(double iconSize, double iconSpacing, int itemCount)
    {
        var effectiveCount = Math.Max(MinDockItemsForWidth, itemCount);
        return (effectiveCount * iconSize) + ((effectiveCount - 1) * iconSpacing) + (GetDockEdgeInset() * 2);
    }

    public static double GetDockWidthMax(DockEdge edge) => DockMetrics.GetWorkAreaSpanLogical(edge);

    public static int GetDockEdgeInset() => DockEdgeInset;
    public static int GetIconSizeMin() => IconSizeMin;
    public static int GetIconSizeMax() => IconSizeMax;
    public static int GetIconSizeDefault() => IconSizeDefault;
    public static int GetIconSpacingMin() => IconSpacingMin;
    public static int GetIconSpacingMax() => IconSpacingMax;
    public static int GetIconSpacingDefault() => IconSpacingDefault;
    public static int GetDockHeightMin() => DockHeightMin;
    public static int GetDockHeightMax() => DockHeightMax;
    public static int GetDockHeightDefault() => DockHeightDefault;
    public static int GetHoverRadiusMin() => HoverRadiusMin;
    public static int GetHoverRadiusHardMax() => HoverRadiusHardMax;
    public static int GetHoverRadiusDefault() => HoverRadiusDefault;
    public static int GetAutoHideProximityDistanceMin() => AutoHideProximityDistanceMin;
    public static int GetAutoHideProximityDistanceMax() => AutoHideProximityDistanceMax;
    public static int GetAutoHideProximityDistanceDefault() => AutoHideProximityDistanceDefault;
    public static int GetCornerRadiusMin() => CornerRadiusMin;
    public static int GetCornerRadiusMax() => CornerRadiusMax;
    public static int GetCornerRadiusDefault() => CornerRadiusDefault;
    public static double GetOutlineThicknessMin() => OutlineThicknessMin;
    public static double GetOutlineThicknessMax() => OutlineThicknessMax;
    public static double GetOutlineThicknessDefault() => OutlineThicknessDefault;

    public static double GetDefaultDockWidthLogical(DockEdge edge)
    {
        var minPx = GetDockWidthMin(GetIconSizeDefault(), GetIconSpacingDefault(), MinDockItemsForWidth);
        var maxPx = GetDockWidthMax(edge);
        if (minPx > maxPx)
        {
            minPx = maxPx;
        }

        return Math.Clamp(DefaultDockWidthLogicalPx, minPx, maxPx);
    }

    public static double GetDefaultHoverEffectRadiusCm() => DockMetrics.LogicalToCm(HoverRadiusDefault);

    public static double GetDefaultAutoHideProximityDistanceCm() => DockMetrics.LogicalToCm(AutoHideProximityDistanceDefault);
}
