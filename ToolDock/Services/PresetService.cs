using System.IO;
using System.Text.Json;
using ToolDock.Models;

namespace ToolDock.Services;

public sealed class PresetService
{
    public const string PresetsFileName = "Presets_TD.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public string PresetsPath { get; }

    /// <summary>True si el archivo existe pero no se pudo leer. No hay que tratarlo como «sin presets».</summary>
    public bool LastLoadFailed { get; private set; }

    public PresetService()
    {
        PresetsPath = Path.Combine(AppPaths.DataDirectory, PresetsFileName);
    }

    public List<DockPreset> LoadCustomPresets()
    {
        LastLoadFailed = false;
        var read = DurableFile.Read(PresetsPath);
        if (read.Status == TextReadStatus.IoFailure)
        {
            LastLoadFailed = true;
            return [];
        }

        if (read.Status == TextReadStatus.Ok && TryParse(read.Text, out var parsed))
        {
            return parsed;
        }

        if (read.Status != TextReadStatus.Missing)
        {
            DurableFile.Quarantine(PresetsPath);
        }

        var backup = DurableFile.ReadBackup(PresetsPath);
        if (backup.Status == TextReadStatus.Ok && backup.Text is not null && TryParse(backup.Text, out var fromBackup))
        {
            DurableFile.Write(PresetsPath, backup.Text, preserveBackup: true);
            return fromBackup;
        }

        LastLoadFailed = read.Status != TextReadStatus.Missing;
        return [];
    }

    public void SaveCustomPresets(IEnumerable<DockPreset> customPresets)
    {
        var payload = customPresets
            .Where(p => !string.Equals(p.Name, DockConfig.DefaultPresetName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        if (DurableFile.Write(PresetsPath, json, preserveBackup: true))
        {
            LastLoadFailed = false;
        }
    }

    private static bool TryParse(string? json, out List<DockPreset> presets)
    {
        presets = [];
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<List<DockPreset>>(json, JsonOptions);
            if (parsed is null)
            {
                return false;
            }

            // Corte limpio de esquema: descarta presets de versiones anteriores (unidades cambiaron a px lógicos).
            presets = parsed
                .Where(p => !string.IsNullOrWhiteSpace(p.Name)
                            && p.Data is not null
                            && p.Data.DataSchemaVersion >= DockConfig.CurrentSchemaVersion)
                .ToList();
            return true;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            return false;
        }
    }
}
