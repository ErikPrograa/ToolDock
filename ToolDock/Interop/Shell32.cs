using System.Runtime.InteropServices;

namespace ToolDock.Interop;

/// <summary>Interop for reserving work-area space through an AppBar (taskbar style).</summary>
internal static class Shell32
{
    public const int ABM_NEW = 0x00000000;
    public const int ABM_REMOVE = 0x00000001;
    public const int ABM_QUERYPOS = 0x00000002;
    public const int ABM_SETPOS = 0x00000003;

    public const int ABE_LEFT = 0;
    public const int ABE_TOP = 1;
    public const int ABE_RIGHT = 2;
    public const int ABE_BOTTOM = 3;

    [StructLayout(LayoutKind.Sequential)]
    public struct APPBARDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public int uEdge;
        public User32.RECT rc;
        public int lParam;
    }

    [DllImport("shell32.dll", SetLastError = true)]
    public static extern uint SHAppBarMessage(int dwMessage, ref APPBARDATA pData);

    /// <summary>State the shell uses to decide whether it can show notifications.</summary>
    public enum QueryUserNotificationState
    {
        NotPresent = 1,
        Busy = 2,
        RunningDirect3DFullScreen = 3,
        PresentationMode = 4,
        AcceptsNotifications = 5,
        QuietTime = 6,
        App = 7
    }

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out QueryUserNotificationState pquns);

    /// <summary>
    /// Reports whether the shell itself considers an app fullscreen (exclusive fullscreen,
    /// borderless, or F11). Windows uses the same check to silence notifications, so it
    /// separates real fullscreen from a window that is merely maximized.
    /// </summary>
    public static bool IsShellReportingFullscreenApp()
    {
        try
        {
            if (SHQueryUserNotificationState(out var state) != 0)
            {
                return false;
            }

            return state is QueryUserNotificationState.Busy or QueryUserNotificationState.RunningDirect3DFullScreen;
        }
        catch
        {
            return false;
        }
    }
}
