using System.Windows;

namespace ToolDock.Models;

/// <summary>
/// Conversión centralizada con el píxel lógico (DIP @96 DPI) como unidad canónica, al estilo de la
/// barra de tareas de Windows. Los tamaños se guardan en píxeles lógicos y WPF los escala al DPI del
/// monitor; <see cref="LogicalToPhysical"/> da el equivalente en píxeles reales para mostrarlo.
/// </summary>
public static class DockMetrics
{
    /// <summary>Margen en el eje del span del dock (debe coincidir con MainWindow).</summary>
    public const double IconMajorAxisPaddingForMetrics = 56;

    private const double LogicalDpi = 96.0;

    /// <summary>DPI horizontal: lo provee la ventana activa (por monitor) o 96 por defecto.</summary>
    public static Func<double> GetDpiPixelsPerInchX { get; set; } = () => LogicalDpi;

    /// <summary>
    /// Proveedor del span (ancho/alto) del área de trabajo en píxeles lógicos del monitor asignado.
    /// Lo establece la app para soporte multi-monitor; si es null se usa el monitor principal.
    /// </summary>
    public static Func<DockEdge, double>? WorkAreaSpanLogicalProvider { get; set; }

    /// <summary>Factor de escala actual (DPI/96).</summary>
    public static double GetDpiScale()
    {
        var dpi = GetDpiPixelsPerInchX();
        if (dpi <= 0)
        {
            dpi = LogicalDpi;
        }

        return dpi / LogicalDpi;
    }

    /// <summary>Convierte píxeles lógicos (DIP) a píxeles físicos del monitor actual.</summary>
    public static int LogicalToPhysical(double logicalPx)
    {
        return Math.Max(0, (int)Math.Round(logicalPx * GetDpiScale(), MidpointRounding.AwayFromZero));
    }

    /// <summary>Igual que <see cref="LogicalToPhysical"/> sin redondear, para valores sub-píxel.</summary>
    public static double LogicalToPhysicalExact(double logicalPx) => Math.Max(0, logicalPx * GetDpiScale());

    /// <summary>Convierte píxeles físicos a píxeles lógicos (DIP).</summary>
    public static double PhysicalToLogical(double physicalPx)
    {
        var scale = GetDpiScale();
        return scale <= 0 ? physicalPx : physicalPx / scale;
    }

    /// <summary>
    /// Devuelve un valor lógico que cae exactamente sobre un píxel físico entero, evitando
    /// fracciones de píxel y los huecos de 1px en bordes/redondeos.
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

    /// <summary>Centímetros → píxeles lógicos (independiente del DPI; el px lógico se define a 96 DPI).</summary>
    public static double CmToLogical(double cm) => cm / 2.54 * LogicalDpi;

    /// <summary>Píxeles lógicos → centímetros.</summary>
    public static double LogicalToCm(double logicalPx) => logicalPx / LogicalDpi * 2.54;

    /// <summary>100% del span del dock = todo el ancho o alto del área de trabajo (en px lógicos) del monitor asignado.</summary>
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
    /// Al iniciar sesión el área de trabajo a veces llega como 0. Tratar eso como «aún no está
    /// lista» evita guardar un ancho de dock de 1 px.
    /// </summary>
    private static double NormalizeSpan(double span) =>
        span < 160 ? 16384 : span;

    /// <summary>Porcentaje del span (0-100) a partir de un ancho lógico, acotado a [min, span].</summary>
    public static double LogicalToSpanPercent(double logicalPx, DockEdge edge)
    {
        var span = GetWorkAreaSpanLogical(edge);
        if (span <= 0)
        {
            return 0;
        }

        return Math.Clamp(logicalPx / span * 100.0, 0, 100);
    }

    /// <summary>Ancho lógico a partir de un porcentaje (0-100) del span del monitor asignado.</summary>
    public static double SpanPercentToLogical(double percent, DockEdge edge)
    {
        var span = GetWorkAreaSpanLogical(edge);
        return Math.Clamp(percent, 0, 100) / 100.0 * span;
    }
}
