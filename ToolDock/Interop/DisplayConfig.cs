using System.Runtime.InteropServices;

namespace ToolDock.Interop;

/// <summary>
/// API CCD (Connecting and Configuring Displays) de user32. Se usa solo para obtener el nombre
/// comercial del monitor (p. ej. «DELL U2412M»), que el nombre GDI (<c>\\.\DISPLAY1</c>) no
/// incluye.
/// </summary>
internal static class DisplayConfig
{
    public const uint QDC_ONLY_ACTIVE_PATHS = 0x00000002;

    private const uint DEVICE_INFO_GET_SOURCE_NAME = 1;
    private const uint DEVICE_INFO_GET_TARGET_NAME = 2;

    /// <summary>
    /// Devuelve el nombre comercial de cada monitor activo indexado por su nombre GDI.
    /// Diccionario vacío si el sistema no puede resolverlos.
    /// </summary>
    public static Dictionary<string, string> GetFriendlyNamesByGdiDeviceName()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out var pathCount, out var modeCount) != 0)
            {
                return map;
            }

            var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
            var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
            if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != 0)
            {
                return map;
            }

            for (var i = 0; i < pathCount; i++)
            {
                var gdiName = GetSourceGdiDeviceName(paths[i]);
                if (string.IsNullOrWhiteSpace(gdiName) || map.ContainsKey(gdiName))
                {
                    continue;
                }

                var friendly = GetTargetFriendlyName(paths[i]);
                if (!string.IsNullOrWhiteSpace(friendly))
                {
                    map[gdiName] = friendly;
                }
            }
        }
        catch
        {
            // sin nombres: el selector cae al formato «índice + resolución»
        }

        return map;
    }

    private static string? GetSourceGdiDeviceName(DISPLAYCONFIG_PATH_INFO path)
    {
        var request = new DISPLAYCONFIG_SOURCE_DEVICE_NAME
        {
            header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
            {
                type = DEVICE_INFO_GET_SOURCE_NAME,
                size = (uint)Marshal.SizeOf<DISPLAYCONFIG_SOURCE_DEVICE_NAME>(),
                adapterId = path.sourceInfo.adapterId,
                id = path.sourceInfo.id
            }
        };

        return DisplayConfigGetDeviceInfo(ref request) == 0 ? request.viewGdiDeviceName : null;
    }

    private static string? GetTargetFriendlyName(DISPLAYCONFIG_PATH_INFO path)
    {
        var request = new DISPLAYCONFIG_TARGET_DEVICE_NAME
        {
            header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
            {
                type = DEVICE_INFO_GET_TARGET_NAME,
                size = (uint)Marshal.SizeOf<DISPLAYCONFIG_TARGET_DEVICE_NAME>(),
                adapterId = path.targetInfo.adapterId,
                id = path.targetInfo.id
            }
        };

        return DisplayConfigGetDeviceInfo(ref request) == 0 ? request.monitorFriendlyDeviceName?.Trim() : null;
    }

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(
        uint flags,
        ref uint numPathArrayElements,
        [Out] DISPLAYCONFIG_PATH_INFO[] pathArray,
        ref uint numModeInfoArrayElements,
        [Out] DISPLAYCONFIG_MODE_INFO[] modeInfoArray,
        IntPtr currentTopologyId);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SOURCE_DEVICE_NAME requestPacket);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_TARGET_DEVICE_NAME requestPacket);

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_RATIONAL
    {
        public uint Numerator;
        public uint Denominator;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_SOURCE_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_TARGET_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint outputTechnology;
        public uint rotation;
        public uint scaling;
        public DISPLAYCONFIG_RATIONAL refreshRate;
        public uint scanLineOrdering;
        [MarshalAs(UnmanagedType.Bool)]
        public bool targetAvailable;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_INFO
    {
        public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
        public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
        public uint flags;
    }

    /// <summary>Unión de modo (source/target/desktop image) que no se lee: 48 bytes opacos.</summary>
    [StructLayout(LayoutKind.Sequential, Size = 48)]
    private struct DISPLAYCONFIG_MODE_INFO_UNION
    {
        public ulong reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_MODE_INFO
    {
        public uint infoType;
        public uint id;
        public LUID adapterId;
        public DISPLAYCONFIG_MODE_INFO_UNION modeInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_DEVICE_INFO_HEADER
    {
        public uint type;
        public uint size;
        public LUID adapterId;
        public uint id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAYCONFIG_SOURCE_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string viewGdiDeviceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAYCONFIG_TARGET_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public uint flags;
        public uint outputTechnology;
        public ushort edidManufactureId;
        public ushort edidProductCodeId;
        public uint connectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string monitorFriendlyDeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string monitorDevicePath;
    }
}
