using System.Runtime.InteropServices;

namespace ToolDock.Interop;

/// <summary>Interop para reservar espacio en el área de trabajo mediante AppBar (estilo barra de tareas).</summary>
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

    /// <summary>Estado que el shell usa para decidir si puede mostrar notificaciones.</summary>
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
    /// Indica si el propio shell considera que hay una app en pantalla completa (fullscreen exclusivo,
    /// borderless o F11). Es el mismo criterio que usa Windows para silenciar notificaciones, por lo que
    /// distingue fullscreen real de una ventana simplemente maximizada.
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
