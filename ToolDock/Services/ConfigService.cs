using System.IO;
using System.Text.Json;
using ToolDock.Models;

namespace ToolDock.Services;

public sealed class ConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private bool _canPersist = true;

    public string ConfigPath { get; }

    public ConfigService(string? configPath = null)
    {
        ConfigPath = configPath ?? Path.Combine(AppPaths.DataDirectory, "config.json");
    }

    public DockConfig Load()
    {
        var primary = DurableFile.Read(ConfigPath);
        if (primary.Status == TextReadStatus.Ok && TryParse(primary.Text) is { } parsed)
        {
            parsed.ArmStartupWidthGuard();
            _canPersist = true;
            return parsed;
        }

        if (primary.Status == TextReadStatus.IoFailure)
        {
            // El archivo existe pero está bloqueado (típico al iniciar sesión). Guardar ahora
            // lo sustituiría por los valores por defecto.
            _canPersist = false;
            if (TryLoadBackup() is { } lockedBackup)
            {
                lockedBackup.Config.ArmStartupWidthGuard();
                return lockedBackup.Config;
            }

            return new DockConfig();
        }

        // Vacío o JSON ilegible: apartarlo antes de reponer la copia, para no pisar el .bak.
        var corrupt = primary.Status != TextReadStatus.Missing;
        if (corrupt)
        {
            DurableFile.Quarantine(ConfigPath);
        }

        if (TryLoadBackup() is { } backup)
        {
            DurableFile.Write(ConfigPath, backup.RawJson, preserveBackup: true);
            backup.Config.ArmStartupWidthGuard();
            _canPersist = true;
            return backup.Config;
        }

        _canPersist = true;
        return new DockConfig();
    }

    public void Save(DockConfig config)
    {
        if (!_canPersist)
        {
            return;
        }

        var json = JsonSerializer.Serialize(config, JsonOptions);
        if (!DurableFile.Write(ConfigPath, json))
        {
            // El archivo anterior sigue intacto. No se relanza: un fallo de disco no debe tumbar el dock.
        }
    }

    private BackupLoad? TryLoadBackup()
    {
        var backup = DurableFile.ReadBackup(ConfigPath);
        if (backup.Status != TextReadStatus.Ok)
        {
            return null;
        }

        var config = TryParse(backup.Text);
        if (config is null || backup.Text is null)
        {
            return null;
        }

        return new BackupLoad(config, backup.Text);
    }

    private static DockConfig? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var schemaVersion = 0;
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty(nameof(DockConfig.ConfigSchemaVersion), out var versionElement) &&
                versionElement.TryGetInt32(out var parsedVersion))
            {
                schemaVersion = parsedVersion;
            }

            // Corte limpio de esquema: configuraciones de versiones anteriores se descartan
            // (las unidades y propiedades cambiaron a píxeles lógicos) y se parte de defaults.
            if (schemaVersion < DockConfig.CurrentSchemaVersion)
            {
                return new DockConfig();
            }

            DockConfig.BeginLoad();
            DockConfig? config;
            try
            {
                config = JsonSerializer.Deserialize<DockConfig>(json, JsonOptions);
            }
            finally
            {
                DockConfig.EndLoad();
            }

            if (config is null)
            {
                return null;
            }

            config.CoerceAfterLoad();
            return config;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            return null;
        }
    }

    private sealed record BackupLoad(DockConfig Config, string RawJson);
}
