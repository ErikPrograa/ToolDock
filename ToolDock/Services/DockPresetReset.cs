using ToolDock.Models;

namespace ToolDock.Services;

/// <summary>
/// Aplica el preset "Default" al <see cref="DockConfig"/> como el botón «Restablecer todo a predeterminado» en Configuración.
/// </summary>
public static class DockPresetReset
{
    public static void ApplyDefaultPreset(DockConfig config)
    {
        var data = SanitizePresetDataColors(DockPresetData.CreateDefault());
        data.ApplyTo(config);
        config.ActivePresetName = DockConfig.DefaultPresetName;
        config.LastNonCustomPresetName = DockConfig.DefaultPresetName;
    }

    private static DockPresetData SanitizePresetDataColors(DockPresetData source)
    {
        var available = GetAvailableColorHexes();
        string Resolve(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex))
            {
                return "#FFFFFF";
            }

            var normalized = NormalizeHex(hex);
            return available.Contains(normalized) ? normalized : "#FFFFFF";
        }

        return new DockPresetData
        {
            Edge = source.Edge,
            AutoAdjustDockWidth = source.AutoAdjustDockWidth,
            DockWidth = source.DockWidth,
            DockHeight = source.DockHeight,
            Offset = source.Offset,
            AlignmentOffset = source.AlignmentOffset,
            DockOpacity = source.DockOpacity,
            RoundedCorners = source.RoundedCorners,
            CornerRadius = source.CornerRadius,
            RoundedCornerTopLeft = source.RoundedCornerTopLeft,
            RoundedCornerTopRight = source.RoundedCornerTopRight,
            RoundedCornerBottomLeft = source.RoundedCornerBottomLeft,
            RoundedCornerBottomRight = source.RoundedCornerBottomRight,
            SolidColorHex = Resolve(source.SolidColorHex),
            ShowOutline = source.ShowOutline,
            OutlineEdgeTop = source.OutlineEdgeTop,
            OutlineEdgeBottom = source.OutlineEdgeBottom,
            OutlineEdgeLeft = source.OutlineEdgeLeft,
            OutlineEdgeRight = source.OutlineEdgeRight,
            OutlineColorHex = Resolve(source.OutlineColorHex),
            OutlineOpacity = source.OutlineOpacity,
            OutlineThickness = source.OutlineThickness,
            IconSize = source.IconSize,
            IconSpacing = source.IconSpacing,
            HighQualityIcons = source.HighQualityIcons,
            ButtonAlignment = source.ButtonAlignment,
            EnableHoverEffects = source.EnableHoverEffects,
            HoverMagnification = source.HoverMagnification,
            HoverEffectRadius = source.HoverEffectRadius,
            AutoHide = source.AutoHide,
            AutoHideSlideAnimation = source.AutoHideSlideAnimation,
            AutoHideSlideDurationMs = source.AutoHideSlideDurationMs,
            AutoHideRevealDelayMs = source.AutoHideRevealDelayMs,
            AutoHideHideDelayMs = source.AutoHideHideDelayMs,
            AutoHideRevealMode = source.AutoHideRevealMode,
            AutoHideProximityDistance = source.AutoHideProximityDistance,
            AlwaysOnTop = source.AlwaysOnTop,
            HideBehindTaskbar = source.HideBehindTaskbar,
            ReserveWorkArea = source.ReserveWorkArea,
            DoNotShowOverFullscreen = source.DoNotShowOverFullscreen,
            OpenAppsOnDockMonitor = source.OpenAppsOnDockMonitor,
            HideWhenMaximized = source.HideWhenMaximized,
            HideWhenWindowIntersects = source.HideWhenWindowIntersects,
            DataSchemaVersion = source.DataSchemaVersion
        };
    }

    private static HashSet<string> GetAvailableColorHexes()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var hex in GetBuiltInHexes())
        {
            set.Add(NormalizeHex(hex));
        }

        foreach (var c in new CustomColorService().LoadCustomColors())
        {
            set.Add(NormalizeHex(c.Hex));
        }

        return set;
    }

    private static IEnumerable<string> GetBuiltInHexes()
    {
        return
        [
            "#000000",
            "#FFFFFF",
            "#1F1F1F",
            "#E5E5E5",
            "#0078D4",
            "#202020",
            "#F3F3F3",
            "#00B7C3",
            "#8764B8",
            "#107C10",
            "#D83B01"
        ];
    }

    private static string NormalizeHex(string value)
    {
        var hex = value.Trim().ToUpperInvariant();
        if (!hex.StartsWith('#'))
        {
            hex = $"#{hex}";
        }

        return hex;
    }
}
