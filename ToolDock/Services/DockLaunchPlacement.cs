using System.Windows.Threading;
using ToolDock.Interop;

namespace ToolDock.Services;

/// <summary>
/// After launching a shortcut, moves the new window (or the one that comes to the foreground) to the dock's monitor.
/// Windows picks the monitor when it creates the process; there is no reliable way to request it ahead of time, especially for UWP.
/// </summary>
internal static class DockLaunchPlacement
{
    private const int WatchMs = 5000;
    private const int TickMs = 200;
    private const int MaxMovesPerWindow = 2;

    private static int _generation;
    private static DispatcherTimer? _timer;
    private static DateTime _deadlineUtc;
    private static IntPtr _dockHwnd;
    private static IntPtr _targetMonitor;
    private static HashSet<IntPtr> _existing = [];
    private static readonly Dictionary<IntPtr, int> Moves = new();
    private static IntPtr _foregroundAtStart;
    private static IntPtr _watchedForeground;

    public static void Begin(IntPtr dockHwnd)
    {
        if (dockHwnd == IntPtr.Zero)
        {
            return;
        }

        var monitor = User32.MonitorFromWindow(dockHwnd, User32.MONITOR_DEFAULTTONEAREST);
        if (monitor == IntPtr.Zero || !TryGetWorkArea(monitor, out _))
        {
            return;
        }

        _generation++;
        _dockHwnd = dockHwnd;
        _targetMonitor = monitor;
        _existing = SnapshotTopLevelWindows();
        Moves.Clear();
        _foregroundAtStart = User32.GetForegroundWindow();
        _watchedForeground = IntPtr.Zero;
        _deadlineUtc = DateTime.UtcNow.AddMilliseconds(WatchMs);

        _timer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(TickMs) };
        _timer.Tick -= OnTick;
        _timer.Tick += OnTick;
        _timer.Start();
    }

    private static void OnTick(object? sender, EventArgs e)
    {
        var generation = _generation;
        try
        {
            if (DateTime.UtcNow >= _deadlineUtc || _targetMonitor == IntPtr.Zero)
            {
                StopIfCurrent(generation);
                return;
            }

            ConsiderForeground();
            foreach (var hwnd in SnapshotTopLevelWindows())
            {
                if (_existing.Contains(hwnd))
                {
                    continue;
                }

                TryMove(hwnd);
            }
        }
        catch
        {
            StopIfCurrent(generation);
        }
    }

    private static void ConsiderForeground()
    {
        if (_watchedForeground != IntPtr.Zero)
        {
            TryMove(_watchedForeground);
            return;
        }

        var foreground = User32.GetForegroundWindow();
        if (foreground == IntPtr.Zero || foreground == _foregroundAtStart || !IsMovableAppWindow(foreground))
        {
            return;
        }

        _watchedForeground = foreground;
        TryMove(foreground);
    }

    private static void TryMove(IntPtr hwnd)
    {
        if (!IsMovableAppWindow(hwnd))
        {
            return;
        }

        if (Moves.TryGetValue(hwnd, out var count) && count >= MaxMovesPerWindow)
        {
            return;
        }

        if (User32.MonitorFromWindow(hwnd, User32.MONITOR_DEFAULTTONEAREST) == _targetMonitor)
        {
            return;
        }

        if (!TryGetWorkArea(_targetMonitor, out var targetWork))
        {
            return;
        }

        var maximized = (User32.GetWindowLong(hwnd, User32.GWL_STYLE) & User32.WS_MAXIMIZE) != 0;
        if (maximized || User32.IsIconic(hwnd))
        {
            User32.ShowWindow(hwnd, User32.SW_RESTORE);
        }

        if (!User32.GetWindowRect(hwnd, out var rect) || rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        var source = User32.MonitorFromWindow(hwnd, User32.MONITOR_DEFAULTTONEAREST);
        if (source == _targetMonitor)
        {
            if (maximized)
            {
                User32.ShowWindow(hwnd, User32.SW_MAXIMIZE);
            }

            return;
        }

        var x = rect.Left;
        var y = rect.Top;
        if (source != IntPtr.Zero && TryGetWorkArea(source, out var sourceWork))
        {
            x += targetWork.Left - sourceWork.Left;
            y += targetWork.Top - sourceWork.Top;
        }
        else
        {
            x = targetWork.Left;
            y = targetWork.Top;
        }

        ClampInto(ref x, ref y, rect.Width, rect.Height, targetWork);
        User32.SetWindowPos(hwnd, User32.HWND_TOP, x, y, rect.Width, rect.Height, User32.SWP_SHOWWINDOW);
        if (maximized)
        {
            User32.ShowWindow(hwnd, User32.SW_MAXIMIZE);
        }

        Moves[hwnd] = count + 1;
    }

    private static bool IsMovableAppWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || hwnd == _dockHwnd || !User32.IsWindow(hwnd))
        {
            return false;
        }

        if (!User32.IsWindowVisible(hwnd) || User32.IsWindowCloaked(hwnd))
        {
            return false;
        }

        if (User32.GetWindow(hwnd, User32.GW_OWNER) != IntPtr.Zero)
        {
            return false;
        }

        var processId = Environment.ProcessId;
        User32.GetWindowThreadProcessId(hwnd, out var windowProcess);
        if (windowProcess == (uint)processId)
        {
            return false;
        }

        var exStyle = User32.GetWindowLong(hwnd, User32.GWL_EXSTYLE);
        if ((exStyle & User32.WS_EX_TOOLWINDOW) != 0 && (exStyle & User32.WS_EX_APPWINDOW) == 0)
        {
            return false;
        }

        if (IsShellWindowClass(hwnd))
        {
            return false;
        }

        if (!User32.GetWindowRect(hwnd, out var rect))
        {
            return false;
        }

        return rect.Width >= 80 && rect.Height >= 80;
    }

    private static bool IsShellWindowClass(IntPtr hwnd)
    {
        var buffer = new System.Text.StringBuilder(64);
        if (User32.GetClassName(hwnd, buffer, buffer.Capacity) == 0)
        {
            return false;
        }

        return buffer.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";
    }

    private static HashSet<IntPtr> SnapshotTopLevelWindows()
    {
        var windows = new HashSet<IntPtr>();
        User32.EnumWindows((hwnd, _) =>
        {
            if (User32.IsWindowVisible(hwnd) && !User32.IsWindowCloaked(hwnd))
            {
                windows.Add(hwnd);
            }

            return true;
        }, IntPtr.Zero);
        return windows;
    }

    private static bool TryGetWorkArea(IntPtr monitor, out User32.RECT work)
    {
        var info = new User32.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<User32.MONITORINFO>() };
        if (!User32.GetMonitorInfo(monitor, ref info))
        {
            work = default;
            return false;
        }

        work = info.rcWork;
        return work.Width > 0 && work.Height > 0;
    }

    private static void ClampInto(ref int x, ref int y, int width, int height, User32.RECT work)
    {
        if (width >= work.Width)
        {
            x = work.Left;
        }
        else
        {
            if (x < work.Left)
            {
                x = work.Left;
            }

            if (x + width > work.Right)
            {
                x = work.Right - width;
            }
        }

        if (height >= work.Height)
        {
            y = work.Top;
        }
        else
        {
            if (y < work.Top)
            {
                y = work.Top;
            }

            if (y + height > work.Bottom)
            {
                y = work.Bottom - height;
            }
        }
    }

    private static void StopIfCurrent(int generation)
    {
        if (generation != _generation)
        {
            return;
        }

        _timer?.Stop();
    }
}
