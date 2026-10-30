using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ToolDock;
using ToolDock.Models;

internal static class ColorPickerTests
{
    public static void Run()
    {
        var config = new DockConfig();
        var settings = new SettingsWindow(config);
        var owner = new Window { Resources = settings.Resources, ShowInTaskbar = false };
        new WindowInteropHelper(owner).EnsureHandle();
        try
        {
            CheckConversions(owner);
            CheckInputs(owner);
            CheckDialogActions(settings);
        }
        finally
        {
            owner.Close();
            settings.Close();
        }
    }

    private static void CheckConversions(Window owner)
    {
        var picker = new ColorPickerWindow(owner, AppLanguage.English, "#374B94");
        try
        {
            Check(picker.SelectedHex == "#374B94", "Opening the picker preserves the exact initial RGB color");
            var hex = Control<TextBox>(picker, "HexInput");
            var model = Control<ComboBox>(picker, "ColorModel");
            foreach (var sample in new[] { "#000000", "#FFFFFF", "#FF0000", "#FFFF00", "#00FF00", "#00FFFF", "#0000FF", "#FF00FF", "#808080", "#010203", "#FEFD01" })
            {
                hex.Text = sample;
                model.SelectedIndex = 1;
                model.SelectedIndex = 0;
                Check(picker.SelectedHex == sample, "Switching models preserves " + sample);
            }
            var random = new Random(42);
            for (var i = 0; i < 256; i++)
            {
                var sample = $"#{random.Next(0x1000000):X6}";
                hex.Text = sample;
                Check(picker.SelectedHex == sample, "RGB/HSV roundtrip preserves " + sample);
            }
            hex.Text = "#0000FF";
            var slider = Control<Slider>(picker, "ValueSlider");
            slider.Value = 0;
            Check(picker.SelectedHex == "#000000", "Zero brightness gives black");
            slider.Value = 100;
            Check(picker.SelectedHex == "#0000FF", "Restoring brightness preserves hue and saturation");

            var spectrum = Control<Grid>(picker, "Spectrum");
            spectrum.Measure(new Size(280, 240));
            spectrum.Arrange(new Rect(0, 0, 280, 240));
            Call(picker, "SelectAt", new Point(spectrum.ActualWidth / 2, 0));
            Check(picker.SelectedHex == "#00FFFF", "Spectrum midpoint selects cyan at full saturation");
            Call(picker, "SelectAt", new Point(-10, 250));
            Check(picker.SelectedHex == "#FFFFFF", "Spectrum dragging clamps outside its bounds");
        }
        finally { picker.Close(); }
    }

    private static void CheckInputs(Window owner)
    {
        var picker = new ColorPickerWindow(owner, AppLanguage.Spanish, "invalid");
        try
        {
            Check(picker.SelectedHex == "#FFFFFF", "An incomplete starting HEX uses white");
            Check(picker.Title == "Seleccionar color", "The dialog uses the selected language");
            var hex = Control<TextBox>(picker, "HexInput");
            var done = Control<Button>(picker, "DoneButton");
            hex.Text = " abc ";
            Check(picker.SelectedHex == "#AABBCC" && done.IsEnabled, "Short HEX without a hash is accepted");
            foreach (var invalid in new[] { "", "#", "#12", "#1234", "#12345678", "#GGHHII", "Red" })
            {
                hex.Text = invalid;
                Check(!done.IsEnabled && picker.SelectedHex == "#AABBCC", "Invalid HEX cannot be confirmed: " + invalid);
            }
            hex.Text = "#374B94";
            var first = Control<TextBox>(picker, "FirstInput");
            var second = Control<TextBox>(picker, "SecondInput");
            var third = Control<TextBox>(picker, "ThirdInput");
            Check(first.Text == "55" && second.Text == "75" && third.Text == "148", "HEX fills the RGB fields");
            first.Text = "255";
            second.Text = "0";
            third.Text = "0";
            Check(picker.SelectedHex == "#FF0000" && hex.Text == "#FF0000", "RGB editing updates HEX");
            foreach (var invalid in new[] { "256", "-1", "1.5", "NaN", "Infinity", "" })
            {
                first.Text = invalid;
                Check(!done.IsEnabled, "Invalid RGB is rejected: " + invalid);
            }
            first.Text = "255";
            var model = Control<ComboBox>(picker, "ColorModel");
            model.SelectedIndex = 1;
            first.Text = "120";
            second.Text = "100";
            third.Text = "100";
            Check(picker.SelectedHex == "#00FF00", "HSV editing selects green");
            first.Text = "360";
            Check(picker.SelectedHex == "#FF0000", "Hue 360 wraps to red");
            first.Text = "361";
            Check(!done.IsEnabled, "Hue beyond 360 is rejected");
            first.Text = "0";
            second.Text = "101";
            Check(!done.IsEnabled, "Saturation beyond 100 is rejected");
            second.Text = "50,5";
            third.Text = "50.5";
            Check(done.IsEnabled, "HSV accepts decimal comma or point");
            Check(((SolidColorBrush)Control<Border>(picker, "OriginalPreview").Background).Color == Colors.White,
                "The original preview does not change while editing");
            Check(!picker.IsVisible, "Input checks never display the picker");
        }
        finally { picker.Close(); }
    }

    private static void CheckDialogActions(SettingsWindow settings)
    {
        new WindowInteropHelper(settings).EnsureHandle();
        var hex = Control<TextBox>(settings, "HexTextBox");
        hex.Text = "#123456";
        foreach (var action in new[] { "close", "cancel", "accept" })
        {
            // Complete the modal dialog before rendering; do not interact with the user's input.
            settings.Dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(() =>
            {
                var picker = settings.OwnedWindows.OfType<ColorPickerWindow>().Single();
                Control<TextBox>(picker, "HexInput").Text = "#ABCDEF";
                if (action == "accept")
                    Control<Button>(picker, "DoneButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                else if (action == "cancel")
                {
                    var cancel = Control<Button>(picker, "CancelButton");
                    typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(cancel, null);
                }
                else
                    picker.Close();
            }));
            Call(settings, "SelectColorButton_Click", settings, new RoutedEventArgs());
            Check(hex.Text == (action == "accept" ? "#ABCDEF" : "#123456"), "Only Done copies the chosen color to Settings");
        }
        Check(!settings.IsLoaded && !settings.IsVisible, "Picker integration never loads Settings or its persistence");
    }

    private static T Control<T>(Window window, string name) => (T)window.FindName(name);
    private static void Call(object target, string method, params object[] arguments) =>
        target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, arguments);
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
