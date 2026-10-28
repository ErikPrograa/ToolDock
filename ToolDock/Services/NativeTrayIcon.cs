using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using ToolDock.Interop;

namespace ToolDock.Services;

/// <summary>
/// Tray icon via <c>Shell_NotifyIcon</c>, without pulling in WinForms.
/// </summary>
internal sealed class NativeTrayIcon : IDisposable
{
    private const uint NimAdd = 0x00000000;
    private const uint NimModify = 0x00000001;
    private const uint NimDelete = 0x00000002;
    private const uint NifMessage = 0x00000001;
    private const uint NifIcon = 0x00000002;
    private const uint NifTip = 0x00000004;
    private const uint TrayCallbackMessage = 0x8001; // WM_APP + 1
    private const int WmLButtonUp = 0x0202;
    private const int WmRButtonUp = 0x0205;
    private const int WmLButtonDblClk = 0x0203;
    private const int WmContextMenu = 0x007B;
    private const uint TrayIconId = 1;

    private readonly HwndSource _source;
    private readonly DispatcherTimer _retryTimer;
    private readonly uint _taskbarCreatedMessage;
    private NOTIFYICONDATA _data;
    private bool _added;
    private bool _disposed;
    private bool _ownsIcon;
    private string _toolTip;

    public event Action? LeftClick;
    public event Action? RightClick;

    public NativeTrayIcon(string toolTip)
    {
        _toolTip = TruncateTip(toolTip);
        var parameters = new HwndSourceParameters("ToolDockNotifyIcon")
        {
            Width = 1,
            Height = 1,
            PositionX = -32000,
            PositionY = -32000,
            WindowStyle = unchecked((int)0x80000000) // WS_POPUP
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
        _taskbarCreatedMessage = User32.RegisterWindowMessage("TaskbarCreated");

        var (trayIcon, ownsIcon) = AppIcons.LoadTrayIcon();
        _ownsIcon = ownsIcon;
        _data = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _source.Handle,
            uID = TrayIconId,
            uFlags = NifMessage | NifIcon | NifTip,
            uCallbackMessage = TrayCallbackMessage,
            hIcon = trayIcon,
            szTip = _toolTip
        };

        _retryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _retryTimer.Tick += RetryTimer_Tick;
        if (!TryAdd())
        {
            _retryTimer.Start();
        }
    }

    public string ToolTip
    {
        get => _toolTip;
        set
        {
            _toolTip = TruncateTip(value);
            if (!_added)
            {
                _data.szTip = _toolTip;
                return;
            }

            _data.szTip = _toolTip;
            _data.uFlags = NifTip;
            Shell_NotifyIcon(NimModify, ref _data);
            _data.uFlags = NifMessage | NifIcon | NifTip;
        }
    }

    public void ShowContextMenu(ContextMenu menu)
    {
        if (!User32.GetCursorPos(out var pt))
        {
            return;
        }

        var scale = Math.Max(0.25, DisplayService.GlobalDipScale());
        var helper = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = System.Windows.Media.Brushes.Transparent,
            ShowInTaskbar = false,
            ShowActivated = true,
            Topmost = true,
            Width = 1,
            Height = 1,
            Left = 0,
            Top = 0,
            ResizeMode = ResizeMode.NoResize
        };

        void Cleanup(object? sender, RoutedEventArgs e)
        {
            menu.Closed -= Cleanup;
            helper.Close();
        }

        helper.Loaded += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(helper).Handle;
            if (hwnd != IntPtr.Zero)
            {
                User32.SetForegroundWindow(hwnd);
            }

            menu.PlacementTarget = helper;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.AbsolutePoint;
            menu.HorizontalOffset = pt.X / scale;
            menu.VerticalOffset = pt.Y / scale;
            menu.StaysOpen = false;
            menu.Closed += Cleanup;
            menu.IsOpen = true;
        };

        helper.Show();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _retryTimer.Stop();
        _retryTimer.Tick -= RetryTimer_Tick;
        if (_added)
        {
            Shell_NotifyIcon(NimDelete, ref _data);
            _added = false;
        }

        if (_ownsIcon && _data.hIcon != IntPtr.Zero)
        {
            User32.DestroyIcon(_data.hIcon);
            _data.hIcon = IntPtr.Zero;
            _ownsIcon = false;
        }

        _source.RemoveHook(WndProc);
        _source.Dispose();
    }

    private void RetryTimer_Tick(object? sender, EventArgs e)
    {
        if (TryAdd())
        {
            _retryTimer.Stop();
        }
    }

    private bool TryAdd()
    {
        _data.uFlags = NifMessage | NifIcon | NifTip;
        _data.szTip = _toolTip;
        if (!Shell_NotifyIcon(NimAdd, ref _data))
        {
            return false;
        }

        _added = true;
        return true;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == (int)_taskbarCreatedMessage)
        {
            _added = false;
            TryAdd();
            handled = true;
            return IntPtr.Zero;
        }

        if (msg != TrayCallbackMessage)
        {
            return IntPtr.Zero;
        }

        var mouseMsg = lParam.ToInt32() & 0xFFFF;
        if (mouseMsg is WmLButtonUp or WmLButtonDblClk)
        {
            LeftClick?.Invoke();
            handled = true;
        }
        else if (mouseMsg is WmRButtonUp or WmContextMenu)
        {
            RightClick?.Invoke();
            handled = true;
        }

        return IntPtr.Zero;
    }

    private static string TruncateTip(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "ToolDock";
        }

        return value.Length <= 127 ? value : value[..127];
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }
}
