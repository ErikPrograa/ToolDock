namespace ToolDock.Models;

public sealed class DockPreset
{
    public string Name { get; set; } = "Default";
    public DockPresetData Data { get; set; } = new();
}

/// <summary>
/// Datos de un preset. Todas las medidas se almacenan en píxeles lógicos (DIP @96), salvo
/// <see cref="HoverMagnification"/> (porcentaje) y opacidades (porcentaje).
/// </summary>
public sealed class DockPresetData
{
    public int DataSchemaVersion { get; set; } = DockConfig.CurrentSchemaVersion;

    public DockEdge Edge { get; set; } = DockEdge.Top;

    public bool AutoAdjustDockWidth { get; set; }

    public double DockWidth { get; set; }
    public double DockHeight { get; set; }
    public double Offset { get; set; }
    public double AlignmentOffset { get; set; }

    public int DockOpacity { get; set; } = 60;
    public bool RoundedCorners { get; set; } = true;
    public double CornerRadius { get; set; } = 18;
    public bool RoundedCornerTopLeft { get; set; } = true;
    public bool RoundedCornerTopRight { get; set; } = true;
    public bool RoundedCornerBottomLeft { get; set; } = true;
    public bool RoundedCornerBottomRight { get; set; } = true;
    public string SolidColorHex { get; set; } = "#1F1F1F";
    public bool ShowOutline { get; set; } = true;
    public bool OutlineEdgeTop { get; set; } = true;
    public bool OutlineEdgeBottom { get; set; } = true;
    public bool OutlineEdgeLeft { get; set; } = true;
    public bool OutlineEdgeRight { get; set; } = true;
    public string OutlineColorHex { get; set; } = "#FFFFFF";
    public int OutlineOpacity { get; set; } = 20;
    public double OutlineThickness { get; set; } = 1;

    public double IconSize { get; set; }
    public double IconSpacing { get; set; }
    public bool HighQualityIcons { get; set; }
    public ButtonAlignment ButtonAlignment { get; set; } = ButtonAlignment.Center;
    public bool EnableHoverEffects { get; set; } = true;
    public int HoverMagnification { get; set; } = 90;
    public int HoverEffectRadius { get; set; }

    public bool AutoHide { get; set; }
    public bool AutoHideSlideAnimation { get; set; } = true;
    public int AutoHideSlideDurationMs { get; set; } = 150;
    public int AutoHideRevealDelayMs { get; set; } = 150;
    public int AutoHideHideDelayMs { get; set; } = 250;
    public AutoHideRevealMode AutoHideRevealMode { get; set; } = AutoHideRevealMode.Proximity;
    public int AutoHideProximityDistance { get; set; }

    public bool AlwaysOnTop { get; set; } = true;
    public bool HideBehindTaskbar { get; set; }
    public bool ReserveWorkArea { get; set; }
    public bool DoNotShowOverFullscreen { get; set; }
    public bool OpenAppsOnDockMonitor { get; set; }
    public bool HideWhenMaximized { get; set; }
    public bool HideWhenWindowIntersects { get; set; }

    public static DockPresetData CreateDefault()
    {
        return new DockPresetData
        {
            Edge = DockEdge.Top,
            AutoAdjustDockWidth = false,
            DockWidth = DockConfig.GetDefaultDockWidthLogical(DockEdge.Top),
            DockHeight = DockConfig.GetDockHeightDefault(),
            Offset = 0,
            AlignmentOffset = 0,
            DockOpacity = 60,
            RoundedCorners = true,
            CornerRadius = DockConfig.GetCornerRadiusDefault(),
            RoundedCornerTopLeft = true,
            RoundedCornerTopRight = true,
            RoundedCornerBottomLeft = true,
            RoundedCornerBottomRight = true,
            SolidColorHex = "#1F1F1F",
            ShowOutline = true,
            OutlineEdgeTop = true,
            OutlineEdgeBottom = true,
            OutlineEdgeLeft = true,
            OutlineEdgeRight = true,
            OutlineColorHex = "#FFFFFF",
            OutlineOpacity = 20,
            OutlineThickness = DockConfig.GetOutlineThicknessDefault(),
            IconSize = DockConfig.GetIconSizeDefault(),
            IconSpacing = DockConfig.GetIconSpacingDefault(),
            HighQualityIcons = false,
            ButtonAlignment = ButtonAlignment.Center,
            EnableHoverEffects = true,
            HoverMagnification = 90,
            HoverEffectRadius = DockConfig.GetHoverRadiusDefault(),
            AutoHide = false,
            AutoHideSlideAnimation = true,
            AutoHideSlideDurationMs = 150,
            AutoHideRevealDelayMs = 150,
            AutoHideHideDelayMs = 250,
            AutoHideRevealMode = AutoHideRevealMode.Proximity,
            AutoHideProximityDistance = DockConfig.GetAutoHideProximityDistanceDefault(),
            AlwaysOnTop = true,
            HideBehindTaskbar = false,
            ReserveWorkArea = false,
            DoNotShowOverFullscreen = false,
            OpenAppsOnDockMonitor = false,
            HideWhenMaximized = false,
            HideWhenWindowIntersects = false,
            DataSchemaVersion = DockConfig.CurrentSchemaVersion
        };
    }

    public static DockPresetData FromConfig(DockConfig config)
    {
        return new DockPresetData
        {
            Edge = config.Edge,
            AutoAdjustDockWidth = config.AutoAdjustDockWidth,
            DockWidth = config.DockWidth,
            DockHeight = config.DockHeight,
            Offset = config.Offset,
            AlignmentOffset = config.AlignmentOffset,
            DockOpacity = config.DockOpacity,
            RoundedCorners = config.RoundedCorners,
            CornerRadius = config.CornerRadius,
            RoundedCornerTopLeft = config.RoundedCornerTopLeft,
            RoundedCornerTopRight = config.RoundedCornerTopRight,
            RoundedCornerBottomLeft = config.RoundedCornerBottomLeft,
            RoundedCornerBottomRight = config.RoundedCornerBottomRight,
            SolidColorHex = config.SolidColorHex,
            ShowOutline = config.ShowOutline,
            OutlineEdgeTop = config.OutlineEdgeTop,
            OutlineEdgeBottom = config.OutlineEdgeBottom,
            OutlineEdgeLeft = config.OutlineEdgeLeft,
            OutlineEdgeRight = config.OutlineEdgeRight,
            OutlineColorHex = config.OutlineColorHex,
            OutlineOpacity = config.OutlineOpacity,
            OutlineThickness = config.OutlineThickness,
            IconSize = config.IconSize,
            IconSpacing = config.IconSpacing,
            HighQualityIcons = config.HighQualityIcons,
            ButtonAlignment = config.ButtonAlignment,
            EnableHoverEffects = config.EnableHoverEffects,
            HoverMagnification = config.HoverMagnification,
            HoverEffectRadius = config.HoverEffectRadius,
            AutoHide = config.AutoHide,
            AutoHideSlideAnimation = config.AutoHideSlideAnimation,
            AutoHideSlideDurationMs = config.AutoHideSlideDurationMs,
            AutoHideRevealDelayMs = config.AutoHideRevealDelayMs,
            AutoHideHideDelayMs = config.AutoHideHideDelayMs,
            AutoHideRevealMode = config.AutoHideRevealMode,
            AutoHideProximityDistance = config.AutoHideProximityDistance,
            AlwaysOnTop = config.AlwaysOnTop,
            HideBehindTaskbar = config.HideBehindTaskbar,
            ReserveWorkArea = config.ReserveWorkArea,
            DoNotShowOverFullscreen = config.DoNotShowOverFullscreen,
            OpenAppsOnDockMonitor = config.OpenAppsOnDockMonitor,
            HideWhenMaximized = config.HideWhenMaximized,
            HideWhenWindowIntersects = config.HideWhenWindowIntersects,
            DataSchemaVersion = DockConfig.CurrentSchemaVersion
        };
    }

    public void ApplyTo(DockConfig config)
    {
        config.Edge = Edge;
        config.AutoAdjustDockWidth = AutoAdjustDockWidth;
        if (!AutoAdjustDockWidth)
        {
            config.DockWidth = DockWidth;
        }
        else
        {
            config.SyncDockWidthLimits();
        }

        config.DockHeight = DockHeight;
        config.Offset = Offset;
        config.AlignmentOffset = AlignmentOffset;
        config.DockOpacity = DockOpacity;
        config.RoundedCorners = RoundedCorners;
        config.CornerRadius = CornerRadius;
        config.RoundedCornerTopLeft = RoundedCornerTopLeft;
        config.RoundedCornerTopRight = RoundedCornerTopRight;
        config.RoundedCornerBottomLeft = RoundedCornerBottomLeft;
        config.RoundedCornerBottomRight = RoundedCornerBottomRight;
        config.SolidColorHex = SolidColorHex;
        config.ShowOutline = ShowOutline;
        config.OutlineEdgeTop = OutlineEdgeTop;
        config.OutlineEdgeBottom = OutlineEdgeBottom;
        config.OutlineEdgeLeft = OutlineEdgeLeft;
        config.OutlineEdgeRight = OutlineEdgeRight;
        config.OutlineColorHex = OutlineColorHex;
        config.OutlineOpacity = OutlineOpacity;
        config.OutlineThickness = OutlineThickness;
        config.IconSize = IconSize;
        config.IconSpacing = IconSpacing;
        config.HighQualityIcons = HighQualityIcons;
        config.ButtonAlignment = ButtonAlignment;
        config.EnableHoverEffects = EnableHoverEffects;
        config.HoverMagnification = HoverMagnification;
        config.HoverEffectRadius = HoverEffectRadius;
        config.AutoHide = AutoHide;
        config.AutoHideSlideAnimation = AutoHideSlideAnimation;
        config.AutoHideSlideDurationMs = AutoHideSlideDurationMs;
        config.AutoHideRevealDelayMs = AutoHideRevealDelayMs;
        config.AutoHideHideDelayMs = AutoHideHideDelayMs;
        config.AutoHideRevealMode = AutoHideRevealMode;
        config.AutoHideProximityDistance = AutoHideProximityDistance;
        config.AlwaysOnTop = AlwaysOnTop;
        config.HideBehindTaskbar = HideBehindTaskbar;
        config.ReserveWorkArea = ReserveWorkArea;
        config.DoNotShowOverFullscreen = DoNotShowOverFullscreen;
        config.OpenAppsOnDockMonitor = OpenAppsOnDockMonitor;
        config.HideWhenMaximized = HideWhenMaximized;
        config.HideWhenWindowIntersects = HideWhenWindowIntersects;
    }
}

public sealed class NamedHexColor
{
    public string Name { get; set; } = string.Empty;
    public string Hex { get; set; } = "#FFFFFF";
    public bool IsBuiltIn { get; set; }
}
