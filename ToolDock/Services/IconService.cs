using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ToolDock.Services;

public static class IconService
{
    private static readonly int[] IconSizes = [16, 20, 24, 32, 40, 48, 64, 96, 128];

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint PrivateExtractIcons(
        string lpszFile,
        int nIconIndex,
        int cxIcon,
        int cyIcon,
        IntPtr[] phicon,
        uint[] piconid,
        uint nIcons,
        uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(
        string pszPath,
        uint dwFileAttributes,
        ref SHFILEINFO psfi,
        uint cbFileInfo,
        uint uFlags);

    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_LARGEICON = 0x000000000;

    /// <summary>
    /// Extraction size in physical pixels: covers DPI and hover without inflating to 256 px
    /// when the dock paints the icon at 16–32 logical px. With <paramref name="highQuality"/>
    /// 256 px is always used (better hover, more RAM).
    /// </summary>
    public static int GetExtractionPixelSize(
        double logicalIconSize,
        double dpiScale,
        double maxHoverScale,
        bool highQuality = false)
    {
        if (highQuality)
        {
            return 256;
        }

        var physical = Math.Max(16, logicalIconSize) * Math.Max(1.0, dpiScale) * Math.Max(1.0, maxHoverScale);
        foreach (var candidate in IconSizes)
        {
            if (physical <= candidate + 0.5)
            {
                return candidate;
            }
        }

        return IconSizes[^1];
    }

    /// <summary>
    /// Loads an item icon in this order: custom icon &gt; UWP logo &gt; .lnk icon &gt;
    /// .ico in the executable folder &gt; icon associated with the executable.
    /// </summary>
    public static ImageSource? LoadIconForItem(ToolDock.Models.DockItemConfig item, int size = 64)
    {
        if (!string.IsNullOrWhiteSpace(item.IconPath))
        {
            var custom = TryLoadFromImageOrIcon(item.IconPath!, size);
            if (custom is not null)
            {
                return custom;
            }
        }

        if (item.IsUwp)
        {
            var uwp = UwpAppService.TryLoadLogo(item.AppUserModelId, size);
            if (uwp is not null)
            {
                return uwp;
            }
        }

        return TryLoadIcon(item.Path, size);
    }

    /// <summary>Loads a .ico/.png/.jpg or extracts the icon from an .exe/.dll at the given path.</summary>
    public static ImageSource? TryLoadFromImageOrIcon(string path, int size = 64)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return null;
            }

            var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
            if (ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif")
            {
                var decode = Math.Max(16, size);
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bmp.DecodePixelWidth = decode;
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }

            return TryExtractIcon(path, size) ?? TryLoadIcon(path, size);
        }
        catch
        {
            return null;
        }
    }

    public static ImageSource? TryLoadIcon(string path, int size = 64)
    {
        var iconPath = ResolveIconSourcePath(path);

        // "Shortcut's own icon" case (for example Discord): if the .lnk sets IconLocation, use it.
        if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            var shortcutIcon = TryResolveShortcutIconLocation(path);
            if (shortcutIcon is not null)
            {
                var fromShortcut = TryExtractIcon(shortcutIcon, size);
                if (fromShortcut is not null)
                {
                    return fromShortcut;
                }
            }
        }

        try
        {
            if (!File.Exists(iconPath))
            {
                return null;
            }

            var extracted = TryExtractIcon(iconPath, size);
            if (extracted is not null)
            {
                return extracted;
            }

            // If the executable has no embedded icon, look for a .ico in its folder.
            var folderIco = TryFindFolderIcon(iconPath);
            if (folderIco is not null)
            {
                var fromFolder = TryExtractIcon(folderIco, size);
                if (fromFolder is not null)
                {
                    return fromFolder;
                }
            }

            return TryLoadAssociatedIcon(iconPath);
        }
        catch
        {
            return null;
        }
    }

    private static string? TryFindFolderIcon(string executablePath)
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(executablePath);
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            {
                return null;
            }

            var baseName = System.IO.Path.GetFileNameWithoutExtension(executablePath);
            var preferred = Directory.GetFiles(dir, "*.ico")
                .OrderByDescending(f =>
                    System.IO.Path.GetFileNameWithoutExtension(f).Equals(baseName, StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault();
            return preferred;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryResolveShortcutIconLocation(string lnkPath)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null)
            {
                return null;
            }

            var shell = Activator.CreateInstance(shellType);
            if (shell is null)
            {
                return null;
            }

            try
            {
                dynamic shellObject = shell;
                var shortcut = shellObject.CreateShortcut(lnkPath);
                try
                {
                    dynamic shortcutObject = shortcut;
                    var iconLocation = shortcutObject.IconLocation as string;
                    if (string.IsNullOrWhiteSpace(iconLocation))
                    {
                        return null;
                    }

                    var commaIndex = iconLocation.LastIndexOf(',');
                    var iconFile = commaIndex > 0 ? iconLocation[..commaIndex] : iconLocation;
                    iconFile = iconFile.Trim();
                    return !string.IsNullOrWhiteSpace(iconFile) && File.Exists(iconFile) ? iconFile : null;
                }
                finally
                {
                    if (shortcut is not null && Marshal.IsComObject(shortcut))
                    {
                        Marshal.FinalReleaseComObject(shortcut);
                    }
                }
            }
            finally
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }
        catch
        {
            return null;
        }
    }

    private static ImageSource? TryExtractIcon(string path, int size)
    {
        var handles = new[] { IntPtr.Zero };
        var ids = new uint[1];
        try
        {
            var count = PrivateExtractIcons(path, 0, size, size, handles, ids, 1, 0);
            if (count == 0 || handles[0] == IntPtr.Zero)
            {
                return null;
            }

            return CreateFrozenBitmapFromIcon(handles[0]);
        }
        catch
        {
            return null;
        }
        finally
        {
            if (handles[0] != IntPtr.Zero)
            {
                DestroyIcon(handles[0]);
            }
        }
    }

    private static ImageSource? TryLoadAssociatedIcon(string path)
    {
        var info = new SHFILEINFO();
        var result = SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_ICON | SHGFI_LARGEICON);
        if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            return CreateFrozenBitmapFromIcon(info.hIcon);
        }
        finally
        {
            DestroyIcon(info.hIcon);
        }
    }

    private static ImageSource CreateFrozenBitmapFromIcon(IntPtr hIcon)
    {
        var source = Imaging.CreateBitmapSourceFromHIcon(
            hIcon,
            Int32Rect.Empty,
            BitmapSizeOptions.FromEmptyOptions());
        source.Freeze();
        return source;
    }

    private static string ResolveIconSourcePath(string path)
    {
        if (!path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null)
            {
                return path;
            }

            var shell = Activator.CreateInstance(shellType);
            if (shell is null)
            {
                return path;
            }

            try
            {
                dynamic shellObject = shell;
                var shortcut = shellObject.CreateShortcut(path);
                try
                {
                    dynamic shortcutObject = shortcut;
                    var target = shortcutObject.TargetPath as string;
                    if (!string.IsNullOrWhiteSpace(target) && File.Exists(target))
                    {
                        return target;
                    }
                }
                finally
                {
                    if (shortcut is not null && Marshal.IsComObject(shortcut))
                    {
                        Marshal.FinalReleaseComObject(shortcut);
                    }
                }
            }
            finally
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }
        catch
        {
            // fallback below
        }

        return path;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }
}
