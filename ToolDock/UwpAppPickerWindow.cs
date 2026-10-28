using System.Windows;
using System.Windows.Controls;
using ToolDock.Models;
using ToolDock.Services;
using Button = System.Windows.Controls.Button;
using Orientation = System.Windows.Controls.Orientation;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace ToolDock;

/// <summary>Simple dialog for choosing an installed UWP/Store app.</summary>
public sealed class UwpAppPickerWindow : Window
{
    private readonly System.Windows.Controls.ListBox _list;

    public UwpApp? SelectedApp { get; private set; }

    public UwpAppPickerWindow(IReadOnlyList<UwpApp> apps, AppLanguage language)
    {
        Title = LocalizationService.Get(language, "AddUwpApp");
        Width = 460;
        Height = 540;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new DockPanel { Margin = new Thickness(12) };

        _list = new System.Windows.Controls.ListBox
        {
            DisplayMemberPath = nameof(UwpApp.Name),
            ItemsSource = apps.OrderBy(a => a.Name).ToList()
        };
        _list.MouseDoubleClick += (_, _) => Accept();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0)
        };
        var ok = new Button { Content = "OK", MinWidth = 90, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(10, 4, 10, 4), IsDefault = true };
        ok.Click += (_, _) => Accept();
        var cancel = new Button { Content = LocalizationService.Get(language, "Close"), MinWidth = 90, Padding = new Thickness(10, 4, 10, 4), IsCancel = true };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(_list);
        Content = root;
    }

    private void Accept()
    {
        if (_list.SelectedItem is UwpApp app)
        {
            SelectedApp = app;
            DialogResult = true;
        }
    }
}
