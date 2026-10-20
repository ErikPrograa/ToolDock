namespace ToolDock.Models;

/// <summary>Formato Shortcuts_TD.json: solo iconos del dock (rutas a .exe/.lnk), sin ajustes de UI ni presets.</summary>
public sealed class DockShortcutsFileExport
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>Rutas y nombres mostrados; el dock solo carga archivos que existan.</summary>
    public List<DockItemConfig> Items { get; set; } = [];

    public static DockShortcutsFileExport FromConfig(DockConfig config) =>
        new()
        {
            SchemaVersion = CurrentSchemaVersion,
            Items =
            [
                .. config.Items.Select(i => new DockItemConfig
                {
                    Path = i.Path,
                    Name = i.Name,
                    IconPath = i.IconPath,
                    AppUserModelId = i.AppUserModelId,
                    IsUwp = i.IsUwp
                })
            ]
        };

    public void ApplyTo(DockConfig config)
    {
        config.Items.Clear();
        foreach (var item in Items)
        {
            config.Items.Add(new DockItemConfig
            {
                Path = item.Path ?? string.Empty,
                Name = item.Name ?? string.Empty,
                IconPath = item.IconPath,
                AppUserModelId = item.AppUserModelId,
                IsUwp = item.IsUwp
            });
        }
    }
}
