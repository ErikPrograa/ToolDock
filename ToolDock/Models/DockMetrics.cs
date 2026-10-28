using System.Windows;

namespace ToolDock.Models;

/// <summary>
/// Central conversion with the logical pixel (DIP at 96 DPI) as the canonical unit, in the style of
/// the Windows taskbar. Sizes are stored in logical pixels and WPF scales them to the monitor DPI;
/// <see cref="LogicalToPhysical"/> gives the real-pixel equivalent for display.
/// </summary>
public static class DockMetrics
{
    /// <summary>Margin along the dock span axis (must match MainWindow).</summary>
    public const double IconMajorAxisPaddingForMetrics = 56;

    private const double LogicalDpi = 96.0;

    /// <summary>Horizontal DPI: supplied by the active window (per monitor), or 96 by default.</summary>
    public static Func<double> GetDpiPixelsPerInchX { get; set; } = () => LogicalDpi;

    /// <summary>
    /// Provider of the work-area span (width/height) in logical pixels for the assigned monitor.
    /// The app sets it for multi-monitor support; if null, the primary monitor is used.
    /// </summary>
    public static Func<DockEdge, double>? WorkAreaSpanLogicalProvider { get; set; }

    /// <summary>Current scale factor (DPI/96).</summary>
    public static double GetDpiScale()
    {
        var dpi = GetDpiPixelsPerInchX();
        if (dpi <= 0)
        {
            dpi = LogicalDpi;
        }

        return dpi / LogicalDpi;
    }

    /// <summary>Converts logical pixels (DIP) to physical pixels on the current monitor.</summary>
    public static int LogicalToPhysical(double logicalPx)
    {
        return Math.Max(0, (int)Math.Round(logicalPx * GetDpiScale(), MidpointRounding.AwayFromZero));
    }

    /// <summary>Same as <see cref="LogicalToPhysical"/> without rounding, for sub-pixel values.</summary>
    public static double LogicalToPhysicalExact(double logicalPx) => Math.Max(0, logicalPx * GetDpiScale());

    /// <summary>Converts physical pixels to logical pixels (DIP).</summary>
    public static double PhysicalToLogical(double physicalPx)
    {
        var scale = GetDpiScale();
        return scale <= 0 ? physicalPx : physicalPx / scale;
    }

    /// <summary>
    /// Returns a logical value that lands exactly on a whole physical pixel, avoiding
    /// fractional pixels and 1px gaps on edges and rounded corners.
    /// </summary>
    public static double SnapLogicalToWholePhysical(double logicalPx)
    {
        var scale = GetDpiScale();
        if (scale <= 0)
        {
            return Math.Round(logicalPx, MidpointRounding.AwayFromZero);
        }

        var physical = Math.Round(logicalPx * scale, MidpointRounding.AwayFromZero);
        return physical / scale;
    }

    /// <summary>Centimeters to logical pixels (DPI-independent; a logical px is defined at 96 DPI).</summary>
    public static double CmToLogical(double cm) => cm / 2.54 * LogicalDpi;

    /// <summary>Logical pixels to centimeters.</summary>
    public static double LogicalToCm(double logicalPx) => logicalPx / LogicalDpi * 2.54;

    /// <summary>100% of the dock span = the full work-area width or height (logical px) of the assigned monitor.</summary>
    public static double GetWorkAreaSpanLogical(DockEdge edge)
    {
        if (WorkAreaSpanLogicalProvider is { } provider)
        {
            try
            {
                return NormalizeSpan(provider(edge));
            }
            catch
            {
                // fallback below
            }
        }

        var wa = SystemParameters.WorkArea;
        var span = edge is DockEdge.Left or DockEdge.Right ? wa.Height : wa.Width;
        return NormalizeSpan(span);
    }

    /// <summary>
    /// At sign-in the work area sometimes arrives as 0. Treating that as "not ready yet"
    /// avoids saving a dock width of 1 px.
    /// </summary>
    private static double NormalizeSpan(double span) =>
        span < 160 ? 16384 : span;

    /// <summary>Span percentage (0-100) from a logical width, clamped to [min, span].</summary>
    public static double LogicalToSpanPercent(double logicalPx, DockEdge edge)
    {
        var span = GetWorkAreaSpanLogical(edge);
        if (span <= 0)
        {
            return 0;
        }

        return Math.Clamp(logicalPx / span * 100.0, 0, 100);
    }

    /// <summary>Logical width from a percentage (0-100) of the assigned monitor's span.</summary>
    public static double SpanPercentToLogical(double percent, DockEdge edge)
    {
        var span = GetWorkAreaSpanLogical(edge);
        return Math.Clamp(percent, 0, 100) / 100.0 * span;
    }
}
