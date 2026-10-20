using System.Windows;

namespace ToolDock;

/// <summary>Marcas visuales (icono Fluent y estado activo) que no alteran la lógica de los controles.</summary>
public static class FluentChrome
{
    public static readonly DependencyProperty IconProperty = DependencyProperty.RegisterAttached(
        "Icon",
        typeof(string),
        typeof(FluentChrome),
        new PropertyMetadata(null));

    public static string? GetIcon(DependencyObject element) => (string?)element.GetValue(IconProperty);

    public static void SetIcon(DependencyObject element, string? value) => element.SetValue(IconProperty, value);

    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.RegisterAttached(
        "IsActive",
        typeof(bool),
        typeof(FluentChrome),
        new PropertyMetadata(false));

    public static bool GetIsActive(DependencyObject element) => (bool)element.GetValue(IsActiveProperty);

    public static void SetIsActive(DependencyObject element, bool value) => element.SetValue(IsActiveProperty, value);
}
