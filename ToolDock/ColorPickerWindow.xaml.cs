using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using ToolDock.Interop;
using ToolDock.Models;
using ToolDock.Services;

namespace ToolDock;

public partial class ColorPickerWindow : Window
{
    private readonly AppLanguage _language;
    private bool _updating = true;
    private double _hue;
    private double _saturation;
    private double _value;

    public string SelectedHex { get; private set; } = "#FFFFFF";

    public ColorPickerWindow(Window owner, AppLanguage language, string initialHex)
    {
        _language = language;
        Resources.MergedDictionaries.Add(owner.Resources);
        InitializeComponent();
        Owner = owner;
        var monitor = User32.MonitorFromWindow(new WindowInteropHelper(owner).Handle, User32.MONITOR_DEFAULTTONEAREST);
        var info = new User32.MONITORINFO { cbSize = Marshal.SizeOf<User32.MONITORINFO>() };
        if (User32.GetMonitorInfo(monitor, ref info))
        {
            var dpi = VisualTreeHelper.GetDpi(owner);
            var availableWidth = Math.Max(1, info.rcWork.Width / dpi.DpiScaleX - 16);
            var availableHeight = Math.Max(1, info.rcWork.Height / dpi.DpiScaleY - 16);
            MinWidth = Math.Min(MinWidth, availableWidth);
            MinHeight = Math.Min(MinHeight, availableHeight);
            Width = Math.Min(Width, availableWidth);
            Height = Math.Min(Height, availableHeight);
        }
        Title = Text("SelectColor");
        Heading.Text = Text("ColorPickerTitle");
        DoneButton.Content = Text("ColorPickerDone");
        CancelButton.Content = Text("ColorPickerCancel");
        ValueLabel.Text = Text("ColorPickerValue") + " (%)";
        ValidationMessage.Text = Text("ColorPickerInvalid");
        OriginalPreview.ToolTip = Text("ColorPickerOriginal");
        ColorPreview.ToolTip = Text("ColorPickerPreview");
        AutomationProperties.SetName(Spectrum, Text("ColorPickerSpectrum"));
        AutomationProperties.SetName(ValueSlider, ValueLabel.Text);
        AutomationProperties.SetName(ColorModel, Text("ColorPickerModel"));
        ColorModel.SelectedIndex = 0;
        var color = TryParseHex(initialHex, out var parsed) ? parsed : Colors.White;
        OriginalPreview.Background = new SolidColorBrush(color);
        SetFromRgb(color);
        RefreshColor();
    }

    private string Text(string key) => LocalizationService.Get(_language, key);

    private void RefreshColor(bool updateComponents = true, bool updateHex = true)
    {
        _updating = true;
        var color = FromHsv(_hue, _saturation, _value);
        SelectedHex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        if (updateHex) HexInput.Text = SelectedHex;
        if (updateComponents)
        {
            var rgb = ColorModel.SelectedIndex == 0;
            FirstLabel.Text = Text(rgb ? "ColorRed" : "ColorPickerHue") + (rgb ? "" : " (°)");
            SecondLabel.Text = Text(rgb ? "ColorGreen" : "ColorPickerSaturation") + (rgb ? "" : " (%)");
            ThirdLabel.Text = Text(rgb ? "ColorBlue" : "ColorPickerValue") + (rgb ? "" : " (%)");
            FirstInput.Text = rgb ? color.R.ToString() : _hue.ToString("0.##", CultureInfo.InvariantCulture);
            SecondInput.Text = rgb ? color.G.ToString() : (_saturation * 100).ToString("0.##", CultureInfo.InvariantCulture);
            ThirdInput.Text = rgb ? color.B.ToString() : (_value * 100).ToString("0.##", CultureInfo.InvariantCulture);
            AutomationProperties.SetName(FirstInput, FirstLabel.Text);
            AutomationProperties.SetName(SecondInput, SecondLabel.Text);
            AutomationProperties.SetName(ThirdInput, ThirdLabel.Text);
        }
        ValueSlider.Value = _value * 100;
        ValueSlider.Background = new LinearGradientBrush(Colors.Black, FromHsv(_hue, _saturation, 1), 0);
        ColorPreview.Background = new SolidColorBrush(color);
        AutomationProperties.SetName(ColorPreview, Text("ColorPickerPreview") + " " + SelectedHex);
        UpdateMarker();
        SetValid(true);
        _updating = false;
    }

    private void SetValid(bool valid)
    {
        DoneButton.IsEnabled = valid;
        ValidationMessage.Visibility = valid ? Visibility.Collapsed : Visibility.Visible;
    }

    private void HexInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updating) return;
        if (!TryParseHex(HexInput.Text, out var color))
        {
            SetValid(false);
            return;
        }
        SetFromRgb(color);
        RefreshColor(updateHex: false);
    }

    private void Component_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updating) return;
        var rgb = ColorModel.SelectedIndex == 0;
        if (!TryComponent(FirstInput.Text, rgb ? 255 : 360, rgb, out var first) ||
            !TryComponent(SecondInput.Text, rgb ? 255 : 100, rgb, out var second) ||
            !TryComponent(ThirdInput.Text, rgb ? 255 : 100, rgb, out var third))
        {
            SetValid(false);
            return;
        }
        if (rgb)
            SetFromRgb(Color.FromRgb((byte)first, (byte)second, (byte)third));
        else
            (_hue, _saturation, _value) = (first, second / 100, third / 100);
        RefreshColor(updateComponents: false);
    }

    private static bool TryComponent(string text, double maximum, bool integer, out double value) =>
        double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value) &&
        double.IsFinite(value) && value >= 0 && value <= maximum && (!integer || value == Math.Truncate(value));

    private void ColorModel_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_updating) RefreshColor();
    }

    private void ValueSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updating) return;
        _value = e.NewValue / 100;
        RefreshColor();
    }

    private void Spectrum_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        Spectrum.Focus();
        Spectrum.CaptureMouse();
        SelectAt(e.GetPosition(Spectrum));
        e.Handled = true;
    }

    private void Spectrum_MouseMove(object sender, MouseEventArgs e)
    {
        if (Spectrum.IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed)
            SelectAt(e.GetPosition(Spectrum));
    }

    private void Spectrum_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        Spectrum.ReleaseMouseCapture();
    }

    private void SelectAt(Point point)
    {
        if (Spectrum.ActualWidth <= 0 || Spectrum.ActualHeight <= 0) return;
        _hue = Math.Clamp(point.X / Spectrum.ActualWidth, 0, 1) * 360;
        _saturation = 1 - Math.Clamp(point.Y / Spectrum.ActualHeight, 0, 1);
        RefreshColor();
    }

    private void Spectrum_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Left: _hue = Math.Max(0, _hue - 1); break;
            case Key.Right: _hue = Math.Min(360, _hue + 1); break;
            case Key.Up: _saturation = Math.Min(1, _saturation + 0.01); break;
            case Key.Down: _saturation = Math.Max(0, _saturation - 0.01); break;
            default: return;
        }
        RefreshColor();
        e.Handled = true;
    }

    private void Spectrum_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_updating) UpdateMarker();
    }

    private void UpdateMarker()
    {
        Canvas.SetLeft(SelectionMarker, _hue / 360 * Spectrum.ActualWidth - 8);
        Canvas.SetTop(SelectionMarker, (1 - _saturation) * Spectrum.ActualHeight - 8);
    }

    private void Done_Click(object sender, RoutedEventArgs e)
    {
        if (DoneButton.IsEnabled) DialogResult = true;
    }

    private void SetFromRgb(Color color)
    {
        var red = color.R / 255d;
        var green = color.G / 255d;
        var blue = color.B / 255d;
        var max = Math.Max(red, Math.Max(green, blue));
        var delta = max - Math.Min(red, Math.Min(green, blue));
        _value = max;
        // Keep the last hue at gray and the last saturation at black so the slider can restore them.
        if (max > 0) _saturation = delta / max;
        if (delta == 0) return;
        var sector = max == red ? (green - blue) / delta : max == green ? (blue - red) / delta + 2 : (red - green) / delta + 4;
        _hue = (sector * 60 + 360) % 360;
    }

    private static Color FromHsv(double hue, double saturation, double value)
    {
        var chroma = value * saturation;
        var sector = hue / 60 % 6;
        var x = chroma * (1 - Math.Abs(sector % 2 - 1));
        var (red, green, blue) = sector switch
        {
            < 1 => (chroma, x, 0d),
            < 2 => (x, chroma, 0d),
            < 3 => (0d, chroma, x),
            < 4 => (0d, x, chroma),
            < 5 => (x, 0d, chroma),
            _ => (chroma, 0d, x)
        };
        var offset = value - chroma;
        return Color.FromRgb((byte)Math.Round((red + offset) * 255),
            (byte)Math.Round((green + offset) * 255), (byte)Math.Round((blue + offset) * 255));
    }

    private static bool TryParseHex(string text, out Color color)
    {
        color = Colors.White;
        var hex = text.Trim();
        if (hex.StartsWith('#')) hex = hex[1..];
        if (hex.Length is not (3 or 6) || !hex.All(Uri.IsHexDigit)) return false;
        if (hex.Length == 3) hex = string.Concat(hex.Select(c => new string(c, 2)));
        var rgb = uint.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        color = Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        return true;
    }
}
