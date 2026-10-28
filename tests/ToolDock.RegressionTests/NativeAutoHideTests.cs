using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ToolDock;
using ToolDock.Models;
using ToolDock.Services;

internal static class NativeAutoHideTests
{
    private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
    private const int VisiblePopup = unchecked((int)0x90000000);

    public static void Run()
    {
        var directory = Directory.CreateTempSubdirectory("ToolDock-native-tests-");
        var previousSpan = DockMetrics.WorkAreaSpanLogicalProvider;
        var config = new DockConfig { AutoHide = true, AutoHideSlideAnimation = false, DockDisabled = true };
        var dock = new MainWindow(new ConfigService(Path.Combine(directory.FullName, "config.json")), config, "test");
        try
        {
            Check((bool)Call(dock, "ShouldAutohideHideForWindows"), "No conditions retain ordinary autohide");
            var self = new WindowInteropHelper(dock).EnsureHandle();
            dock.Loaded -= (RoutedEventHandler)Delegate.CreateDelegate(typeof(RoutedEventHandler), dock,
                typeof(MainWindow).GetMethod("Window_Loaded", PrivateInstance)!);
            HwndSource.FromHwnd(self)!.RootVisual = dock;
            CheckWindowFilters(dock, config, self);
            CheckDockGeometry(dock, config);
            Check(!dock.IsVisible, "The test never displays the dock");
        }
        finally
        {
            foreach (var field in typeof(MainWindow).GetFields(PrivateInstance))
            {
                if (field.GetValue(dock) is DispatcherTimer timer)
                {
                    timer.Stop();
                }
            }
            dock.Close();
            DockMetrics.WorkAreaSpanLogicalProvider = previousSpan;
            directory.Delete(recursive: true);
        }
    }

    private static void CheckWindowFilters(MainWindow dock, DockConfig config, IntPtr self)
    {
        var monitors = DisplayService.GetMonitors();
        // Exercise real HWNDs without displaying anything on the user's desktop or taking focus.
        using var candidate = new HwndSource(new HwndSourceParameters("ToolDock regression fixture")
        {
            PositionX = (int)monitors.Max(m => m.BoundsPx.Right) + 1000,
            PositionY = (int)monitors.Max(m => m.BoundsPx.Bottom) + 1000,
            Width = 200,
            Height = 120,
            WindowStyle = VisiblePopup,
            ExtendedWindowStyle = 0x08000000 // WS_EX_NOACTIVATE
        });
        var hwnd = candidate.Handle;
        Check(GetWindowRect(hwnd, out var rect), "Fixture rectangle is available");
        var bounds = new Rect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
        var overlap = new Rect(bounds.Left + 10, bounds.Top + 10, 20, 20);
        var monitor = MonitorFromWindow(hwnd, 2);
        bool Requires(Rect area, IntPtr? activeMonitor = null, IntPtr? dockHandle = null) =>
            (bool)Call(dock, "WindowRequiresAutohide", hwnd, dockHandle ?? self, activeMonitor ?? monitor, area);

        config.HideWhenWindowIntersects = true;
        Check(Requires(overlap), "A normal, non-maximized window intersecting the dock triggers hide");
        Check(Requires(new Rect(bounds.Left - 10, bounds.Top - 10, bounds.Width + 20, bounds.Height + 20)),
            "Window contained by the dock intersects");
        Check(Requires(new Rect(bounds.Right - 1, bounds.Bottom - 1, 10, 10)), "One pixel of overlap intersects");
        foreach (var outside in new[]
        {
            new Rect(bounds.Right, bounds.Top, 20, 20),
            new Rect(bounds.Left - 20, bounds.Top, 20, 20),
            new Rect(bounds.Left, bounds.Bottom, 20, 20),
            new Rect(bounds.Left, bounds.Top - 20, 20, 20),
            new Rect(bounds.Right + 100, bounds.Bottom + 100, 20, 20)
        })
        {
            Check(!Requires(outside), "Contact without overlapping area must not hide the dock");
        }
        Check(!Requires(Rect.Empty), "An empty dock rectangle does not intersect");
        Check(!Requires(overlap, dockHandle: hwnd), "The dock itself is excluded");
        Check(Requires(overlap, activeMonitor: IntPtr.Zero), "Intersections are not filtered by a window's primary monitor");

        SetWindowLong(hwnd, -16, VisiblePopup & ~0x10000000);
        Check(!Requires(overlap), "Invisible windows are excluded");
        SetWindowLong(hwnd, -16, VisiblePopup);
        SetWindowLong(hwnd, -20, 0x08000080);
        Check(!Requires(overlap), "Own tool windows and popups are excluded");
        SetWindowLong(hwnd, -20, 0x08000000);

        config.HideWhenWindowIntersects = false;
        config.HideWhenMaximized = true;
        Check(!Requires(overlap), "Maximized-only mode ignores a restored overlapping window");
        SetWindowLong(hwnd, -16, VisiblePopup | 0x01000000);
        Check(Requires(Rect.Empty), "Maximized-only mode retains same-monitor detection");
        Check(!Requires(overlap, activeMonitor: IntPtr.Zero), "Maximized-only mode excludes another monitor");
        config.HideWhenWindowIntersects = true;
        Check(!config.HideWhenMaximized, "Intersection replaces maximized mode");
        Check(Requires(overlap, activeMonitor: IntPtr.Zero), "An overlapping maximized window qualifies by intersection");
        Check(!Requires(Rect.Empty), "A maximized window without overlap does not hide in intersection mode");
        SetWindowLong(hwnd, -16, VisiblePopup);
        Check(Requires(overlap), "Intersection mode accepts an overlapping non-maximized window");
        Check(!Requires(Rect.Empty), "No intersection means remain visible");
        SetWindowLong(hwnd, -16, VisiblePopup | 0x20000000);
        Check(!Requires(overlap), "Minimized windows are excluded");

        var shell = GetShellWindow();
        if (shell != IntPtr.Zero)
        {
            Check(!(bool)Call(dock, "WindowRequiresAutohide", shell, self, monitor,
                new Rect(-100000, -100000, 200000, 200000)), "The desktop is excluded");
        }
    }

    private static void CheckDockGeometry(MainWindow dock, DockConfig config)
    {
        config.HideWhenMaximized = false;
        config.AutoHideProximityDistance = 1;
        config.HoverEffectRadius = 400;
        var hiddenField = typeof(MainWindow).GetField("_isHiddenByAutohide", PrivateInstance)!;
        var translation = (TranslateTransform)dock.FindName("DockContentTranslate");

        foreach (var edge in Enum.GetValues<DockEdge>())
        {
            config.Edge = edge;
            config.Offset = 37;
            config.AlignmentOffset = 19;
            hiddenField.SetValue(dock, false);
            Call(dock, "PositionDockWindow", false);
            var visibleBounds = (Rect)Call(dock, "GetVisibleDockBounds");
            Check(!visibleBounds.IsEmpty && visibleBounds.Width > 0 && visibleBounds.Height > 0, $"{edge}: visible frame has area");

            var monitor = (MonitorInfo)typeof(MainWindow).GetField("_activeMonitor", PrivateInstance)!.GetValue(dock)!;
            Check(monitor.BoundsPx.Contains(visibleBounds), $"{edge}: frame stays on the assigned monitor");
            var frameOffsetX = Math.Max(0, (dock.Width - dock.DockFrameWidth) / 2);
            var frameOffsetY = Math.Max(0, (dock.Height - dock.DockFrameHeight) / 2);
            var origin = new Point(frameOffsetX + translation.X, frameOffsetY + translation.Y);
            var rendered = new Rect(dock.PointToScreen(origin),
                dock.PointToScreen(new Point(origin.X + dock.DockFrameWidth, origin.Y + dock.DockFrameHeight)));
            rendered.Intersect(monitor.BoundsPx);
            Check(rendered == visibleBounds, $"{edge}: detection measures the rendered frame, not the padded WPF window");

            hiddenField.SetValue(dock, true);
            Call(dock, "PositionDockWindow", false);
            Check((Rect)Call(dock, "GetVisibleDockBounds") == visibleBounds, $"{edge}: hiding does not change intersection bounds");
            translation.X += 23;
            translation.Y -= 17;
            Check((Rect)Call(dock, "GetVisibleDockBounds") == visibleBounds, $"{edge}: intermediate animation offsets do not change bounds");

            hiddenField.SetValue(dock, false);
            config.Offset = 0;
            config.AlignmentOffset = 0;
            Call(dock, "PositionDockWindow", false);
            var work = monitor.WorkAreaDip;
            var inside = edge switch
            {
                DockEdge.Top => new Point(work.Left + work.Width / 2, work.Top + 0.5),
                DockEdge.Bottom => new Point(work.Left + work.Width / 2, work.Bottom - 0.5),
                DockEdge.Left => new Point(work.Left + 0.5, work.Top + work.Height / 2),
                _ => new Point(work.Right - 0.5, work.Top + work.Height / 2)
            };
            var outside = inside + (edge switch
            {
                DockEdge.Top => new Vector(0, 2),
                DockEdge.Bottom => new Vector(0, -2),
                DockEdge.Left => new Vector(2, 0),
                _ => new Vector(-2, 0)
            });
            Check((bool)Call(dock, "IsPointerInDockEdgeRegion", inside, 1d), $"{edge}: one-DIP activation can reveal");
            Check(!(bool)Call(dock, "IsPointerInDockEdgeRegion", outside, 1d), $"{edge}: small activation distance remains narrow");

            config.Offset = 4000;
            config.AlignmentOffset = 2000;
            Check(monitor.BoundsPx.Contains((Rect)Call(dock, "GetVisibleDockBounds")), $"{edge}: extreme offsets remain clipped");
        }
    }

    private static object Call(MainWindow dock, string method, params object[] arguments) =>
        typeof(MainWindow).GetMethod(method, PrivateInstance)!.Invoke(dock, arguments)!;

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();
}
