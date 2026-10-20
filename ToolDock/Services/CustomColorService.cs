using System.IO;
using System.Text.Json;
using ToolDock.Models;

namespace ToolDock.Services;

public sealed class CustomColorService
{
    public const string ColorsFileName = "Colors_TD.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public string ColorsPath { get; }

    /// <summary>True si el archivo existe pero no se pudo leer. No hay que sustituir los colores activos.</summary>
    public bool LastLoadFailed { get; private set; }

    public CustomColorService()
    {
        ColorsPath = Path.Combine(AppPaths.DataDirectory, ColorsFileName);
    }

    public List<NamedHexColor> LoadCustomColors()
    {
        LastLoadFailed = false;
        var read = DurableFile.Read(ColorsPath);
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
            DurableFile.Quarantine(ColorsPath);
        }

        var backup = DurableFile.ReadBackup(ColorsPath);
        if (backup.Status == TextReadStatus.Ok && backup.Text is not null && TryParse(backup.Text, out var fromBackup))
        {
            DurableFile.Write(ColorsPath, backup.Text, preserveBackup: true);
            return fromBackup;
        }

        LastLoadFailed = read.Status != TextReadStatus.Missing;
        return [];
    }

    public void SaveCustomColors(IEnumerable<NamedHexColor> customColors)
    {
        var payload = customColors
            .Where(c => !c.IsBuiltIn)
            .Select(c => new NamedHexColor
            {
                Name = c.Name.Trim(),
                Hex = NormalizeHex(c.Hex),
                IsBuiltIn = false
            })
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        if (DurableFile.Write(ColorsPath, json, preserveBackup: true))
        {
            LastLoadFailed = false;
        }
    }

    private static bool TryParse(string? json, out List<NamedHexColor> colors)
    {
        colors = [];
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<List<NamedHexColor>>(json, JsonOptions);
            if (parsed is null)
            {
                return false;
            }

            colors = parsed
                .Where(c => !string.IsNullOrWhiteSpace(c.Name) && !string.IsNullOrWhiteSpace(c.Hex))
                .Select(c => new NamedHexColor
                {
                    Name = c.Name.Trim(),
                    Hex = NormalizeHex(c.Hex),
                    IsBuiltIn = false
                })
                .DistinctBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return true;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            return false;
        }
    }

    private static string NormalizeHex(string value)
    {
        var hex = value.Trim().ToUpperInvariant();
        if (!hex.StartsWith('#'))
        {
            hex = $"#{hex}";
        }

        return hex;
    }
}
