using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace ToolDock.Models;

public sealed class DockItemViewModel : INotifyPropertyChanged
{
    private double _scale = 1.0;
    private double _offsetX;
    private double _offsetY;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Path { get; }

    public string Name { get; }

    public ImageSource? Icon { get; }

    /// <summary>Ruta a un icono personalizado asignado por el usuario (opcional).</summary>
    public string? IconPath { get; }

    /// <summary>AppUserModelID para apps UWP/Store.</summary>
    public string? AppUserModelId { get; }

    public bool IsUwp { get; }

    public double Scale
    {
        get => _scale;
        set
        {
            if (Math.Abs(_scale - value) < 0.001)
            {
                return;
            }

            _scale = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Scale)));
        }
    }

    public double OffsetX
    {
        get => _offsetX;
        set
        {
            if (Math.Abs(_offsetX - value) < 0.001)
            {
                return;
            }

            _offsetX = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(OffsetX)));
        }
    }

    public double OffsetY
    {
        get => _offsetY;
        set
        {
            if (Math.Abs(_offsetY - value) < 0.001)
            {
                return;
            }

            _offsetY = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(OffsetY)));
        }
    }

    public DockItemViewModel(
        string path,
        string name,
        ImageSource? icon,
        string? iconPath = null,
        string? appUserModelId = null,
        bool isUwp = false)
    {
        Path = path;
        Name = name;
        Icon = icon;
        IconPath = iconPath;
        AppUserModelId = appUserModelId;
        IsUwp = isUwp;
    }

    public DockItemConfig ToConfig()
    {
        return new DockItemConfig
        {
            Path = Path,
            Name = Name,
            IconPath = IconPath,
            AppUserModelId = AppUserModelId,
            IsUwp = IsUwp
        };
    }
}
