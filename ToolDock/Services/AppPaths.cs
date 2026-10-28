using System.IO;

namespace ToolDock.Services;

/// <summary>
/// Data folder next to the executable. The single-file .exe does not use the extraction
/// temp directory: if that path changed on every launch, the app would create a new
/// config.json with default values.
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
