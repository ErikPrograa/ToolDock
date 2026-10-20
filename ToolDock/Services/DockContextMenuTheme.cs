using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using ToolDock.Models;

namespace ToolDock.Services;

/// <summary>Estilo compartido de los menús contextuales del dock y de la bandeja.</summary>
internal static class DockContextMenuTheme
{
    public static void Apply(ContextMenu contextMenu, AppTheme theme)
    {
        var isDark = theme == AppTheme.Dark;
        var back = isDark ? Color.FromRgb(42, 44, 53) : Color.FromRgb(255, 255, 255);
        var fore = isDark ? Color.FromRgb(243, 243, 243) : Color.FromRgb(26, 26, 26);
        var hover = isDark ? Color.FromRgb(60, 63, 74) : Color.FromRgb(243, 243, 243);
        var border = isDark ? Color.FromRgb(58, 61, 72) : Color.FromRgb(229, 229, 229);

        var backBrush = FrozenBrush(back);
        var foreBrush = FrozenBrush(fore);
        var hoverBrush = FrozenBrush(hover);
        var borderBrush = FrozenBrush(border);

        contextMenu.Background = backBrush;
        contextMenu.Foreground = foreBrush;
        contextMenu.BorderBrush = borderBrush;
        contextMenu.BorderThickness = new Thickness(1);
        contextMenu.Padding = new Thickness(4);
        contextMenu.FontFamily = new FontFamily("Segoe UI Variable, Segoe UI");
        contextMenu.Template = BuildMenuTemplate();

        var itemStyle = new Style(typeof(MenuItem));
        itemStyle.Setters.Add(new Setter(MenuItem.OverridesDefaultStyleProperty, true));
        itemStyle.Setters.Add(new Setter(MenuItem.BackgroundProperty, backBrush));
        itemStyle.Setters.Add(new Setter(MenuItem.ForegroundProperty, foreBrush));
        itemStyle.Setters.Add(new Setter(MenuItem.BorderThicknessProperty, new Thickness(0)));
        itemStyle.Setters.Add(new Setter(MenuItem.PaddingProperty, new Thickness(8, 6, 10, 6)));
        itemStyle.Setters.Add(new Setter(MenuItem.TemplateProperty, BuildMenuItemTemplate(backBrush, borderBrush)));

        var highlighted = new Trigger { Property = MenuItem.IsHighlightedProperty, Value = true };
        highlighted.Setters.Add(new Setter(MenuItem.BackgroundProperty, hoverBrush));
        highlighted.Setters.Add(new Setter(MenuItem.ForegroundProperty, foreBrush));
        itemStyle.Triggers.Add(highlighted);

        itemStyle.Seal();
        contextMenu.Resources[typeof(MenuItem)] = itemStyle;

        var separatorStyle = new Style(typeof(Separator));
        separatorStyle.Setters.Add(new Setter(Separator.OverridesDefaultStyleProperty, true));
        separatorStyle.Setters.Add(new Setter(Separator.TemplateProperty, BuildSeparatorTemplate(borderBrush)));
        separatorStyle.Seal();
        contextMenu.Resources[typeof(Separator)] = separatorStyle;
    }

    private static SolidColorBrush FrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static ControlTemplate BuildMenuTemplate()
    {
        var template = new ControlTemplate(typeof(ContextMenu));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding(nameof(ContextMenu.Background))
        {
            RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)
        });
        border.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding(nameof(ContextMenu.BorderBrush))
        {
            RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)
        });
        border.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding(nameof(ContextMenu.BorderThickness))
        {
            RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)
        });
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        border.SetValue(Border.PaddingProperty, new Thickness(4));
        border.SetValue(Border.MarginProperty, new Thickness(0, 2, 10, 12));
        border.SetValue(UIElement.EffectProperty, new DropShadowEffect
        {
            BlurRadius = 16,
            ShadowDepth = 1,
            Opacity = 0.32,
            Color = Colors.Black
        });

        var presenter = new FrameworkElementFactory(typeof(ItemsPresenter));
        border.AppendChild(presenter);
        template.VisualTree = border;
        return template;
    }

    private static ControlTemplate BuildMenuItemTemplate(Brush menuBackground, Brush menuBorder)
    {
        var template = new ControlTemplate(typeof(MenuItem));

        var border = new FrameworkElementFactory(typeof(Border));
        border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding(nameof(MenuItem.Background))
        {
            RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)
        });
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        border.SetValue(Border.MarginProperty, new Thickness(2, 1, 2, 1));
        border.SetValue(Border.PaddingProperty, new Thickness(8, 6, 10, 6));

        var grid = new FrameworkElementFactory(typeof(Grid));
        var checkCol = new FrameworkElementFactory(typeof(ColumnDefinition));
        checkCol.SetValue(ColumnDefinition.WidthProperty, new GridLength(0, GridUnitType.Auto));
        var headerCol = new FrameworkElementFactory(typeof(ColumnDefinition));
        headerCol.SetValue(ColumnDefinition.WidthProperty, new GridLength(1, GridUnitType.Star));
        var arrowCol = new FrameworkElementFactory(typeof(ColumnDefinition));
        arrowCol.SetValue(ColumnDefinition.WidthProperty, new GridLength(0, GridUnitType.Auto));
        grid.AppendChild(checkCol);
        grid.AppendChild(headerCol);
        grid.AppendChild(arrowCol);

        var check = new FrameworkElementFactory(typeof(TextBlock));
        check.Name = "CheckMark";
        check.SetValue(Grid.ColumnProperty, 0);
        check.SetValue(TextBlock.TextProperty, "\uE73E");
        check.SetValue(TextBlock.FontFamilyProperty, new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"));
        check.SetValue(TextBlock.FontSizeProperty, 12d);
        check.SetValue(TextBlock.MarginProperty, new Thickness(0, 0, 8, 0));
        check.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        check.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
        grid.AppendChild(check);

        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(Grid.ColumnProperty, 1);
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Left);
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        presenter.SetBinding(ContentPresenter.ContentProperty, new System.Windows.Data.Binding(nameof(HeaderedItemsControl.Header))
        {
            RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)
        });
        presenter.SetBinding(ContentPresenter.ContentTemplateProperty, new System.Windows.Data.Binding(nameof(HeaderedItemsControl.HeaderTemplate))
        {
            RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)
        });
        grid.AppendChild(presenter);

        var arrow = new FrameworkElementFactory(typeof(TextBlock));
        arrow.Name = "SubmenuArrow";
        arrow.SetValue(Grid.ColumnProperty, 2);
        arrow.SetValue(TextBlock.TextProperty, "\uE76C");
        arrow.SetValue(TextBlock.FontFamilyProperty, new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"));
        arrow.SetValue(TextBlock.FontSizeProperty, 10d);
        arrow.SetValue(TextBlock.MarginProperty, new Thickness(12, 0, 2, 0));
        arrow.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        arrow.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
        grid.AppendChild(arrow);

        var popup = new FrameworkElementFactory(typeof(System.Windows.Controls.Primitives.Popup));
        popup.Name = "SubmenuPopup";
        popup.SetValue(Grid.ColumnProperty, 1);
        popup.SetValue(System.Windows.Controls.Primitives.Popup.PlacementProperty, System.Windows.Controls.Primitives.PlacementMode.Right);
        popup.SetValue(System.Windows.Controls.Primitives.Popup.AllowsTransparencyProperty, true);
        popup.SetValue(System.Windows.Controls.Primitives.Popup.StaysOpenProperty, false);
        popup.SetBinding(System.Windows.Controls.Primitives.Popup.IsOpenProperty, new System.Windows.Data.Binding(nameof(MenuItem.IsSubmenuOpen))
        {
            RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)
        });

        var popupBorder = new FrameworkElementFactory(typeof(Border));
        popupBorder.SetValue(Border.BackgroundProperty, menuBackground);
        popupBorder.SetValue(Border.BorderBrushProperty, menuBorder);
        popupBorder.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        popupBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        popupBorder.SetValue(Border.PaddingProperty, new Thickness(4));
        var subItems = new FrameworkElementFactory(typeof(StackPanel));
        subItems.SetValue(StackPanel.IsItemsHostProperty, true);
        popupBorder.AppendChild(subItems);
        popup.AppendChild(popupBorder);
        grid.AppendChild(popup);

        border.AppendChild(grid);
        template.VisualTree = border;

        var checkedTrigger = new Trigger { Property = MenuItem.IsCheckedProperty, Value = true };
        checkedTrigger.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "CheckMark"));
        template.Triggers.Add(checkedTrigger);

        var submenuTrigger = new Trigger { Property = MenuItem.RoleProperty, Value = MenuItemRole.SubmenuHeader };
        submenuTrigger.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "SubmenuArrow"));
        template.Triggers.Add(submenuTrigger);

        return template;
    }

    private static ControlTemplate BuildSeparatorTemplate(Brush stroke)
    {
        var template = new ControlTemplate(typeof(Separator));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.HeightProperty, 1d);
        border.SetValue(Border.MarginProperty, new Thickness(8, 4, 8, 4));
        border.SetValue(Border.BackgroundProperty, stroke);
        template.VisualTree = border;
        return template;
    }
}
