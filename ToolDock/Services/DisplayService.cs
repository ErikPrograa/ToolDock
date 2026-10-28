using System.Runtime.InteropServices;
using System.Windows;
using ToolDock.Interop;
using ToolDock.Models;

namespace ToolDock.Services;

/// <summary>Monitor information in physical coordinates (px) and logical coordinates (DIP/WPF).</summary>
public sealed class MonitorInfo
{
    public required string DeviceName { get; init; }
    public required string FriendlyName { get; init; }
    public bool IsPrimary { get; init; }
    public Rect BoundsPx { get; init; }
    public Rect WorkAreaPx { get; init; }
    public Rect BoundsDip { get; init; }
    public Rect WorkAreaDip { get; init; }

    /// <summary>Readable label for the monitor picker.</summary>
    public string DisplayLabel => FriendlyName;
}

/// <summary>
/// Enumerates monitors with <c>EnumDisplayMonitors</c> and exposes work areas in DIP,
/// so placement is multi-monitor instead of using only
/// <see cref="SystemParameters.WorkArea"/>.
/// </summary>
public static class DisplayService
{
    /// <summary>Physical/DIP factor of the primary monitor (the base WPF uses for Left/Top).</summary>
    public static double GlobalDipScale()
    {
        try
        {
            var dipWidth = SystemParameters.PrimaryScreenWidth;
            var primaryWidth = GetPrimaryMonitorWidthPx();
            if (primaryWidth > 0 && dipWidth > 0)
            {
                var scale = primaryWidth / dipWidth;
                if (scale > 0)
                {
                    return scale;
                }
            }
        }
        catch
        {
            // fallback below
        }

        return 1.0;
    }

    public static IReadOnlyList<MonitorInfo> GetMonitors()
    {
        var scale = GlobalDipScale();
        var list = new List<MonitorInfo>();
        try
        {
            var names = DisplayConfig.GetFriendlyNamesByGdiDeviceName();
            var index = 1;
            User32.MonitorEnumProc proc = (IntPtr hMonitor, IntPtr hdc, ref User32.RECT rect, IntPtr data) =>
            {
                var info = new User32.MONITORINFOEX { cbSize = Marshal.SizeOf<User32.MONITORINFOEX>() };
                if (!User32.GetMonitorInfo(hMonitor, ref info))
                {
                    return true;
                }

                names.TryGetValue(info.szDevice ?? string.Empty, out var friendly);
                list.Add(BuildMonitorInfo(info, scale, index, friendly));
                index++;
                return true;
            };
            User32.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, proc, IntPtr.Zero);
            GC.KeepAlive(proc);
        }
        catch
        {
            // ignore; empty list handled by callers
        }

        return list;
    }

    public static MonitorInfo GetMonitorForConfig(DockConfig config)
    {
        var monitors = GetMonitors();
        if (monitors.Count == 0)
        {
            return BuildFallbackPrimary();
        }

        if (!string.IsNullOrWhiteSpace(config.MonitorDeviceName))
        {
            var match = monitors.FirstOrDefault(m =>
                string.Equals(m.DeviceName, config.MonitorDeviceName, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match;
            }
        }

        return monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];
    }

    /// <summary>
    /// Returns the same monitor with the work area read again from the system.
    /// After registering an AppBar it must be queried again: the granted rectangle
    /// changes the work area, and the previous enumeration is not enough.
    /// </summary>
    public static MonitorInfo WithFreshWorkArea(MonitorInfo monitor)
    {
        try
        {
            var center = new User32.POINT
            {
                X = (int)Math.Round(monitor.BoundsPx.Left + (monitor.BoundsPx.Width / 2)),
                Y = (int)Math.Round(monitor.BoundsPx.Top + (monitor.BoundsPx.Height / 2))
            };
            var hMonitor = User32.MonitorFromPoint(center, User32.MONITOR_DEFAULTTONEAREST);
            if (hMonitor == IntPtr.Zero)
            {
                return monitor;
            }

            var info = new User32.MONITORINFO { cbSize = Marshal.SizeOf<User32.MONITORINFO>() };
            if (!User32.GetMonitorInfo(hMonitor, ref info))
            {
                return monitor;
            }

            var scale = GlobalDipScale();
            if (scale <= 0)
            {
                scale = 1.0;
            }

            var work = info.rcWork;
            if (work.Width <= 0 || work.Height <= 0)
            {
                return monitor;
            }

            return new MonitorInfo
            {
                DeviceName = monitor.DeviceName,
                FriendlyName = monitor.FriendlyName,
                IsPrimary = monitor.IsPrimary,
                BoundsPx = monitor.BoundsPx,
                BoundsDip = monitor.BoundsDip,
                WorkAreaPx = new Rect(work.Left, work.Top, work.Width, work.Height),
                WorkAreaDip = new Rect(work.Left / scale, work.Top / scale, work.Width / scale, work.Height / scale)
            };
        }
        catch
        {
            return monitor;
        }
    }

    private static double GetPrimaryMonitorWidthPx()
    {
        var width = 0;
        User32.MonitorEnumProc proc = (IntPtr hMonitor, IntPtr hdc, ref User32.RECT rect, IntPtr data) =>
        {
            var info = new User32.MONITORINFOEX { cbSize = Marshal.SizeOf<User32.MONITORINFOEX>() };
            if (User32.GetMonitorInfo(hMonitor, ref info) && (info.dwFlags & User32.MONITORINFOF_PRIMARY) != 0)
            {
                width = info.rcMonitor.Width;
                return false;
            }

            return true;
        };
        User32.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, proc, IntPtr.Zero);
        GC.KeepAlive(proc);
        return width;
    }

    private static MonitorInfo BuildMonitorInfo(User32.MONITORINFOEX info, double scale, int index, string? monitorName)
    {
        if (scale <= 0)
        {
            scale = 1.0;
        }

        var bounds = info.rcMonitor;
        var work = info.rcWork;
        var isPrimary = (info.dwFlags & User32.MONITORINFOF_PRIMARY) != 0;
        var name = string.IsNullOrWhiteSpace(monitorName) ? string.Empty : $"{monitorName} — ";
        return new MonitorInfo
        {
            DeviceName = info.szDevice ?? string.Empty,
            FriendlyName = $"{index}: {name}{bounds.Width}×{bounds.Height}" + (isPrimary ? " ★" : string.Empty),
            IsPrimary = isPrimary,
            BoundsPx = new Rect(bounds.Left, bounds.Top, bounds.Width, bounds.Height),
            WorkAreaPx = new Rect(work.Left, work.Top, work.Width, work.Height),
            BoundsDip = new Rect(bounds.Left / scale, bounds.Top / scale, bounds.Width / scale, bounds.Height / scale),
            WorkAreaDip = new Rect(work.Left / scale, work.Top / scale, work.Width / scale, work.Height / scale)
        };
    }

    private static MonitorInfo BuildFallbackPrimary()
    {
        var wa = SystemParameters.WorkArea;
        var fullW = SystemParameters.PrimaryScreenWidth;
        var fullH = SystemParameters.PrimaryScreenHeight;
        return new MonitorInfo
        {
            DeviceName = "PRIMARY",
            FriendlyName = "1",
            IsPrimary = true,
            BoundsPx = new Rect(0, 0, fullW, fullH),
            WorkAreaPx = wa,
            BoundsDip = new Rect(0, 0, fullW, fullH),
            WorkAreaDip = wa
        };
    }
}
