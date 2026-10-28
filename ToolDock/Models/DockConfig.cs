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

    /// <summary>Ruta opcional a un icono personalizado (.ico/.png/.exe/.dll) asignado por el usuario.</summary>
    public string? IconPath { get; set; }

    /// <summary>AppUserModelID para apps UWP/Microsoft Store (cuando <see cref="IsUwp"/> es verdadero).</summary>
    public string? AppUserModelId { get; set; }

    public bool IsUwp { get; set; }
}

/// <summary>Combinación de tecla + modificadores para un hotkey global.</summary>
public sealed class HotkeyConfig
{
    /// <summary>Modificadores MOD_ALT(1)|MOD_CONTROL(2)|MOD_SHIFT(4)|MOD_WIN(8).</summary>
    public int Modifiers { get; set; }

    /// <summary>Virtual-key code (0 = sin asignar).</summary>
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

    /// <summary>True mientras se deserializa. Los setters no deben recortar con datos aún incompletos.</summary>
    public static bool IsLoading => _loadDepth > 0;

    /// <summary>Notificaciones de solo lectura (cm, %) que no deben reescribir el JSON.</summary>
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
    private bool _doNotShowOverFullscreen;
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

    /// <summary>Versión del modelo serializado. ≥3 = unidad canónica píxel lógico.</summary>
    public int ConfigSchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>Preset que la UI debe mostrar: Default, Custom o el nombre de un preset guardado.</summary>
    public string ActivePresetName
    {
        get => _activePresetName;
        set => SetField(ref _activePresetName, NormalizePresetName(value, allowCustom: true));
    }

    /// <summary>Último preset real (no Custom). Restablecer vuelve a este, no a Default por adivinar.</summary>
    public string LastNonCustomPresetName
    {
        get => _lastNonCustomPresetName;
        set => SetField(ref _lastNonCustomPresetName, NormalizePresetName(value, allowCustom: false));
    }

    // ---------------------------------------------------------------------
    // Tamaños en píxeles lógicos (DIP @96). WPF los escala según DPI del monitor.
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

    /// <summary>Ancho del dock expresado como porcentaje del span del monitor (solo presentación/selector de unidad).</summary>
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

    /// <summary>Radio de hover en cm (solo presentación; almacenado en px lógicos).</summary>
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

    /// <summary>Distancia de proximidad en cm (solo presentación; almacenada en px lógicos).</summary>
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

    /// <summary>Separación del dock respecto al borde (px lógicos).</summary>
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

    /// <summary>Radio de las esquinas redondeadas (px lógicos). Solo aplica con <see cref="RoundedCorners"/>.</summary>
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

    /// <summary>Reserva espacio en el área de trabajo (como la barra de tareas). Excluyente con autohide.</summary>
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

    /// <summary>Al abrir un acceso del dock, mueve esa ventana al monitor donde está el dock.</summary>
    public bool OpenAppsOnDockMonitor
    {
        get => _openAppsOnDockMonitor;
        set => SetField(ref _openAppsOnDockMonitor, value);
    }

    /// <summary>
    /// Con autohide: permite ocultar si hay una ventana maximizada en su monitor.
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

    /// <summary>Con autohide: permite ocultar si una ventana se superpone al dock visible.</summary>
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

    /// <summary>Oculta el dock en esta sesión (no se serializa). Al reiniciar vuelve a activo.</summary>
    [JsonIgnore]
    public bool DockDisabled
    {
        get => _dockDisabled;
        set => SetField(ref _dockDisabled, value);
    }

    /// <summary>Bloquea el orden de los accesos del dock (impide reordenar con drag &amp; drop).</summary>
    public bool LockShortcuts
    {
        get => _lockShortcuts;
        set => SetField(ref _lockShortcuts, value);
    }

    /// <summary>
    /// Extrae iconos a 256 px en lugar del tamaño de pantalla. Mejora el hover en algunos
    /// accesos a costa de un poco más de memoria.
    /// </summary>
    public bool HighQualityIcons
    {
        get => _highQualityIcons;
        set => SetField(ref _highQualityIcons, value);
    }

    /// <summary>Nombre del dispositivo de monitor (Screen.DeviceName) asignado, o null para el principal.</summary>
    public string? MonitorDeviceName
    {
        get => _monitorDeviceName;
        set => SetField(ref _monitorDeviceName, string.IsNullOrWhiteSpace(value) ? null : value);
    }

    public HotkeyConfig ShowHideHotkey { get; set; } = new();

    /// <summary>Hotkey que solo muestra el dock (equivale a «Show Dock» del menú contextual).</summary>
    public HotkeyConfig ShowDockHotkey { get; set; } = new();

    /// <summary>Hotkey que activa/desactiva Disable dock (solo esta sesión).</summary>
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
        // Las configuraciones anteriores podían activar ambos modos; conservar intersección.
        if (_hideWhenWindowIntersects)
        {
            HideWhenMaximized = false;
        }
    }

    /// <summary>
    /// Durante los primeros segundos el área de trabajo puede ser falsa (shell aún arrancando,
    /// monitor apagado). No recortar el ancho guardado hasta que quepa o pase el plazo.
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

    /// <summary>Grosor del contorno en px lógicos. Por debajo de 1 el trazo se dibuja con
    /// antialias (cobertura parcial del píxel físico), que es la única forma de bajar de 1 px real.</summary>
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
        DoNotShowOverFullscreen = false;
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

    /// <summary>Recalcula límites/notifica tras cambios de DPI o de configuración de pantalla.</summary>
    public void NotifyShellMetricsChanged()
    {
        ClampDockWidthToCurrentLimits();
        // Las equivalencias en cm/% dependen del DPI/área de trabajo, así que se refrescan
        // explícitamente aunque los valores lógicos no hayan cambiado. No son un cambio de
        // configuración: reescribir el JSON aquí, al iniciar Windows, pisaba el archivo.
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
    // Límites y valores por defecto (todos en píxeles lógicos salvo cm/% indicados).
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
