using System.IO;
using System.Text.Json;
using ToolDock.Models;

namespace ToolDock.Services;

public static class DockShortcutsFileService
{
    public const string DefaultFileName = "Shortcuts_TD.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string GetExecutableDirectory() => AppPaths.DataDirectory;

    public static void SaveToFile(DockConfig config, string path)
    {
        var dto = DockShortcutsFileExport.FromConfig(config);
        var json = JsonSerializer.Serialize(dto, JsonOptions);
        File.WriteAllText(path, json);
    }

    /// <summary><paramref name="errorKey"/> es clave de localización si falla.</summary>
    public static bool TryLoad(string json, out DockShortcutsFileExport? export, out string? errorKey)
    {
        export = null;
        errorKey = null;

        DockShortcutsFileExport? data;
        try
        {
            data = JsonSerializer.Deserialize<DockShortcutsFileExport>(json, JsonOptions);
        }
        catch (JsonException)
        {
            errorKey = "ShortcutsFileErrorInvalidJson";
            return false;
        }

        if (data is null)
        {
            errorKey = "ShortcutsFileErrorInvalidStructure";
            return false;
        }

        if (data.SchemaVersion != DockShortcutsFileExport.CurrentSchemaVersion)
        {
            errorKey = "ShortcutsFileErrorUnsupportedVersion";
            return false;
        }

        if (data.Items is null)
        {
            errorKey = "ShortcutsFileErrorInvalidStructure";
            return false;
        }

        export = data;
        return true;
    }
}
