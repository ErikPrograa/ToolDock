using System.IO;

namespace ToolDock.Services;

/// <summary>
/// Carpeta de datos junto al ejecutable. En el .exe de un solo archivo no se usa el
/// directorio temporal de extracción: si cambiara en cada arranque, la app crearía un
/// config.json nuevo con valores por defecto.
/// </summary>
public static class AppPaths
{
    public static string DataDirectory
    {
        get
        {
            var processPath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(processPath))
            {
                var directory = Path.GetDirectoryName(processPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    return Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
                }
            }

            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory));
        }
    }
}
