using System.IO;
using System.Windows;
using ToolDock.Interop;

namespace ToolDock.Services;

internal static class AppIcons
{
    public const string WindowIconPackUri = "pack://application:,,,/Assets/icon.ico";
    private const string TrayIconPackUri = "pack://application:,,,/Assets/trayicon.ico";

    public static (IntPtr Handle, bool Owned) LoadTrayIcon()
    {
        try
        {
            var path = MaterializePackResource(TrayIconPackUri, "trayicon.ico");
            if (path is null)
            {
                return (User32.LoadApplicationIcon(), false);
            }

            var cx = Math.Max(16, User32.GetSystemMetrics(User32.SM_CXSMICON));
            var cy = Math.Max(16, User32.GetSystemMetrics(User32.SM_CYSMICON));
            var handle = User32.LoadImage(IntPtr.Zero, path, User32.IMAGE_ICON, cx, cy, User32.LR_LOADFROMFILE);
            if (handle == IntPtr.Zero)
            {
                return (User32.LoadApplicationIcon(), false);
            }

            return (handle, true);
        }
        catch
        {
            return (User32.LoadApplicationIcon(), false);
        }
    }

    private static string? MaterializePackResource(string packUri, string fileName)
    {
        var resource = Application.GetResourceStream(new Uri(packUri, UriKind.Absolute));
        if (resource is null)
        {
            return null;
        }

        var directory = Path.Combine(Path.GetTempPath(), "ToolDock");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        using var output = File.Create(path);
        resource.Stream.CopyTo(output);
        return path;
    }
}
