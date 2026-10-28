using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ToolDock.Services;

public sealed record UwpApp(string Name, string Aumid);

/// <summary>
/// Support for UWP / Microsoft Store apps: enumeration (shell applications folder),
/// launch by AppUserModelID, and package icon (IShellItemImageFactory).
/// </summary>
public static class UwpAppService
{
    public const string UwpPathPrefix = @"shell:AppsFolder\";

    public static bool IsUwpPath(string? path) =>
        !string.IsNullOrWhiteSpace(path) && path.StartsWith(UwpPathPrefix, StringComparison.OrdinalIgnoreCase);

    public static string ToAumid(string path) =>
        IsUwpPath(path) ? path[UwpPathPrefix.Length..] : path;

    public static string ToShellPath(string aumid) =>
        IsUwpPath(aumid) ? aumid : UwpPathPrefix + aumid;

    public static void Launch(string aumid)
    {
        Process.Start(new ProcessStartInfo(ToShellPath(aumid))
        {
            UseShellExecute = true
        });
    }

    public static IReadOnlyList<UwpApp> GetInstalledApps()
    {
        var apps = new List<UwpApp>();
        object? shell = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null)
            {
                return apps;
            }

            shell = Activator.CreateInstance(shellType);
            if (shell is null)
            {
                return apps;
            }

            dynamic shellObject = shell;
            dynamic folder = shellObject.NameSpace("shell:AppsFolder");
            if (folder is null)
            {
                return apps;
            }

            dynamic items = folder.Items();
            foreach (dynamic item in items)
            {
                try
                {
                    string name = item.Name;
                    string aumid = item.Path;
                    if (!string.IsNullOrWhiteSpace(aumid) && aumid.Contains('!'))
                    {
                        // Packaged apps only (AUMID contains '!'): excludes classic Win32 shortcuts.
                        apps.Add(new UwpApp(name, aumid));
                    }
                }
                catch
                {
                    // ignore individual item
                }
            }
        }
        catch
        {
            // ignore
        }
        finally
        {
            if (shell is not null && Marshal.IsComObject(shell))
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }

        return apps
            .GroupBy(a => a.Aumid, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static ImageSource? TryLoadLogo(string? aumid, int size = 96)
    {
        if (string.IsNullOrWhiteSpace(aumid))
        {
            return null;
        }

        var hbitmap = IntPtr.Zero;
        try
        {
            var guid = typeof(IShellItemImageFactory).GUID;
            var hr = SHCreateItemFromParsingName(ToShellPath(aumid), IntPtr.Zero, ref guid, out var factory);
            if (hr != 0 || factory is null)
            {
                return null;
            }

            try
            {
                var dim = Math.Max(32, size);
                var sizeStruct = new SIZE { cx = dim, cy = dim };
                var imgHr = factory.GetImage(sizeStruct, SIIGBF_RESIZETOFIT | SIIGBF_BIGGERSIZEOK, out hbitmap);
                if (imgHr != 0 || hbitmap == IntPtr.Zero)
                {
                    return null;
                }

                var source = Imaging.CreateBitmapSourceFromHBitmap(
                    hbitmap,
                    IntPtr.Zero,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            finally
            {
                Marshal.ReleaseComObject(factory);
            }
        }
        catch
        {
            return null;
        }
        finally
        {
            if (hbitmap != IntPtr.Zero)
            {
                DeleteObject(hbitmap);
            }
        }
    }

    private const int SIIGBF_RESIZETOFIT = 0x00000000;
    private const int SIIGBF_BIGGERSIZEOK = 0x00000004;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(
        string pszPath,
        IntPtr pbc,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int cx;
        public int cy;
    }

    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(SIZE size, int flags, out IntPtr phbm);
    }
}
