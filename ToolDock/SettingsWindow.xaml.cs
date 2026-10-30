using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using ToolDock.Interop;
using ToolDock.Models;
using ToolDock.Services;
using WpfBinding = System.Windows.Data.Binding;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace ToolDock;

public enum SettingsCategory
{
    Info,
    General,
    QuickAccess,
    Position,
    Appearance,
    Shortcuts,
    Behaviour,
    Details,
    Troubleshooting
}

public partial class SettingsWindow : Window, INotifyPropertyChanged
{
    private const string DefaultPresetName = "Default";
    private const string CustomPresetName = "Custom";
    private static readonly HashSet<string> AutoHideDependentQuickAccessOptions = [];
    private static readonly HashSet<string> HoverDependentQuickAccessOptions = [];

    private static readonly HashSet<string> OutlineDependentQuickAccessOptions =
    [
        "OutlineColorHex",
        "OutlineOpacity",
        "OutlineThickness"
    ];

    private readonly Dictionary<WpfTextBox, NumericBinding> _numericBindings = [];
    private readonly HashSet<string> _supportedPins =
    [
        "Edge", "AutoAdjustDockWidth", "DockWidth", "DockHeight", "Offset",
        "SolidColorHex", "DockOpacity",
        "ShowOutline", "OutlineColorHex", "OutlineOpacity", "OutlineThickness",
        "RoundedCorners", "CornerRadius",
        "IconSize", "IconSpacing", "ButtonAlignment", "EnableHoverEffects",
        "AutoHide"
    ];
    private SettingsCategory _currentCategory;
    private bool _isSizePersistenceReady;
    private bool _isUpdatingColorHexText;
    private string? _draggedQuickAccessOptionKey;
    private System.Windows.Point _quickAccessDragStartPoint;
    private readonly PresetService _presetService = new();
    private readonly CustomColorService _customColorService = new();
    private readonly List<DockPreset> _presets = [];
    private readonly List<NamedHexColor> _savedColors = [];
    private bool _isPresetSelectionUpdating;
    private bool _isSavedColorSelectionUpdating;
    private string _selectedPresetName = DefaultPresetName;
    private string _lastNonCustomPresetName = DefaultPresetName;
    private bool _presetStateReady;
    private bool _syncingPreset;
    private string _lastValidCustomHex = "#FFFFFF";
    private bool _isApplyingPresetData;
    private readonly string _instanceLabel;
    public event PropertyChangedEventHandler? PropertyChanged;

    public DockConfig Config { get; }

    public bool IsDarkMode
    {
        get => Config.Theme == AppTheme.Dark;
        set => Config.Theme = value ? AppTheme.Dark : AppTheme.Light;
    }

    public double DockWidthMin => DockConfig.GetDockWidthMin(Config.IconSize, Config.IconSpacing, Config.Items.Count);

    public double DockWidthMax => DockConfig.GetDockWidthMax(Config.Edge);

    public double IconSizeMin => DockConfig.GetIconSizeMin();

    public double IconSizeMax => DockConfig.GetIconSizeMax();

    public double IconSpacingMin => DockConfig.GetIconSpacingMin();

    public double IconSpacingMax => DockConfig.GetIconSpacingMax();

    public double DockHeightMin => DockConfig.GetDockHeightMin();

    public double DockHeightMax => DockConfig.GetDockHeightMax();

    public double HoverRadiusMinCm =>
        DockMetrics.LogicalToCm(DockConfig.GetHoverRadiusMin());

    public double HoverRadiusMaxCm =>
        DockMetrics.LogicalToCm(DockConfig.GetHoverRadiusHardMax());

    public double AutoHideProximityMinCm =>
        DockMetrics.LogicalToCm(DockConfig.GetAutoHideProximityDistanceMin());

    public double AutoHideProximityMaxCm =>
        DockMetrics.LogicalToCm(DockConfig.GetAutoHideProximityDistanceMax());

    public double EffectiveDpiX { get; private set; } = 96.0;

    public bool IsReserveWorkAreaEnabled => !Config.AutoHide;

    public bool IsShowHideHotkeyEnabled => !Config.AutoHide;

    private bool _capturingHotkeyShowHide;
    private bool _capturingHotkeyShowDock;
    private bool _capturingHotkeyDisableDock;

    public bool IsDockWidthSliderEnabled => !Config.AutoAdjustDockWidth;

    public bool IsHideBehindTaskbarToggleEnabled => Config.AlwaysOnTop && Config.AutoHide;

    public SettingsWindow(DockConfig config, SettingsCategory initialCategory = SettingsCategory.General, string instanceLabel = "")
    {
        Config = config;
        _currentCategory = initialCategory;
        _instanceLabel = instanceLabel;
        InitializeComponent();
        DataContext = this;
        Config.PropertyChanged += Config_PropertyChanged;
        Config.Items.CollectionChanged += Items_CollectionChanged;
        SizeChanged += SettingsWindow_SizeChanged;
        PreviewKeyDown += SettingsWindow_PreviewKeyDown;
        Closed += SettingsWindow_Closed;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        EffectiveDpiX = VisualTreeHelper.GetDpi(this).PixelsPerInchX;
        DockMetrics.GetDpiPixelsPerInchX = () => VisualTreeHelper.GetDpi(this).PixelsPerInchX;
        RegisterNumericBindings();
        ApplyPercentSliderRanges();
        ApplyPersistedWindowSize();
        _isSizePersistenceReady = true;
        LoadSavedColorsState();
        LoadPresetState();
        RefreshTexts();
        ApplyTheme();
        SyncAllNumericBoxes();
        RefreshColorPresetSelections();
        NavigateTo(_currentCategory);
        RebuildQuickAccessTab();
        RefreshDetailsPanel();
    }

    private void ApplyPercentSliderRanges()
    {
        // Size measurements are now logical px; Min/Max are bound in XAML. Only the
        // default value (Tag) used on slider double-click is set here.
        DockHeightSlider.Tag = DockConfig.GetDockHeightDefault().ToString(CultureInfo.InvariantCulture);
        IconSizeSlider.Tag = DockConfig.GetIconSizeDefault().ToString(CultureInfo.InvariantCulture);
        IconSpacingSlider.Tag = DockConfig.GetIconSpacingDefault().ToString(CultureInfo.InvariantCulture);
        DockWidthSlider.Tag = DockConfig.GetDefaultDockWidthLogical(Config.Edge).ToString("0", CultureInfo.InvariantCulture);

        HoverRadiusSlider.Minimum = HoverRadiusMinCm;
        HoverRadiusSlider.Maximum = HoverRadiusMaxCm;
        HoverRadiusSlider.Tag = DockConfig.GetDefaultHoverEffectRadiusCm().ToString(CultureInfo.InvariantCulture);
        CornerRadiusSlider.Tag = DockConfig.GetCornerRadiusDefault().ToString(CultureInfo.InvariantCulture);
        OutlineThicknessSlider.Tag = DockConfig.GetOutlineThicknessDefault().ToString(CultureInfo.InvariantCulture);

        AutoHideProximityDistanceSlider.Tag = DockConfig.GetDefaultAutoHideProximityDistanceCm().ToString(CultureInfo.InvariantCulture);
    }

    private void RefreshDetailsPanel()
    {
        if (DetailsDockWidthPx is null)
        {
            return;
        }

        var wa = SystemParameters.WorkArea;
        var l = Config.Language;
        string Real(double logical) =>
            $"{DockMetrics.LogicalToPhysical(logical)} {LocalizationService.Get(l, "DetailsRealPxSuffix")}";

        DetailsDockWidthPx.Text = $"{Math.Round(Config.DockWidth, 0, MidpointRounding.AwayFromZero)} px";
        DetailsDockHeightPx.Text = $"{Math.Round(Config.DockHeight, 0, MidpointRounding.AwayFromZero)} px";
        DetailsIconSizePx.Text = $"{Math.Round(Config.IconSize, 0, MidpointRounding.AwayFromZero)} px";
        DetailsIconSpacingPx.Text = $"{Math.Round(Config.IconSpacing, 0, MidpointRounding.AwayFromZero)} px";
        DetailsHoverRadiusPx.Text = $"{Config.HoverEffectRadius} px";
        DetailsHoverRadiusCm.Text = $"{Config.HoverEffectRadiusCm:0.##} {LocalizationService.Get(l, "UnitCm")}";
        DetailsAutoHideProximityPx.Text = $"{Config.AutoHideProximityDistance} px";
        DetailsAutoHideProximityCm.Text = $"{Config.AutoHideProximityDistanceCm:0.##} {LocalizationService.Get(l, "UnitCm")}";
        DetailsWorkArea.Text = $"{wa.Width:0} × {wa.Height:0} px";
        DetailsScreenResolution.Text = FormatScreenResolutionLine();
        DetailsDpi.Text = $"{EffectiveDpiX:0.#} dpi — {LocalizationService.Get(l, "DpiEffective")}";

        // Rows that used to be in % now show the equivalent in real pixels (physical, by DPI).
        DetailsDockWidthPercent.Text = Real(Config.DockWidth);
        DetailsDockHeightPercent.Text = Real(Config.DockHeight);
        DetailsIconSizePercent.Text = Real(Config.IconSize);
        DetailsIconSpacingPercent.Text = Real(Config.IconSpacing);
    }

    private static string LocalizeDockEdge(AppLanguage language, DockEdge edge) =>
        edge switch
        {
            DockEdge.Top => LocalizationService.Get(language, "Top"),
            DockEdge.Bottom => LocalizationService.Get(language, "Bottom"),
            DockEdge.Left => LocalizationService.Get(language, "Left"),
            DockEdge.Right => LocalizationService.Get(language, "Right"),
            _ => edge.ToString()
        };

    private static string LocalizeButtonAlignment(AppLanguage language, ButtonAlignment alignment) =>
        alignment switch
        {
            ButtonAlignment.Start => LocalizationService.Get(language, "DetailsButtonAlignStart"),
            ButtonAlignment.Center => LocalizationService.Get(language, "Center"),
            ButtonAlignment.End => LocalizationService.Get(language, "DetailsButtonAlignEnd"),
            _ => alignment.ToString()
        };

    private string FormatScreenResolutionLine()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var hMonitor = hwnd != IntPtr.Zero
                ? User32.MonitorFromWindow(hwnd, User32.MONITOR_DEFAULTTONEAREST)
                : IntPtr.Zero;
            if (hMonitor != IntPtr.Zero)
            {
                var info = new User32.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<User32.MONITORINFO>() };
                if (User32.GetMonitorInfo(hMonitor, ref info) && info.rcMonitor.Width > 0)
                {
                    return $"{info.rcMonitor.Width} × {info.rcMonitor.Height} px";
                }
            }

            return $"{SystemParameters.PrimaryScreenWidth:0} × {SystemParameters.PrimaryScreenHeight:0} px";
        }
        catch
        {
            return $"{SystemParameters.PrimaryScreenWidth:0} × {SystemParameters.PrimaryScreenHeight:0} px";
        }
    }

    private void Config_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DockConfig.Theme))
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDarkMode)));
            ApplyTheme();
        }
        else if (e.PropertyName == nameof(DockConfig.Language))
        {
            RefreshTexts();
        }
        else if (e.PropertyName is nameof(DockConfig.SolidColorHex) or nameof(DockConfig.OutlineColorHex))
        {
            RefreshColorPresetSelections();
        }
        else if (e.PropertyName == nameof(DockConfig.Edge))
        {
            RefreshButtonAlignmentOptions();
            RebuildQuickAccessTab();
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DockWidthMax)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DockWidthMin)));
            ApplyPercentSliderRanges();
            SyncAllNumericBoxes();
        }
        else if (e.PropertyName is nameof(DockConfig.IconSize) or nameof(DockConfig.IconSpacing))
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DockWidthMin)));
            SyncAllNumericBoxes();
        }
        else if (e.PropertyName is nameof(DockConfig.AutoHideProximityDistance) or nameof(DockConfig.AutoHideProximityDistanceCm))
        {
            SyncAllNumericBoxes();
        }
        else if (e.PropertyName == nameof(DockConfig.AutoHideRevealDelayMs))
        {
            SyncAllNumericBoxes();
        }
        else if (e.PropertyName == nameof(DockConfig.AutoHideHideDelayMs))
        {
            SyncAllNumericBoxes();
        }
        else if (e.PropertyName is nameof(DockConfig.AutoHide) or nameof(DockConfig.AlwaysOnTop))
        {
            RebuildQuickAccessTab();
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsHideBehindTaskbarToggleEnabled)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsReserveWorkAreaEnabled)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsShowHideHotkeyEnabled)));
        }
        else if (e.PropertyName == nameof(DockConfig.EnableHoverEffects))
        {
            RebuildQuickAccessTab();
        }
        else if (e.PropertyName is nameof(DockConfig.ShowOutline) or nameof(DockConfig.RoundedCorners))
        {
            RebuildQuickAccessTab();
        }
        else if (e.PropertyName == nameof(DockConfig.AutoAdjustDockWidth))
        {
            RebuildQuickAccessTab();
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDockWidthSliderEnabled)));
            SyncAllNumericBoxes();
        }
        else if (e.PropertyName == nameof(DockConfig.DockDisabled))
        {
            RefreshDisableDockButton();
        }

        if (_presetStateReady && !_isApplyingPresetData && IsPresetTrackedProperty(e.PropertyName))
        {
            SyncCurrentPresetSelectionFromCurrentValues();
        }

        if (e.PropertyName is not null &&
            (IsSizeDetailProperty(e.PropertyName) || e.PropertyName == nameof(DockConfig.Offset) ||
             e.PropertyName == nameof(DockConfig.Edge)))
        {
            RefreshDetailsPanel();
        }

        RefreshPinButtons();
    }

    private void Items_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DockWidthMin)));
        SyncAllNumericBoxes();
        RefreshDetailsPanel();
    }

    private static MainWindow? FindDockWindow() =>
        System.Windows.Application.Current.Windows.OfType<MainWindow>().FirstOrDefault();

    // ---------------------------------------------------------------------
    // Monitor picker
    // ---------------------------------------------------------------------

    private bool _suppressMonitorSelection;

    private void RefreshMonitorOptions()
    {
        _suppressMonitorSelection = true;
        try
        {
            var options = new List<MonitorInfo>
            {
                new()
                {
                    DeviceName = string.Empty,
                    FriendlyName = LocalizationService.Get(Config.Language, "MonitorPrimaryDefault"),
                    IsPrimary = true
                }
            };
            options.AddRange(DisplayService.GetMonitors());
            MonitorSelectCombo.ItemsSource = options;
            MonitorSelectCombo.SelectedValue = Config.MonitorDeviceName ?? string.Empty;
            if (MonitorSelectCombo.SelectedItem is null)
            {
                MonitorSelectCombo.SelectedValue = string.Empty;
            }
        }
        finally
        {
            _suppressMonitorSelection = false;
        }
    }

    private void MonitorSelectCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressMonitorSelection)
        {
            return;
        }

        var device = MonitorSelectCombo.SelectedValue as string;
        Config.MonitorDeviceName = string.IsNullOrEmpty(device) ? null : device;
    }

    // ---------------------------------------------------------------------
    // Hotkeys
    // ---------------------------------------------------------------------

    private void RefreshHotkeyButtonLabels()
    {
        HotkeyShowHideButton.Content = _capturingHotkeyShowHide
            ? LocalizationService.Get(Config.Language, "HotkeyPressKeys")
            : FormatHotkey(Config.ShowHideHotkey);
        HotkeyShowDockButton.Content = _capturingHotkeyShowDock
            ? LocalizationService.Get(Config.Language, "HotkeyPressKeys")
            : FormatHotkey(Config.ShowDockHotkey);
        HotkeyDisableDockButton.Content = _capturingHotkeyDisableDock
            ? LocalizationService.Get(Config.Language, "HotkeyPressKeys")
            : FormatHotkey(Config.DisableDockHotkey);
    }

    private string FormatHotkey(HotkeyConfig hotkey)
    {
        if (!hotkey.IsAssigned)
        {
            return LocalizationService.Get(Config.Language, "HotkeyNotSet");
        }

        var parts = new List<string>();
        if ((hotkey.Modifiers & 2) != 0) parts.Add("Ctrl");
        if ((hotkey.Modifiers & 1) != 0) parts.Add("Alt");
        if ((hotkey.Modifiers & 4) != 0) parts.Add("Shift");
        if ((hotkey.Modifiers & 8) != 0) parts.Add("Win");
        var key = KeyInterop.KeyFromVirtualKey(hotkey.Key);
        parts.Add(key.ToString());
        return string.Join(" + ", parts);
    }

    private void HotkeyCapture_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string which })
        {
            return;
        }

        _capturingHotkeyShowHide = which == "ShowHide";
        _capturingHotkeyShowDock = which == "ShowDock";
        _capturingHotkeyDisableDock = which == "DisableDock";
        RefreshHotkeyButtonLabels();
    }

    private void HotkeyClear_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string which })
        {
            return;
        }

        var hotkey = GetHotkeyByTag(which);
        hotkey.Modifiers = 0;
        hotkey.Key = 0;
        StopHotkeyCapture();
        FindDockWindow()?.RefreshHotkeys();
        RefreshHotkeyButtonLabels();
        FindDockWindow()?.SaveNow();
    }

    private void SettingsWindow_PreviewKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (!_capturingHotkeyShowHide && !_capturingHotkeyShowDock && !_capturingHotkeyDisableDock)
        {
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or
            Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            e.Handled = true;
            return;
        }

        if (key == Key.Escape)
        {
            StopHotkeyCapture();
            RefreshHotkeyButtonLabels();
            e.Handled = true;
            return;
        }

        var mods = 0;
        var m = Keyboard.Modifiers;
        if ((m & ModifierKeys.Alt) != 0) mods |= 1;
        if ((m & ModifierKeys.Control) != 0) mods |= 2;
        if ((m & ModifierKeys.Shift) != 0) mods |= 4;
        if ((m & ModifierKeys.Windows) != 0) mods |= 8;

        var hotkey = GetCapturingHotkey();
        hotkey.Modifiers = mods;
        hotkey.Key = KeyInterop.VirtualKeyFromKey(key);

        StopHotkeyCapture();
        FindDockWindow()?.RefreshHotkeys();
        RefreshHotkeyButtonLabels();
        FindDockWindow()?.SaveNow();
        e.Handled = true;
    }

    private HotkeyConfig GetHotkeyByTag(string which) => which switch
    {
        "ShowDock" => Config.ShowDockHotkey,
        "DisableDock" => Config.DisableDockHotkey,
        _ => Config.ShowHideHotkey
    };

    private HotkeyConfig GetCapturingHotkey()
    {
        if (_capturingHotkeyShowDock)
        {
            return Config.ShowDockHotkey;
        }

        if (_capturingHotkeyDisableDock)
        {
            return Config.DisableDockHotkey;
        }

        return Config.ShowHideHotkey;
    }

    private void StopHotkeyCapture()
    {
        _capturingHotkeyShowHide = false;
        _capturingHotkeyShowDock = false;
        _capturingHotkeyDisableDock = false;
    }

    // ---------------------------------------------------------------------
    // Add shortcuts
    // ---------------------------------------------------------------------

    private void AddShortcutButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Multiselect = true,
            Filter = LocalizationService.Get(Config.Language, "AddShortcutFileFilter")
        };
        if (dlg.ShowDialog() != true)
        {
            return;
        }

        FindDockWindow()?.AddShortcutsFromPaths(dlg.FileNames);
    }

    // ---------------------------------------------------------------------
    // Apps UWP / Store
    // ---------------------------------------------------------------------

    private void AddUwpAppButton_Click(object sender, RoutedEventArgs e)
    {
        var apps = UwpAppService.GetInstalledApps();
        if (apps.Count == 0)
        {
            System.Windows.MessageBox.Show(this, LocalizationService.Get(Config.Language, "NoUwpAppsFound"), "ToolDock",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var picker = new UwpAppPickerWindow(apps, Config.Language) { Owner = this };
        if (picker.ShowDialog() == true && picker.SelectedApp is { } app)
        {
            FindDockWindow()?.AddUwpApp(app.Aumid, app.Name);
        }
    }

    // ---------------------------------------------------------------------
    // Detailed log
    // ---------------------------------------------------------------------

    private void GenerateDetailedLogButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dock = FindDockWindow();
            var report = dock?.BuildDiagnosticReport() ?? "Dock window not available.";
            var exeDir = AppPaths.DataDirectory;
            var fileName = $"ToolDock_Log_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
            var path = Path.Combine(exeDir, fileName);
            File.WriteAllText(path, report);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, ex.Message, "ToolDock", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SettingsWindow_Closed(object? sender, EventArgs e)
    {
        Config.PropertyChanged -= Config_PropertyChanged;
        Config.Items.CollectionChanged -= Items_CollectionChanged;
        SizeChanged -= SettingsWindow_SizeChanged;
        Closed -= SettingsWindow_Closed;

        if (System.Windows.Application.Current.Windows.OfType<MainWindow>().FirstOrDefault() is { } main)
        {
            main.SaveNow();
        }
    }

    private void SettingsWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_isSizePersistenceReady)
        {
            return;
        }

        if (WindowState != WindowState.Normal)
        {
            return;
        }

        Config.SettingsWindowWidth = ActualWidth;
        Config.SettingsWindowHeight = ActualHeight;
    }

    private void ApplyPersistedWindowSize()
    {
        Width = Config.SettingsWindowWidth;
        Height = Config.SettingsWindowHeight;
    }

    private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RefreshTexts();
    }

    public void NavigateTo(SettingsCategory category)
    {
        _currentCategory = category;
        InfoPanel.Visibility = category == SettingsCategory.Info ? Visibility.Visible : Visibility.Collapsed;
        GeneralPanel.Visibility = category == SettingsCategory.General ? Visibility.Visible : Visibility.Collapsed;
        QuickAccessPanel.Visibility = category == SettingsCategory.QuickAccess ? Visibility.Visible : Visibility.Collapsed;
        PositionPanel.Visibility = category == SettingsCategory.Position ? Visibility.Visible : Visibility.Collapsed;
        AppearancePanel.Visibility = category == SettingsCategory.Appearance ? Visibility.Visible : Visibility.Collapsed;
        ShortcutsPanel.Visibility = category == SettingsCategory.Shortcuts ? Visibility.Visible : Visibility.Collapsed;
        BehaviourPanel.Visibility = category == SettingsCategory.Behaviour ? Visibility.Visible : Visibility.Collapsed;
        DetailsPanel.Visibility = category == SettingsCategory.Details ? Visibility.Visible : Visibility.Collapsed;
        TroubleshootingPanel.Visibility = category == SettingsCategory.Troubleshooting ? Visibility.Visible : Visibility.Collapsed;
        HighlightCategoryButton(InfoTabButton, category == SettingsCategory.Info);
        HighlightCategoryButton(GeneralTabButton, category == SettingsCategory.General);
        HighlightCategoryButton(QuickAccessTabButton, category == SettingsCategory.QuickAccess);
        HighlightCategoryButton(PositionTabButton, category == SettingsCategory.Position);
        HighlightCategoryButton(AppearanceTabButton, category == SettingsCategory.Appearance);
        HighlightCategoryButton(ShortcutsTabButton, category == SettingsCategory.Shortcuts);
        HighlightCategoryButton(BehaviourTabButton, category == SettingsCategory.Behaviour);
        HighlightCategoryButton(DetailsTabButton, category == SettingsCategory.Details);
        HighlightCategoryButton(TroubleshootingTabButton, category == SettingsCategory.Troubleshooting);
        UpdatePageHeader();
        if (category == SettingsCategory.Details)
        {
            RefreshDetailsPanel();
        }
    }

    private void CategoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button || button.Tag is not string categoryTag)
        {
            return;
        }

        if (!Enum.TryParse<SettingsCategory>(categoryTag, out var parsed))
        {
            return;
        }

        NavigateTo(parsed);
    }

    private void SaveShortcutsButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            InitialDirectory = DockShortcutsFileService.GetExecutableDirectory(),
            FileName = DockShortcutsFileService.DefaultFileName,
            DefaultExt = ".json",
            Filter = "JSON (*.json)|*.json|All files (*.*)|*.*",
            AddExtension = true
        };

        if (dlg.ShowDialog() != true)
        {
            return;
        }

        var l = Config.Language;
        try
        {
            DockShortcutsFileService.SaveToFile(Config, dlg.FileName);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                LocalizationService.Get(l, "ShortcutsFileErrorWrite") + "\n" + ex.Message,
                LocalizationService.Get(l, "ShortcutsFileErrorTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void LoadShortcutsButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            InitialDirectory = DockShortcutsFileService.GetExecutableDirectory(),
            DefaultExt = ".json",
            Filter = "JSON (*.json)|*.json|All files (*.*)|*.*"
        };

        if (dlg.ShowDialog() != true)
        {
            return;
        }

        var l = Config.Language;
        string json;
        try
        {
            json = File.ReadAllText(dlg.FileName);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                LocalizationService.Get(l, "ShortcutsFileErrorRead") + "\n" + ex.Message,
                LocalizationService.Get(l, "ShortcutsFileErrorTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        if (!DockShortcutsFileService.TryLoad(json, out var export, out var errorKey) || export is null)
        {
            var msg = errorKey is not null
                ? LocalizationService.Get(l, errorKey)
                : LocalizationService.Get(l, "ShortcutsFileErrorInvalidStructure");
            System.Windows.MessageBox.Show(
                msg,
                LocalizationService.Get(l, "ShortcutsFileErrorTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        export.ApplyTo(Config);
        SyncAllNumericBoxes();
        RefreshDetailsPanel();
        if (System.Windows.Application.Current.Windows.OfType<MainWindow>().FirstOrDefault() is { } main)
        {
            main.ReloadDock();
            main.SaveNow();
        }
    }

    private void RemoveAllShortcutsButton_Click(object sender, RoutedEventArgs e)
    {
        var l = Config.Language;
        var confirm = System.Windows.MessageBox.Show(
            LocalizationService.Get(l, "RemoveAllShortcutsConfirmMessage"),
            LocalizationService.Get(l, "RemoveAllShortcutsConfirmTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        Config.Items.Clear();
        SyncAllNumericBoxes();
        RefreshDetailsPanel();
        if (System.Windows.Application.Current.Windows.OfType<MainWindow>().FirstOrDefault() is { } main)
        {
            main.ReloadDock();
            main.SaveNow();
        }
    }

    private void ResetCategoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button || button.Tag is not string categoryTag)
        {
            return;
        }

        if (!Enum.TryParse<SettingsCategory>(categoryTag, out var parsed))
        {
            return;
        }

        switch (parsed)
        {
            case SettingsCategory.General:
                Config.ResetGeneral();
                break;
            case SettingsCategory.Position:
                ApplyCategoryFromResolvedPreset(SettingsCategory.Position);
                break;
            case SettingsCategory.Appearance:
                ApplyCategoryFromResolvedPreset(SettingsCategory.Appearance);
                break;
            case SettingsCategory.Shortcuts:
                ApplyCategoryFromResolvedPreset(SettingsCategory.Shortcuts);
                break;
            case SettingsCategory.Behaviour:
                ApplyCategoryFromResolvedPreset(SettingsCategory.Behaviour);
                break;
            case SettingsCategory.QuickAccess:
                ResetPinnedQuickAccessOptions(GetVisiblePinnedOptions().ToList());
                break;
        }

        SyncAllNumericBoxes();
        RefreshColorPresetSelections();
        RebuildQuickAccessTab();
        RefreshPinButtons();
    }

    private void PinOption_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button || button.Tag is not string rawTag)
        {
            return;
        }

        if (!rawTag.StartsWith("Pin:", StringComparison.Ordinal))
        {
            return;
        }

        var optionKey = rawTag["Pin:".Length..];
        if (!_supportedPins.Contains(optionKey))
        {
            return;
        }

        if (Config.QuickAccessPinnedOptions.Contains(optionKey))
        {
            Config.QuickAccessPinnedOptions.Remove(optionKey);
        }
        else
        {
            Config.QuickAccessPinnedOptions.Add(optionKey);
        }

        RebuildQuickAccessTab();
        RefreshPinButtons();
    }

    private void UnpinOption_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button || button.Tag is not string optionKey)
        {
            return;
        }

        Config.QuickAccessPinnedOptions.Remove(optionKey);
        RebuildQuickAccessTab();
        RefreshPinButtons();
    }

    private void SolidPresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SolidPresetCombo.SelectedItem is not ComboBoxItem comboItem)
        {
            return;
        }

        if (comboItem.Tag is not string hex)
        {
            return;
        }

        var isCustom = string.Equals(hex, "custom", StringComparison.OrdinalIgnoreCase);
        if (isCustom)
        {
            UpdateColorEditorStates();
            return;
        }

        var parsedHex = ExtractHexFromPresetTag(hex);
        if (parsedHex is null)
        {
            return;
        }

        Config.SolidColorHex = parsedHex;
        RefreshColorPresetSelections();
    }

    private void OutlinePresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (OutlinePresetCombo.SelectedItem is not ComboBoxItem comboItem)
        {
            return;
        }

        if (comboItem.Tag is not string hex)
        {
            return;
        }

        var isCustom = string.Equals(hex, "custom", StringComparison.OrdinalIgnoreCase);
        if (isCustom)
        {
            UpdateColorEditorStates();
            return;
        }

        var parsedHex = ExtractHexFromPresetTag(hex);
        if (parsedHex is null)
        {
            return;
        }

        Config.OutlineColorHex = parsedHex;
        RefreshColorPresetSelections();
    }

    private void ColorHexTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUpdatingColorHexText || sender is not WpfTextBox textBox)
        {
            return;
        }

        var normalized = NormalizeHexInput(textBox.Text);
        if (!string.Equals(normalized, textBox.Text, StringComparison.Ordinal))
        {
            var caret = Math.Max(1, textBox.CaretIndex);
            _isUpdatingColorHexText = true;
            textBox.Text = normalized;
            textBox.CaretIndex = Math.Min(textBox.Text.Length, caret);
            _isUpdatingColorHexText = false;
        }

        if (IsValidHexColor(textBox.Text))
        {
            _lastValidCustomHex = textBox.Text;
        }
    }

    private void ColorHexTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfTextBox textBox)
        {
            return;
        }

        if (IsValidHexColor(textBox.Text))
        {
            return;
        }

        _isUpdatingColorHexText = true;
        textBox.Text = _lastValidCustomHex;
        textBox.CaretIndex = textBox.Text.Length;
        _isUpdatingColorHexText = false;
    }

    private void ColorHexTextBox_KeyDown(object sender, WpfKeyEventArgs e)
    {
        if (sender is not WpfTextBox textBox)
        {
            return;
        }

        if (e.Key == Key.Back && textBox.SelectionLength == 0 && textBox.CaretIndex <= 1)
        {
            e.Handled = true;
        }
        else if (e.Key == Key.Delete && textBox.SelectionLength == 0 && textBox.CaretIndex == 0)
        {
            e.Handled = true;
        }
    }

    private void ColorHexTextBox_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (sender is not WpfTextBox textBox)
        {
            return;
        }

        if (!e.DataObject.GetDataPresent(typeof(string)))
        {
            e.CancelCommand();
            return;
        }

        var pasted = e.DataObject.GetData(typeof(string)) as string ?? string.Empty;
        _isUpdatingColorHexText = true;
        textBox.Text = NormalizeHexInput(pasted);
        textBox.CaretIndex = textBox.Text.Length;
        _isUpdatingColorHexText = false;
        e.CancelCommand();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void LoadPresetState()
    {
        _presetStateReady = false;
        _presets.Clear();
        _presets.AddRange(_presetService.LoadCustomPresets());
        _selectedPresetName = string.IsNullOrWhiteSpace(Config.ActivePresetName)
            ? DefaultPresetName
            : Config.ActivePresetName;
        _lastNonCustomPresetName = string.IsNullOrWhiteSpace(Config.LastNonCustomPresetName)
            ? DefaultPresetName
            : Config.LastNonCustomPresetName;
        _presetStateReady = true;

        if (_presetService.LastLoadFailed)
        {
            RebuildPresetCombo(_selectedPresetName);
            UpdatePresetUiState();
            return;
        }

        SyncCurrentPresetSelectionFromCurrentValues();
    }

    private void RebuildPresetCombo(string? selectedName = null)
    {
        _isPresetSelectionUpdating = true;
        PresetCombo.Items.Clear();
        PresetCombo.Items.Add(new ComboBoxItem
        {
            Content = LocalizationService.Get(Config.Language, "PresetDefault"),
            Tag = DefaultPresetName
        });
        foreach (var preset in _presets)
        {
            PresetCombo.Items.Add(new ComboBoxItem
            {
                Content = preset.Name,
                Tag = preset.Name
            });
        }
        PresetCombo.Items.Add(new ComboBoxItem
        {
            Content = LocalizationService.Get(Config.Language, "PresetCustom"),
            Tag = CustomPresetName
        });

        var nameToSelect = string.IsNullOrWhiteSpace(selectedName)
            ? _selectedPresetName
            : selectedName;
        if (!string.IsNullOrWhiteSpace(nameToSelect))
        {
            var match = PresetCombo.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item => item.Tag is string tag && string.Equals(tag, nameToSelect, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                // The presets file could not be read: keep the name shown on screen.
                match = new ComboBoxItem
                {
                    Content = nameToSelect,
                    Tag = nameToSelect
                };
                var insertAt = Math.Max(0, PresetCombo.Items.Count - 1);
                PresetCombo.Items.Insert(insertAt, match);
            }

            PresetCombo.SelectedItem = match;
        }

        _isPresetSelectionUpdating = false;
    }

    private void PresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isPresetSelectionUpdating || PresetCombo.SelectedItem is not ComboBoxItem { Tag: string selectedName })
        {
            return;
        }

        if (string.Equals(selectedName, CustomPresetName, StringComparison.OrdinalIgnoreCase))
        {
            CommitPresetSelection(selectedName, updateLastNonCustom: false);
            UpdatePresetUiState();
            return;
        }

        ApplyPresetByName(selectedName);
    }

    private void SavePresetButton_Click(object sender, RoutedEventArgs e)
    {
        var presetName = NormalizeName(PresetNameTextBox.Text);
        if (string.IsNullOrWhiteSpace(presetName))
        {
            return;
        }

        if (string.Equals(presetName, DefaultPresetName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(presetName, CustomPresetName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var existing = _presets.FirstOrDefault(p => string.Equals(p.Name, presetName, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            _presets.Add(new DockPreset
            {
                Name = presetName,
                Data = DockPresetData.FromConfig(Config)
            });
        }
        else
        {
            var confirm = System.Windows.MessageBox.Show(
                this,
                LocalizationService.Get(Config.Language, "PresetOverwriteConfirmMessage"),
                LocalizationService.Get(Config.Language, "PresetOverwriteConfirmTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }

            existing.Name = presetName;
            existing.Data = DockPresetData.FromConfig(Config);
        }

        _presetService.SaveCustomPresets(_presets);
        CommitPresetSelection(presetName, updateLastNonCustom: true);
        PresetNameTextBox.Clear();
        RebuildPresetCombo(presetName);
        UpdatePresetUiState();
    }

    private void DeletePresetButton_Click(object sender, RoutedEventArgs e)
    {
        var presetName = PresetCombo.SelectedItem is ComboBoxItem { Tag: string tag } ? tag : null;
        if (string.IsNullOrWhiteSpace(presetName) ||
            string.Equals(presetName, DefaultPresetName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(presetName, CustomPresetName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _presets.RemoveAll(p => string.Equals(p.Name, presetName, StringComparison.OrdinalIgnoreCase));
        _presetService.SaveCustomPresets(_presets);
        ApplyPresetByName(DefaultPresetName);
    }

    private void ResetPresetButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyPresetByName(ResolvePresetNameForResetOperations());
    }

    private void ApplyPresetByName(string presetName)
    {
        if (string.Equals(presetName, CustomPresetName, StringComparison.OrdinalIgnoreCase))
        {
            CommitPresetSelection(presetName, updateLastNonCustom: false);
            RebuildPresetCombo(_selectedPresetName);
            UpdatePresetUiState();
            return;
        }

        var data = GetPresetDataByName(presetName);
        if (data is null)
        {
            if (_presetService.LastLoadFailed)
            {
                return;
            }

            presetName = DefaultPresetName;
            data = SanitizePresetDataColors(DockPresetData.CreateDefault());
        }

        _isApplyingPresetData = true;
        try
        {
            data.ApplyTo(Config);
            EnsureActiveColorsAreAvailable();
        }
        finally
        {
            _isApplyingPresetData = false;
        }

        SyncAllNumericBoxes();
        RefreshColorPresetSelections();
        RebuildQuickAccessTab();
        CommitPresetSelection(presetName, updateLastNonCustom: true);
        RebuildPresetCombo(_selectedPresetName);
        UpdatePresetUiState();
    }

    private void CommitPresetSelection(string selectedName, bool updateLastNonCustom)
    {
        _selectedPresetName = selectedName;
        if (updateLastNonCustom &&
            !string.Equals(selectedName, CustomPresetName, StringComparison.OrdinalIgnoreCase))
        {
            _lastNonCustomPresetName = selectedName;
        }

        if (!string.Equals(Config.ActivePresetName, _selectedPresetName, StringComparison.OrdinalIgnoreCase))
        {
            Config.ActivePresetName = _selectedPresetName;
        }

        if (!string.Equals(Config.LastNonCustomPresetName, _lastNonCustomPresetName, StringComparison.OrdinalIgnoreCase))
        {
            Config.LastNonCustomPresetName = _lastNonCustomPresetName;
        }
    }

    private void UpdatePresetUiState()
    {
        EnsurePresetListStillAvailable();
        var selected = PresetCombo.SelectedItem is ComboBoxItem { Tag: string tag } ? tag : _selectedPresetName;
        var isCustom = string.Equals(selected, CustomPresetName, StringComparison.OrdinalIgnoreCase);
        var canDelete = !string.Equals(selected, DefaultPresetName, StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(selected, CustomPresetName, StringComparison.OrdinalIgnoreCase);
        SavePresetPanel.IsEnabled = isCustom;
        DeletePresetButton.IsEnabled = canDelete;
    }

    private void SyncCurrentPresetSelectionFromCurrentValues()
    {
        if (!_presetStateReady || _syncingPreset)
        {
            return;
        }

        _syncingPreset = true;
        try
        {
            EnsurePresetListStillAvailable();
            var currentData = DockPresetData.FromConfig(Config);
            var resolved = ResolvePresetNameFromValues(currentData);
            var updateLast = !string.Equals(resolved, CustomPresetName, StringComparison.OrdinalIgnoreCase);
            CommitPresetSelection(resolved, updateLast);

            var comboName = PresetCombo.SelectedItem is ComboBoxItem { Tag: string tag } ? tag : null;
            if (!string.Equals(comboName, _selectedPresetName, StringComparison.OrdinalIgnoreCase))
            {
                RebuildPresetCombo(_selectedPresetName);
                UpdatePresetUiState();
            }
        }
        finally
        {
            _syncingPreset = false;
        }
    }

    private string ResolvePresetNameFromValues(DockPresetData current)
    {
        var strict = CollectMatchingPresetNames(current, ignoreDockWidth: false);
        if (TryPickPresetMatch(strict, out var strictName))
        {
            return strictName;
        }

        var relaxed = CollectMatchingPresetNames(current, ignoreDockWidth: true);
        if (relaxed.Count == 0)
        {
            return CustomPresetName;
        }

        var bestDelta = relaxed.Min(name => DockWidthDelta(current, name));
        var tied = relaxed
            .Where(name => Math.Abs(DockWidthDelta(current, name) - bestDelta) < 0.001)
            .ToList();
        return TryPickPresetMatch(tied, out var relaxedName) ? relaxedName : CustomPresetName;
    }

    private List<string> CollectMatchingPresetNames(DockPresetData current, bool ignoreDockWidth)
    {
        var matches = new List<string>();
        if (ArePresetDataEquivalent(current, DockPresetData.CreateDefault(), ignoreDockWidth))
        {
            matches.Add(DefaultPresetName);
        }

        foreach (var preset in _presets)
        {
            if (ArePresetDataEquivalent(current, preset.Data, ignoreDockWidth))
            {
                matches.Add(preset.Name);
            }
        }

        return matches;
    }

    private bool TryPickPresetMatch(List<string> matches, out string name)
    {
        if (matches.Count == 0)
        {
            name = CustomPresetName;
            return false;
        }

        var selected = matches.FirstOrDefault(candidate =>
            string.Equals(candidate, _selectedPresetName, StringComparison.OrdinalIgnoreCase));
        if (selected is not null)
        {
            name = selected;
            return true;
        }

        var last = matches.FirstOrDefault(candidate =>
            string.Equals(candidate, _lastNonCustomPresetName, StringComparison.OrdinalIgnoreCase));
        if (last is not null)
        {
            name = last;
            return true;
        }

        name = matches.FirstOrDefault(candidate =>
                   !string.Equals(candidate, DefaultPresetName, StringComparison.OrdinalIgnoreCase))
               ?? matches[0];
        return true;
    }

    private double DockWidthDelta(DockPresetData current, string presetName)
    {
        var data = string.Equals(presetName, DefaultPresetName, StringComparison.OrdinalIgnoreCase)
            ? DockPresetData.CreateDefault()
            : _presets.FirstOrDefault(p => string.Equals(p.Name, presetName, StringComparison.OrdinalIgnoreCase))?.Data;
        return data is null ? double.MaxValue : Math.Abs(current.DockWidth - data.DockWidth);
    }

    private static bool ArePresetDataEquivalent(DockPresetData a, DockPresetData b, bool ignoreDockWidth = false)
    {
        var skipWidth = ignoreDockWidth || a.AutoAdjustDockWidth || b.AutoAdjustDockWidth;
        return a.Edge == b.Edge &&
               a.AutoAdjustDockWidth == b.AutoAdjustDockWidth &&
               (skipWidth || Math.Abs(a.DockWidth - b.DockWidth) < 0.001) &&
               Math.Abs(a.DockHeight - b.DockHeight) < 0.001 &&
               Math.Abs(a.Offset - b.Offset) < 0.001 &&
               Math.Abs(a.AlignmentOffset - b.AlignmentOffset) < 0.001 &&
               a.DockOpacity == b.DockOpacity &&
               a.RoundedCorners == b.RoundedCorners &&
               Math.Abs(a.CornerRadius - b.CornerRadius) < 0.001 &&
               Math.Abs(a.OutlineThickness - b.OutlineThickness) < 0.001 &&
               string.Equals(NormalizeHexInput(a.SolidColorHex), NormalizeHexInput(b.SolidColorHex), StringComparison.OrdinalIgnoreCase) &&
               a.ShowOutline == b.ShowOutline &&
               a.OutlineEdgeTop == b.OutlineEdgeTop &&
               a.OutlineEdgeBottom == b.OutlineEdgeBottom &&
               a.OutlineEdgeLeft == b.OutlineEdgeLeft &&
               a.OutlineEdgeRight == b.OutlineEdgeRight &&
               string.Equals(NormalizeHexInput(a.OutlineColorHex), NormalizeHexInput(b.OutlineColorHex), StringComparison.OrdinalIgnoreCase) &&
               a.OutlineOpacity == b.OutlineOpacity &&
               Math.Abs(a.IconSize - b.IconSize) < 0.001 &&
               Math.Abs(a.IconSpacing - b.IconSpacing) < 0.001 &&
               a.HighQualityIcons == b.HighQualityIcons &&
               a.ButtonAlignment == b.ButtonAlignment &&
               a.EnableHoverEffects == b.EnableHoverEffects &&
               a.HoverMagnification == b.HoverMagnification &&
               a.HoverEffectRadius == b.HoverEffectRadius &&
               a.AutoHide == b.AutoHide &&
               a.AutoHideSlideAnimation == b.AutoHideSlideAnimation &&
               a.AutoHideSlideDurationMs == b.AutoHideSlideDurationMs &&
               a.AutoHideRevealDelayMs == b.AutoHideRevealDelayMs &&
               a.AutoHideHideDelayMs == b.AutoHideHideDelayMs &&
               a.AutoHideRevealMode == b.AutoHideRevealMode &&
               a.AutoHideProximityDistance == b.AutoHideProximityDistance &&
               a.AlwaysOnTop == b.AlwaysOnTop &&
               a.HideBehindTaskbar == b.HideBehindTaskbar &&
               a.ReserveWorkArea == b.ReserveWorkArea &&
               a.DoNotShowOverFullscreen == b.DoNotShowOverFullscreen &&
               a.OpenAppsOnDockMonitor == b.OpenAppsOnDockMonitor &&
               a.HideWhenMaximized == b.HideWhenMaximized &&
               a.HideWhenWindowIntersects == b.HideWhenWindowIntersects &&
               a.RoundedCornerTopLeft == b.RoundedCornerTopLeft &&
               a.RoundedCornerTopRight == b.RoundedCornerTopRight &&
               a.RoundedCornerBottomLeft == b.RoundedCornerBottomLeft &&
               a.RoundedCornerBottomRight == b.RoundedCornerBottomRight;
    }

    private static bool IsPresetTrackedProperty(string? propertyName)
    {
        return propertyName is nameof(DockConfig.Edge) or nameof(DockConfig.AutoAdjustDockWidth) or nameof(DockConfig.DockWidth) or nameof(DockConfig.DockHeight) or nameof(DockConfig.Offset) or
               nameof(DockConfig.AlignmentOffset) or
               nameof(DockConfig.DockOpacity) or nameof(DockConfig.RoundedCorners) or nameof(DockConfig.SolidColorHex) or nameof(DockConfig.ShowOutline) or
               nameof(DockConfig.RoundedCornerTopLeft) or nameof(DockConfig.RoundedCornerTopRight) or nameof(DockConfig.RoundedCornerBottomLeft) or nameof(DockConfig.RoundedCornerBottomRight) or
               nameof(DockConfig.OutlineEdgeTop) or nameof(DockConfig.OutlineEdgeBottom) or nameof(DockConfig.OutlineEdgeLeft) or nameof(DockConfig.OutlineEdgeRight) or
               nameof(DockConfig.OutlineColorHex) or nameof(DockConfig.OutlineOpacity) or nameof(DockConfig.OutlineThickness) or
               nameof(DockConfig.CornerRadius) or nameof(DockConfig.IconSize) or nameof(DockConfig.IconSpacing) or nameof(DockConfig.HighQualityIcons) or
               nameof(DockConfig.ButtonAlignment) or nameof(DockConfig.EnableHoverEffects) or nameof(DockConfig.HoverMagnification) or nameof(DockConfig.HoverEffectRadius) or
               nameof(DockConfig.AutoHide) or nameof(DockConfig.AutoHideSlideAnimation) or nameof(DockConfig.AutoHideSlideDurationMs) or
               nameof(DockConfig.AutoHideRevealDelayMs) or nameof(DockConfig.AutoHideHideDelayMs) or nameof(DockConfig.AutoHideRevealMode) or
               nameof(DockConfig.AutoHideProximityDistance) or nameof(DockConfig.AlwaysOnTop) or
               nameof(DockConfig.HideBehindTaskbar) or nameof(DockConfig.ReserveWorkArea) or nameof(DockConfig.DoNotShowOverFullscreen) or
               nameof(DockConfig.OpenAppsOnDockMonitor) or nameof(DockConfig.HideWhenMaximized) or
               nameof(DockConfig.HideWhenWindowIntersects);
    }

    private static bool IsSizeDetailProperty(string propertyName) =>
        propertyName is nameof(DockConfig.HoverEffectRadius) or
        nameof(DockConfig.AutoHideProximityDistance) or
        nameof(DockConfig.OutlineThickness) or nameof(DockConfig.ShowOutline) or
        nameof(DockConfig.DockWidth) or nameof(DockConfig.DockHeight) or nameof(DockConfig.IconSize) or nameof(DockConfig.IconSpacing);

    private void LoadSavedColorsState()
    {
        _savedColors.Clear();
        _savedColors.AddRange(GetBuiltInColors());
        _savedColors.AddRange(_customColorService.LoadCustomColors());
        EnsureActiveColorsAreAvailable();
        RebuildSavedColorCombos();
        _lastValidCustomHex = HexTextBox.Text;
    }

    private void RebuildSavedColorCombos(string? selectedName = null)
    {
        _isSavedColorSelectionUpdating = true;
        CustomSavedColorsCombo.Items.Clear();
        RefreshPresetCombosWithCustomColors();

        foreach (var color in _savedColors.Where(c => !c.IsBuiltIn).OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
        {
            CustomSavedColorsCombo.Items.Add(color);
        }

        if (!string.IsNullOrWhiteSpace(selectedName))
        {
            var selected = _savedColors.FirstOrDefault(c => string.Equals(c.Name, selectedName, StringComparison.OrdinalIgnoreCase));
            if (selected is { IsBuiltIn: false })
            {
                CustomSavedColorsCombo.SelectedItem = selected;
            }
        }

        _isSavedColorSelectionUpdating = false;
        UpdateColorEditorStates();
    }

    private void RefreshPresetCombosWithCustomColors()
    {
        AddCustomColorsToPresetCombo(SolidPresetCombo);
        AddCustomColorsToPresetCombo(OutlinePresetCombo);
    }

    private void AddCustomColorsToPresetCombo(WpfComboBox combo)
    {
        var toRemove = combo.Items
            .OfType<ComboBoxItem>()
            .Where(item => item.Tag is string tag && tag.StartsWith("custom:", StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var removable in toRemove)
        {
            combo.Items.Remove(removable);
        }

        var insertIndex = combo.Items.Count;
        foreach (var color in _savedColors.Where(c => !c.IsBuiltIn).OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
        {
            combo.Items.Insert(insertIndex++, new ComboBoxItem
            {
                Content = color.Name,
                Tag = $"custom:{color.Hex}"
            });
        }
    }

    private void CustomSavedColorsCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSavedColorSelectionUpdating || CustomSavedColorsCombo.SelectedItem is not NamedHexColor selected)
        {
            return;
        }

        HexTextBox.Text = selected.Hex;
        _lastValidCustomHex = selected.Hex;
        DeleteCustomColorButton.IsEnabled = true;
    }

    private void SaveCustomColorButton_Click(object sender, RoutedEventArgs e)
    {
        SaveNamedColor(CustomColorNameTextBox.Text, HexTextBox.Text);
    }

    private void DeleteCustomColorButton_Click(object sender, RoutedEventArgs e)
    {
        if (CustomSavedColorsCombo.SelectedItem is not NamedHexColor selected)
        {
            return;
        }

        DeleteNamedColor(selected.Name);
    }

    private void SaveNamedColor(string rawName, string hex)
    {
        var colorName = NormalizeName(rawName);
        if (string.IsNullOrWhiteSpace(colorName) || !IsValidHexColor(hex))
        {
            return;
        }

        var existing = _savedColors.FirstOrDefault(c => string.Equals(c.Name, colorName, StringComparison.OrdinalIgnoreCase));
        if (existing is { IsBuiltIn: true })
        {
            return;
        }

        if (existing is null)
        {
            _savedColors.Add(new NamedHexColor
            {
                Name = colorName,
                Hex = hex,
                IsBuiltIn = false
            });
        }
        else
        {
            var confirm = System.Windows.MessageBox.Show(
                this,
                LocalizationService.Get(Config.Language, "ColorOverwriteConfirmMessage"),
                LocalizationService.Get(Config.Language, "ColorOverwriteConfirmTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }

            existing.Hex = hex;
        }

        PersistCustomColors();
        RebuildSavedColorCombos(colorName);
        CustomColorNameTextBox.Clear();
        _lastValidCustomHex = hex;
        RebuildQuickAccessTab();
        RefreshColorPresetSelections();
    }

    private void DeleteNamedColor(string rawName)
    {
        var colorName = NormalizeName(rawName);
        if (string.IsNullOrWhiteSpace(colorName))
        {
            return;
        }

        var target = _savedColors.FirstOrDefault(c => string.Equals(c.Name, colorName, StringComparison.OrdinalIgnoreCase));
        if (target is null || target.IsBuiltIn)
        {
            return;
        }

        _savedColors.Remove(target);
        PersistCustomColors();
        EnsureActiveColorsAreAvailable();
        RebuildSavedColorCombos();
        RebuildQuickAccessTab();
        RefreshColorPresetSelections();
    }

    private void PersistCustomColors()
    {
        _customColorService.SaveCustomColors(_savedColors.Where(c => !c.IsBuiltIn));
    }

    private void RefreshBuiltInColors()
    {
        var custom = _savedColors.Where(c => !c.IsBuiltIn).ToList();
        _savedColors.Clear();
        _savedColors.AddRange(GetBuiltInColors());
        _savedColors.AddRange(custom);
    }

    private List<NamedHexColor> GetBuiltInColors()
    {
        return
        [
            new NamedHexColor { Name = LocalizationService.Get(Config.Language, "ColorDeepBlack"), Hex = "#000000", IsBuiltIn = true },
            new NamedHexColor { Name = LocalizationService.Get(Config.Language, "ColorPureWhite"), Hex = "#FFFFFF", IsBuiltIn = true },
            new NamedHexColor { Name = LocalizationService.Get(Config.Language, "ColorDarkGrey"), Hex = "#1F1F1F", IsBuiltIn = true },
            new NamedHexColor { Name = LocalizationService.Get(Config.Language, "ColorSilver"), Hex = "#E5E5E5", IsBuiltIn = true },
            new NamedHexColor { Name = LocalizationService.Get(Config.Language, "ColorWindowsBlue"), Hex = "#0078D4", IsBuiltIn = true },
            new NamedHexColor { Name = LocalizationService.Get(Config.Language, "ColorMicaDark"), Hex = "#202020", IsBuiltIn = true },
            new NamedHexColor { Name = LocalizationService.Get(Config.Language, "ColorMicaLight"), Hex = "#F3F3F3", IsBuiltIn = true },
            new NamedHexColor { Name = LocalizationService.Get(Config.Language, "ColorElectricBlue"), Hex = "#00B7C3", IsBuiltIn = true },
            new NamedHexColor { Name = LocalizationService.Get(Config.Language, "ColorNeonPurple"), Hex = "#8764B8", IsBuiltIn = true },
            new NamedHexColor { Name = LocalizationService.Get(Config.Language, "ColorEmeraldGreen"), Hex = "#107C10", IsBuiltIn = true },
            new NamedHexColor { Name = LocalizationService.Get(Config.Language, "ColorSunsetOrange"), Hex = "#D83B01", IsBuiltIn = true }
        ];
    }

    private static string NormalizeName(string? rawName)
    {
        return (rawName ?? string.Empty).Trim();
    }

    private string ResolvePresetNameForResetOperations()
    {
        EnsurePresetListStillAvailable();

        if (string.Equals(_selectedPresetName, CustomPresetName, StringComparison.OrdinalIgnoreCase))
        {
            return IsPresetSelectable(_lastNonCustomPresetName) ? _lastNonCustomPresetName : DefaultPresetName;
        }

        if (string.IsNullOrWhiteSpace(_selectedPresetName) || !IsPresetSelectable(_selectedPresetName))
        {
            return DefaultPresetName;
        }

        return _selectedPresetName;
    }

    private DockPresetData ResolvePresetDataForResetOperations()
    {
        return GetPresetDataByName(ResolvePresetNameForResetOperations()) ?? DockPresetData.CreateDefault();
    }

    private DockPresetData? GetPresetDataByName(string presetName)
    {
        if (string.Equals(presetName, DefaultPresetName, StringComparison.OrdinalIgnoreCase))
        {
            return SanitizePresetDataColors(DockPresetData.CreateDefault());
        }

        if (string.Equals(presetName, CustomPresetName, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var presetData = _presets.FirstOrDefault(p => string.Equals(p.Name, presetName, StringComparison.OrdinalIgnoreCase))?.Data;
        return presetData is null ? null : SanitizePresetDataColors(presetData);
    }

    private bool IsPresetSelectable(string presetName)
    {
        if (string.Equals(presetName, DefaultPresetName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(presetName, CustomPresetName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (_presets.Any(p => string.Equals(p.Name, presetName, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        // Without a loaded list the remembered preset must not be treated as invalid: Reset
        // would apply Default and save those values.
        return _presetService.LastLoadFailed &&
               (string.Equals(presetName, _selectedPresetName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(presetName, _lastNonCustomPresetName, StringComparison.OrdinalIgnoreCase));
    }

    private void EnsurePresetListStillAvailable()
    {
        if (_presetService.LastLoadFailed || System.IO.File.Exists(_presetService.PresetsPath) || _presets.Count == 0)
        {
            return;
        }

        _presets.Clear();
        if (_presetStateReady && !_syncingPreset)
        {
            SyncCurrentPresetSelectionFromCurrentValues();
        }
    }

    private DockPresetData SanitizePresetDataColors(DockPresetData source)
    {
        return new DockPresetData
        {
            Edge = source.Edge,
            AutoAdjustDockWidth = source.AutoAdjustDockWidth,
            DockWidth = source.DockWidth,
            DockHeight = source.DockHeight,
            Offset = source.Offset,
            AlignmentOffset = source.AlignmentOffset,
            DockOpacity = source.DockOpacity,
            RoundedCorners = source.RoundedCorners,
            CornerRadius = source.CornerRadius,
            RoundedCornerTopLeft = source.RoundedCornerTopLeft,
            RoundedCornerTopRight = source.RoundedCornerTopRight,
            RoundedCornerBottomLeft = source.RoundedCornerBottomLeft,
            RoundedCornerBottomRight = source.RoundedCornerBottomRight,
            SolidColorHex = ResolveColorOrFallback(source.SolidColorHex),
            ShowOutline = source.ShowOutline,
            OutlineEdgeTop = source.OutlineEdgeTop,
            OutlineEdgeBottom = source.OutlineEdgeBottom,
            OutlineEdgeLeft = source.OutlineEdgeLeft,
            OutlineEdgeRight = source.OutlineEdgeRight,
            OutlineColorHex = ResolveColorOrFallback(source.OutlineColorHex),
            OutlineOpacity = source.OutlineOpacity,
            OutlineThickness = source.OutlineThickness,
            IconSize = source.IconSize,
            IconSpacing = source.IconSpacing,
            HighQualityIcons = source.HighQualityIcons,
            ButtonAlignment = source.ButtonAlignment,
            EnableHoverEffects = source.EnableHoverEffects,
            HoverMagnification = source.HoverMagnification,
            HoverEffectRadius = source.HoverEffectRadius,
            AutoHide = source.AutoHide,
            AutoHideSlideAnimation = source.AutoHideSlideAnimation,
            AutoHideSlideDurationMs = source.AutoHideSlideDurationMs,
            AutoHideRevealDelayMs = source.AutoHideRevealDelayMs,
            AutoHideHideDelayMs = source.AutoHideHideDelayMs,
            AutoHideRevealMode = source.AutoHideRevealMode,
            AutoHideProximityDistance = source.AutoHideProximityDistance,
            AlwaysOnTop = source.AlwaysOnTop,
            HideBehindTaskbar = source.HideBehindTaskbar,
            ReserveWorkArea = source.ReserveWorkArea,
            DoNotShowOverFullscreen = source.DoNotShowOverFullscreen,
            OpenAppsOnDockMonitor = source.OpenAppsOnDockMonitor,
            HideWhenMaximized = source.HideWhenMaximized,
            HideWhenWindowIntersects = source.HideWhenWindowIntersects,
            DataSchemaVersion = source.DataSchemaVersion
        };
    }

    private void EnsureActiveColorsAreAvailable()
    {
        if (_customColorService.LastLoadFailed)
        {
            return;
        }

        if (!System.IO.File.Exists(_customColorService.ColorsPath))
        {
            _savedColors.RemoveAll(c => !c.IsBuiltIn);
            RebuildSavedColorCombos();
        }

        Config.SolidColorHex = ResolveColorOrFallback(Config.SolidColorHex);
        Config.OutlineColorHex = ResolveColorOrFallback(Config.OutlineColorHex);
    }

    private string ResolveColorOrFallback(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return "#FFFFFF";
        }

        var normalized = NormalizeHexInput(hex);
        var available = _savedColors.Any(c => string.Equals(c.Hex, normalized, StringComparison.OrdinalIgnoreCase));
        return available ? normalized : "#FFFFFF";
    }

    private void ApplyCategoryFromResolvedPreset(SettingsCategory category)
    {
        var data = ResolvePresetDataForResetOperations();
        switch (category)
        {
            case SettingsCategory.Position:
                Config.Edge = data.Edge;
                Config.AutoAdjustDockWidth = data.AutoAdjustDockWidth;
                if (!data.AutoAdjustDockWidth)
                {
                    Config.DockWidth = data.DockWidth;
                }
                else
                {
                    Config.SyncDockWidthLimits();
                }

                Config.DockHeight = data.DockHeight;
                Config.Offset = data.Offset;
                Config.AlignmentOffset = data.AlignmentOffset;
                break;
            case SettingsCategory.Appearance:
                Config.DockOpacity = data.DockOpacity;
                Config.RoundedCorners = data.RoundedCorners;
                Config.CornerRadius = data.CornerRadius;
                Config.RoundedCornerTopLeft = data.RoundedCornerTopLeft;
                Config.RoundedCornerTopRight = data.RoundedCornerTopRight;
                Config.RoundedCornerBottomLeft = data.RoundedCornerBottomLeft;
                Config.RoundedCornerBottomRight = data.RoundedCornerBottomRight;
                Config.SolidColorHex = data.SolidColorHex;
                Config.ShowOutline = data.ShowOutline;
                Config.OutlineEdgeTop = data.OutlineEdgeTop;
                Config.OutlineEdgeBottom = data.OutlineEdgeBottom;
                Config.OutlineEdgeLeft = data.OutlineEdgeLeft;
                Config.OutlineEdgeRight = data.OutlineEdgeRight;
                Config.OutlineColorHex = data.OutlineColorHex;
                Config.OutlineOpacity = data.OutlineOpacity;
                Config.OutlineThickness = data.OutlineThickness;
                break;
            case SettingsCategory.Shortcuts:
                Config.IconSize = data.IconSize;
                Config.IconSpacing = data.IconSpacing;
                Config.HighQualityIcons = data.HighQualityIcons;
                Config.ButtonAlignment = data.ButtonAlignment;
                Config.EnableHoverEffects = data.EnableHoverEffects;
                Config.HoverMagnification = data.HoverMagnification;
                Config.HoverEffectRadius = data.HoverEffectRadius;
                break;
            case SettingsCategory.Behaviour:
                Config.AutoHide = data.AutoHide;
                Config.AutoHideSlideAnimation = data.AutoHideSlideAnimation;
                Config.AutoHideSlideDurationMs = data.AutoHideSlideDurationMs;
                Config.AutoHideRevealDelayMs = data.AutoHideRevealDelayMs;
                Config.AutoHideHideDelayMs = data.AutoHideHideDelayMs;
                Config.AutoHideRevealMode = data.AutoHideRevealMode;
                Config.AutoHideProximityDistance = data.AutoHideProximityDistance;
                Config.AlwaysOnTop = data.AlwaysOnTop;
                Config.HideBehindTaskbar = data.HideBehindTaskbar;
                Config.ReserveWorkArea = data.ReserveWorkArea;
                Config.DoNotShowOverFullscreen = data.DoNotShowOverFullscreen;
                Config.OpenAppsOnDockMonitor = data.OpenAppsOnDockMonitor;
                Config.HideWhenMaximized = data.HideWhenMaximized;
                Config.HideWhenWindowIntersects = data.HideWhenWindowIntersects;
                break;
        }
    }

    private bool TryGetPresetSliderDefault(Slider slider, DockPresetData presetData, out double value)
    {
        value = 0;
        if (ReferenceEquals(slider, DockWidthSlider))
        {
            value = presetData.DockWidth;
        }
        else if (ReferenceEquals(slider, DockHeightSlider))
        {
            value = presetData.DockHeight;
        }
        else if (ReferenceEquals(slider, OffsetSlider))
        {
            value = presetData.Offset;
        }
        else if (ReferenceEquals(slider, DockOpacitySlider))
        {
            value = presetData.DockOpacity;
        }
        else if (ReferenceEquals(slider, OutlineOpacitySlider))
        {
            value = presetData.OutlineOpacity;
        }
        else if (ReferenceEquals(slider, OutlineThicknessSlider))
        {
            value = presetData.OutlineThickness;
        }
        else if (ReferenceEquals(slider, CornerRadiusSlider))
        {
            value = presetData.CornerRadius;
        }
        else if (ReferenceEquals(slider, IconSizeSlider))
        {
            value = presetData.IconSize;
        }
        else if (ReferenceEquals(slider, IconSpacingSlider))
        {
            value = presetData.IconSpacing;
        }
        else if (ReferenceEquals(slider, HoverMagnificationSlider))
        {
            value = presetData.HoverMagnification;
        }
        else if (ReferenceEquals(slider, HoverRadiusSlider))
        {
            value = DockMetrics.LogicalToCm(presetData.HoverEffectRadius);
        }
        else if (ReferenceEquals(slider, AutoHideProximityDistanceSlider))
        {
            value = DockMetrics.LogicalToCm(presetData.AutoHideProximityDistance);
        }
        else if (ReferenceEquals(slider, AutoHideSlideSpeedSlider))
        {
            value = presetData.AutoHideSlideDurationMs;
        }
        else
        {
            return false;
        }

        return true;
    }

    private void Slider_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Slider slider)
        {
            return;
        }

        var presetData = ResolvePresetDataForResetOperations();
        if (TryGetPresetSliderDefault(slider, presetData, out var presetValue))
        {
            slider.Value = presetValue;
            return;
        }

        if (slider.Tag is string tagValue && double.TryParse(tagValue, out var defaultValue))
        {
            slider.Value = defaultValue;
        }
    }

    private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (sender is not Slider slider)
        {
            return;
        }

        foreach (var (textBox, binding) in _numericBindings)
        {
            if (ReferenceEquals(binding.Slider, slider))
            {
                SyncTextBoxWithSlider(textBox, binding);
                break;
            }
        }
    }

    private void NumericTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (sender is not WpfTextBox textBox)
        {
            return;
        }

        if (_numericBindings.TryGetValue(textBox, out var binding))
        {
            if (binding.FractionDigits > 0)
            {
                e.Handled = e.Text.Length > 0 && !Regex.IsMatch(e.Text, @"^[0-9.,]$");
                return;
            }

            var min = (int)Math.Round(binding.Min);
            var max = (int)Math.Round(binding.Max);
            e.Handled = !IsCandidateInputAllowed(textBox, e.Text, min, max);
            return;
        }

        if (!TryGetStandaloneNumericRange(textBox, out var smin, out var smax))
        {
            return;
        }

        e.Handled = !IsCandidateInputAllowed(textBox, e.Text, smin, smax);
    }

    private void NumericTextBox_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (sender is not WpfTextBox textBox)
        {
            return;
        }

        if (!e.DataObject.GetDataPresent(typeof(string)))
        {
            e.CancelCommand();
            return;
        }

        var text = e.DataObject.GetData(typeof(string)) as string ?? string.Empty;
        if (_numericBindings.TryGetValue(textBox, out var binding))
        {
            if (binding.FractionDigits > 0)
            {
                if (!Regex.IsMatch(text.Replace(',', '.'), @"^-?[0-9]*\.?[0-9]*$"))
                {
                    e.CancelCommand();
                }

                return;
            }

            var min = (int)Math.Round(binding.Min);
            var max = (int)Math.Round(binding.Max);
            if (!IsCandidateInputAllowed(textBox, text, min, max))
            {
                e.CancelCommand();
            }

            return;
        }

        if (!TryGetStandaloneNumericRange(textBox, out var smin, out var smax))
        {
            return;
        }

        if (!IsCandidateInputAllowed(textBox, text, smin, smax))
        {
            e.CancelCommand();
        }
    }

    private void NumericTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is WpfTextBox textBox)
        {
            ApplyTextBoxValue(textBox);
        }
    }

    private void NumericTextBox_KeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key is not Key.Enter || sender is not WpfTextBox textBox)
        {
            return;
        }

        ApplyTextBoxValue(textBox);
        e.Handled = true;
    }

    private void RefreshTexts()
    {
        var l = Config.Language;

        var baseTitle = LocalizationService.Get(l, "SettingsTitle");
        Title = string.IsNullOrWhiteSpace(_instanceLabel)
            ? baseTitle
            : $"{baseTitle} - {_instanceLabel}";
        HeaderText.Text = "ToolDock";
        HeaderText.Margin = new Thickness(12, 0, 0, 0);
        PresetLabel.Text = LocalizationService.Get(l, "CurrentPreset");
        SavePresetLabel.Text = LocalizationService.Get(l, "SaveCurrentAsNewPreset");
        SavePresetButton.Content = LocalizationService.Get(l, "SavePreset");
        DeletePresetButton.Content = LocalizationService.Get(l, "DeletePreset");
        LanguageLabel.Text = LocalizationService.Get(l, "Language");
        SpanishOption.Content = LocalizationService.Get(l, "Spanish");
        EnglishOption.Content = LocalizationService.Get(l, "English");
        ThemeLabel.Text = LocalizationService.Get(l, "Theme");
        DarkModeCheck.Content = LocalizationService.Get(l, "DarkMode");
        RefreshDisableDockButton();
        InfoTabButton.Content = LocalizationService.Get(l, "CategoryInfo");
        GeneralTabButton.Content = LocalizationService.Get(l, "CategoryGeneral");
        QuickAccessTabButton.Content = LocalizationService.Get(l, "CategoryQuickAccess");
        PositionTabButton.Content = LocalizationService.Get(l, "CategoryPosition");
        AppearanceTabButton.Content = LocalizationService.Get(l, "CategoryAppearance");
        ShortcutsTabButton.Content = LocalizationService.Get(l, "CategoryShortcuts");
        BehaviourTabButton.Content = LocalizationService.Get(l, "CategoryBehaviour");
        DetailsTabButton.Content = LocalizationService.Get(l, "CategoryDetails");
        TroubleshootingTabButton.Content = LocalizationService.Get(l, "CategoryTroubleshooting");
        InfoPanelTitle.Text = LocalizationService.Get(l, "CategoryInfo");
        InfoHeadingGeneral.Text = LocalizationService.Get(l, "InfoHeadingGeneralUsage");
        InfoBodyGeneral.Text = LocalizationService.Get(l, "InfoBodyGeneralUsage");
        InfoHeadingStartup.Text = LocalizationService.Get(l, "InfoHeadingStartup");
        InfoBodyStartup.Text = LocalizationService.Get(l, "InfoBodyStartup");
        InfoHeadingInstances.Text = LocalizationService.Get(l, "InfoHeadingInstances");
        InfoBodyInstances.Text = LocalizationService.Get(l, "InfoBodyInstances");
        InfoHeadingJson.Text = LocalizationService.Get(l, "InfoHeadingJsonFiles");
        InfoBodyJson.Text = LocalizationService.Get(l, "InfoBodyJsonFiles");
        InfoHeadingHidden.Text = LocalizationService.Get(l, "InfoHeadingHiddenDetails");
        InfoBodyHidden.Text = LocalizationService.Get(l, "InfoBodyHiddenDetails");
        InfoHeadingWip.Text = LocalizationService.Get(l, "InfoHeadingWip");
        InfoBodyWip.Text = LocalizationService.Get(l, "InfoBodyWip");
        GeneralPanelTitle.Text = LocalizationService.Get(l, "CategoryGeneral");
        QuickAccessPanelTitle.Text = LocalizationService.Get(l, "CategoryQuickAccess");
        PositionPanelTitle.Text = LocalizationService.Get(l, "CategoryPosition");
        AppearancePanelTitle.Text = LocalizationService.Get(l, "CategoryAppearance");
        ShortcutsPanelTitle.Text = LocalizationService.Get(l, "CategoryShortcuts");
        BehaviourPanelTitle.Text = LocalizationService.Get(l, "CategoryBehaviour");
        DetailsPanelTitle.Text = LocalizationService.Get(l, "CategoryDetails");
        TroubleshootingPanelTitle.Text = LocalizationService.Get(l, "CategoryTroubleshooting");
        OpenExeFolderButton.Content = LocalizationService.Get(l, "TroubleshootingOpenExeFolder");
        RefreshWindowDescriptionText.Text = LocalizationService.Get(l, "SettingsRefreshWindowDescription");
        ReloadAppDescriptionText.Text = LocalizationService.Get(l, "SettingsReloadAppDescription");
        ResetThisInstanceButton.Content = LocalizationService.Get(l, "TroubleshootingResetThisInstance");
        ResetThisInstanceDescriptionText.Text = LocalizationService.Get(l, "TroubleshootingResetThisInstanceDescription");
        ResetAllInstancesButton.Content = LocalizationService.Get(l, "TroubleshootingResetAllInstances");
        ResetAllInstancesDescriptionText.Text = LocalizationService.Get(l, "TroubleshootingResetAllInstancesDescription");
        HardResetButton.Content = LocalizationService.Get(l, "TroubleshootingHardReset");
        HardResetDescriptionText.Text = LocalizationService.Get(l, "TroubleshootingHardResetDescription");
        DeleteAllCustomColorsButton.Content = LocalizationService.Get(l, "TroubleshootingDeleteAllCustomColors");
        DeleteAllCustomColorsDescriptionText.Text = LocalizationService.Get(l, "TroubleshootingDeleteAllCustomColorsDescription");
        DeleteAllCustomPresetsButton.Content = LocalizationService.Get(l, "TroubleshootingDeleteAllCustomPresets");
        DeleteAllCustomPresetsDescriptionText.Text = LocalizationService.Get(l, "TroubleshootingDeleteAllCustomPresetsDescription");
        DetailsIntroText.Text = LocalizationService.Get(l, "DetailsIntro");
        DetailsDockWidthRow.Text = LocalizationService.Get(l, "DetailsDockWidth");
        DetailsDockHeightRow.Text = LocalizationService.Get(l, "DetailsDockHeight");
        DetailsIconSizeRow.Text = LocalizationService.Get(l, "DetailsIconSize");
        DetailsIconSpacingRow.Text = LocalizationService.Get(l, "DetailsIconSpacing");
        DetailsHoverRow.Text = LocalizationService.Get(l, "DetailsHoverRadius");
        DetailsAutoHideProximityRow.Text = LocalizationService.Get(l, "DetailsAutoHideProximity");
        DetailsWorkAreaRow.Text = LocalizationService.Get(l, "DetailsWorkArea");
        DetailsScreenResolutionRow.Text = LocalizationService.Get(l, "DetailsScreenResolution");
        DetailsDpiRow.Text = LocalizationService.Get(l, "DetailsDpi");
        ResetGeneralButton.Content = LocalizationService.Get(l, "ResetGeneral");
        ResetQuickAccessButton.Content = LocalizationService.Get(l, "ResetQuickAccess");
        ResetPositionButton.Content = LocalizationService.Get(l, "ResetPosition");
        ResetAppearanceButton.Content = LocalizationService.Get(l, "ResetAppearance");
        SaveShortcutsButton.Content = LocalizationService.Get(l, "SaveShortcuts");
        LoadShortcutsButton.Content = LocalizationService.Get(l, "LoadShortcuts");
        RemoveAllShortcutsButton.Content = LocalizationService.Get(l, "RemoveAllShortcuts");
        ResetShortcutsButton.Content = LocalizationService.Get(l, "ResetShortcuts");
        ResetBehaviourButton.Content = LocalizationService.Get(l, "ResetBehaviour");
        ResetPresetButton.Content = LocalizationService.Get(l, "ResetPreset");
        UnpinAllQuickAccessButton.Content = LocalizationService.Get(l, "UnpinAll");
        QuickAccessEmptyText.Text = LocalizationService.Get(l, "QuickAccessEmpty");
        EdgeLabel.Text = LocalizationService.Get(l, "DockAlignment");
        TopOption.Content = LocalizationService.Get(l, "Top");
        BottomOption.Content = LocalizationService.Get(l, "Bottom");
        LeftOption.Content = LocalizationService.Get(l, "Left");
        RightOption.Content = LocalizationService.Get(l, "Right");
        BackgroundSectionLabel.Text = LocalizationService.Get(l, "BackgroundSection");
        SolidPresetLabel.Text = LocalizationService.Get(l, "SolidPreset");
        CustomHexLabel.Text = LocalizationService.Get(l, "CustomHex");
        CustomColorNameLabel.Text = LocalizationService.Get(l, "CustomColorName");
        CustomColorsSectionLabel.Text = LocalizationService.Get(l, "CustomColorsSection");
        CustomSavedColorsLabel.Text = LocalizationService.Get(l, "SavedColors");
        SaveCustomColorButton.Content = LocalizationService.Get(l, "SaveColor");
        DeleteCustomColorButton.Content = LocalizationService.Get(l, "DeleteColor");
        ColorDeepBlackOption.Content = LocalizationService.Get(l, "ColorDeepBlack");
        ColorPureWhiteOption.Content = LocalizationService.Get(l, "ColorPureWhite");
        ColorDarkGreyOption.Content = LocalizationService.Get(l, "ColorDarkGrey");
        ColorSilverOption.Content = LocalizationService.Get(l, "ColorSilver");
        ColorWindowsBlueOption.Content = LocalizationService.Get(l, "ColorWindowsBlue");
        ColorMicaDarkOption.Content = LocalizationService.Get(l, "ColorMicaDark");
        ColorMicaLightOption.Content = LocalizationService.Get(l, "ColorMicaLight");
        ColorElectricBlueOption.Content = LocalizationService.Get(l, "ColorElectricBlue");
        ColorNeonPurpleOption.Content = LocalizationService.Get(l, "ColorNeonPurple");
        ColorEmeraldGreenOption.Content = LocalizationService.Get(l, "ColorEmeraldGreen");
        ColorSunsetOrangeOption.Content = LocalizationService.Get(l, "ColorSunsetOrange");
        DockOpacityLabel.Text = LocalizationService.Get(l, "DockOpacity");
        OutlineSectionLabel.Text = LocalizationService.Get(l, "OutlineSection");
        ShowOutlineCheck.Content = LocalizationService.Get(l, "OutlineEnabled");
        OutlineEdgesLabel.Text = LocalizationService.Get(l, "OutlineEdgesLabel");
        OutlineEdgesHintText.Text = LocalizationService.Get(l, "OutlineEdgesHint");
        OutlineEdgeTopCheck.Content = LocalizationService.Get(l, "OutlineEdgeTop");
        OutlineEdgeBottomCheck.Content = LocalizationService.Get(l, "OutlineEdgeBottom");
        OutlineEdgeLeftCheck.Content = LocalizationService.Get(l, "OutlineEdgeLeft");
        OutlineEdgeRightCheck.Content = LocalizationService.Get(l, "OutlineEdgeRight");
        OutlineShowAllEdgesButton.Content = LocalizationService.Get(l, "OutlineShowAllEdges");
        OutlineColorLabel.Text = LocalizationService.Get(l, "OutlineColor");
        OutlineOpacityLabel.Text = LocalizationService.Get(l, "OutlineOpacity");
        OutlineThicknessLabel.Text = LocalizationService.Get(l, "OutlineThickness");
        OutlineThicknessHintText.Text = LocalizationService.Get(l, "OutlineThicknessHint");
        OutlineDeepBlackOption.Content = LocalizationService.Get(l, "ColorDeepBlack");
        OutlinePureWhiteOption.Content = LocalizationService.Get(l, "ColorPureWhite");
        OutlineDarkGreyOption.Content = LocalizationService.Get(l, "ColorDarkGrey");
        OutlineSilverOption.Content = LocalizationService.Get(l, "ColorSilver");
        OutlineWindowsBlueOption.Content = LocalizationService.Get(l, "ColorWindowsBlue");
        OutlineMicaDarkOption.Content = LocalizationService.Get(l, "ColorMicaDark");
        OutlineMicaLightOption.Content = LocalizationService.Get(l, "ColorMicaLight");
        OutlineElectricBlueOption.Content = LocalizationService.Get(l, "ColorElectricBlue");
        OutlineNeonPurpleOption.Content = LocalizationService.Get(l, "ColorNeonPurple");
        OutlineEmeraldGreenOption.Content = LocalizationService.Get(l, "ColorEmeraldGreen");
        OutlineSunsetOrangeOption.Content = LocalizationService.Get(l, "ColorSunsetOrange");
        IconSizeLabel.Text = LocalizationService.Get(l, "IconSizePx");
        IconSpacingLabel.Text = LocalizationService.Get(l, "IconSpacingPx");
        HighQualityIconsCheck.Content = LocalizationService.Get(l, "HighQualityIcons");
        HighQualityIconsHint.Text = LocalizationService.Get(l, "HighQualityIconsHint");
        ButtonsSectionLabel.Text = LocalizationService.Get(l, "ButtonsSection");
        ButtonAlignmentLabel.Text = LocalizationService.Get(l, "ButtonAlignment");
        HoverEffectsSectionLabel.Text = LocalizationService.Get(l, "HoverEffectsSection");
        HoverEffectsCheck.Content = LocalizationService.Get(l, "HoverEffectsEnabled");
        HoverMagnificationLabel.Text = LocalizationService.Get(l, "HoverMagnification");
        HoverRadiusLabel.Text = LocalizationService.Get(l, "HoverRadiusCm");
        AutoAdjustDockWidthCheck.Content = LocalizationService.Get(l, "AutoAdjustDockWidth");
        DockWidthLabel.Text = LocalizationService.Get(l, "DockWidthPx");
        DockHeightLabel.Text = LocalizationService.Get(l, "DockHeightPx");
        OffsetLabel.Text = LocalizationService.Get(l, "OffsetPx");
        RoundedCheck.Content = LocalizationService.Get(l, "RoundedCorners");
        CornerRadiusLabel.Text = LocalizationService.Get(l, "CornerRadius");
        AutohideCheck.Content = LocalizationService.Get(l, "AutoHide");
        HideWhenMaximizedCheck.Content = LocalizationService.Get(l, "HideWhenMaximized");
        HideWhenWindowIntersectsCheck.Content = LocalizationService.Get(l, "HideWhenWindowIntersects");
        AutoHideRevealModeLabel.Text = LocalizationService.Get(l, "AutoHideRevealMode");
        AutoHideRevealModeEdgeTouchOption.Content = LocalizationService.Get(l, "AutoHideRevealModeEdgeTouch");
        AutoHideRevealModeProximityOption.Content = LocalizationService.Get(l, "AutoHideRevealModeProximity");
        AutoHideProximityDistanceLabel.Text = LocalizationService.Get(l, "AutoHideProximityDistance");
        AutoHideSlideCheck.Content = LocalizationService.Get(l, "AutoHideSlideEnabled");
        AutoHideSlideSpeedLabel.Text = LocalizationService.Get(l, "AutoHideSlideSpeed");
        AutoHideRevealDelayLabel.Text = LocalizationService.Get(l, "AutoHideRevealDelay");
        AutoHideHideDelayLabel.Text = LocalizationService.Get(l, "AutoHideHideDelay");
        AlwaysOnTopCheck.Content = LocalizationService.Get(l, "AlwaysOnTop");
        OpenAppsOnDockMonitorCheck.Content = LocalizationService.Get(l, "OpenAppsOnDockMonitor");
        HideBehindTaskbarCheck.Content = LocalizationService.Get(l, "HideBehindTaskbar");
        ReserveWorkAreaCheck.Content = LocalizationService.Get(l, "ReserveWorkArea");
        DoNotShowOverFullscreenCheck.Content = LocalizationService.Get(l, "DoNotShowOverFullscreen");
        HotkeysSectionLabel.Text = LocalizationService.Get(l, "HotkeysSection");
        HotkeyShowHideLabel.Text = LocalizationService.Get(l, "HotkeyShowHide");
        HotkeyShowHideClearButton.Content = LocalizationService.Get(l, "HotkeyClear");
        HotkeyShowDockLabel.Text = LocalizationService.Get(l, "HotkeyShowDock");
        HotkeyShowDockClearButton.Content = LocalizationService.Get(l, "HotkeyClear");
        HotkeyDisableDockLabel.Text = LocalizationService.Get(l, "HotkeyDisableDock");
        HotkeyDisableDockClearButton.Content = LocalizationService.Get(l, "HotkeyClear");
        AlignmentOffsetLabel.Text = LocalizationService.Get(l, "AlignmentOffset");
        MonitorSelectLabel.Text = LocalizationService.Get(l, "MonitorSelect");
        RoundedCornersPerSideLabel.Text = LocalizationService.Get(l, "RoundedCornersPerSide");
        RoundedCornerTopLeftCheck.Content = LocalizationService.Get(l, "RoundedCornerTopLeft");
        RoundedCornerTopRightCheck.Content = LocalizationService.Get(l, "RoundedCornerTopRight");
        RoundedCornerBottomLeftCheck.Content = LocalizationService.Get(l, "RoundedCornerBottomLeft");
        RoundedCornerBottomRightCheck.Content = LocalizationService.Get(l, "RoundedCornerBottomRight");
        ShortcutsDragDropHint.Text = LocalizationService.Get(l, "ShortcutsDragDropHint");
        AddShortcutsSectionLabel.Text = LocalizationService.Get(l, "AddShortcutsSection");
        AddShortcutButton.Content = LocalizationService.Get(l, "AddShortcut");
        AddUwpAppButton.Content = LocalizationService.Get(l, "AddUwpApp");
        GenerateDetailedLogButton.Content = LocalizationService.Get(l, "GenerateDetailedLog");
        GenerateDetailedLogDescriptionText.Text = LocalizationService.Get(l, "GenerateDetailedLogDescription");
        RefreshHotkeyButtonLabels();
        RefreshMonitorOptions();
        UpdatePageHeader();
        var resetHint = LocalizationService.Get(l, "ResetHint");
        ResetPositionButton.ToolTip = resetHint;
        ResetAppearanceButton.ToolTip = resetHint;
        ResetShortcutsButton.ToolTip = resetHint;
        ResetBehaviourButton.ToolTip = resetHint;
        ResetDefaultsButton.Content = LocalizationService.Get(l, "ResetDefaults");
        ReloadAppButton.Content = LocalizationService.Get(l, "SettingsReloadApp");
        RefreshWindowButton.Content = LocalizationService.Get(l, "SettingsRefreshWindow");
        CloseButton.Content = LocalizationService.Get(l, "Close");
        RefreshButtonAlignmentOptions();
        RefreshBuiltInColors();
        RebuildPresetCombo(_selectedPresetName);
        UpdatePresetUiState();
        RebuildSavedColorCombos();
        RefreshPinButtons();
        RebuildQuickAccessTab();
        RefreshDetailsPanel();
    }

    private void ApplyTheme()
    {
        var isDark = Config.Theme == AppTheme.Dark;
        var background = isDark
            ? System.Windows.Media.Color.FromRgb(27, 28, 34)
            : System.Windows.Media.Color.FromRgb(243, 243, 243);
        var sidebar = isDark
            ? System.Windows.Media.Color.FromRgb(23, 24, 29)
            : System.Windows.Media.Color.FromRgb(249, 249, 249);
        var card = isDark
            ? System.Windows.Media.Color.FromRgb(42, 44, 53)
            : System.Windows.Media.Color.FromRgb(255, 255, 255);
        var cardBorder = isDark
            ? System.Windows.Media.Color.FromRgb(58, 61, 72)
            : System.Windows.Media.Color.FromRgb(229, 229, 229);
        var controlBack = isDark
            ? System.Windows.Media.Color.FromRgb(50, 52, 62)
            : System.Windows.Media.Color.FromRgb(255, 255, 255);
        var controlBorder = isDark
            ? System.Windows.Media.Color.FromRgb(70, 74, 86)
            : System.Windows.Media.Color.FromRgb(209, 209, 209);
        var controlHover = isDark
            ? System.Windows.Media.Color.FromRgb(60, 63, 74)
            : System.Windows.Media.Color.FromRgb(245, 245, 245);
        var controlPressed = isDark
            ? System.Windows.Media.Color.FromRgb(46, 48, 58)
            : System.Windows.Media.Color.FromRgb(232, 232, 232);
        var navSelected = isDark
            ? System.Windows.Media.Color.FromRgb(60, 63, 74)
            : System.Windows.Media.Color.FromRgb(224, 224, 224);
        var navHover = isDark
            ? System.Windows.Media.Color.FromRgb(42, 44, 54)
            : System.Windows.Media.Color.FromRgb(235, 235, 235);
        var accent = isDark
            ? System.Windows.Media.Color.FromRgb(96, 205, 255)
            : System.Windows.Media.Color.FromRgb(0, 103, 192);
        var foreground = isDark
            ? System.Windows.Media.Color.FromRgb(243, 243, 243)
            : System.Windows.Media.Color.FromRgb(26, 26, 26);
        var muted = isDark
            ? System.Windows.Media.Color.FromRgb(160, 164, 176)
            : System.Windows.Media.Color.FromRgb(96, 96, 96);
        var checkGlyph = isDark
            ? System.Windows.Media.Color.FromRgb(20, 20, 20)
            : System.Windows.Media.Color.FromRgb(255, 255, 255);
        var scrollThumb = isDark
            ? System.Windows.Media.Color.FromRgb(214, 214, 222)
            : System.Windows.Media.Color.FromRgb(150, 150, 150);

        SetThemeResource("AppBackgroundBrush", background);
        SetThemeResource("SidebarBrush", sidebar);
        SetThemeResource("PaneBackgroundBrush", sidebar);
        SetThemeResource("PaneBorderBrush", cardBorder);
        SetThemeResource("CardBackgroundBrush", card);
        SetThemeResource("CardBorderBrush", cardBorder);
        SetThemeResource("ControlBackgroundBrush", controlBack);
        SetThemeResource("ControlBorderBrush", controlBorder);
        SetThemeResource("ControlHoverBrush", controlHover);
        SetThemeResource("ControlPressedBrush", controlPressed);
        SetThemeResource("NavSelectedBrush", navSelected);
        SetThemeResource("NavHoverBrush", navHover);
        SetThemeResource("AccentBrush", accent);
        SetThemeResource("AccentForegroundBrush", foreground);
        SetThemeResource("MutedForegroundBrush", muted);
        SetThemeResource("ThumbBrush", System.Windows.Media.Color.FromRgb(255, 255, 255));
        SetThemeResource("ScrollThumbBrush", scrollThumb);
        SetThemeResource("CheckGlyphBrush", checkGlyph);

        Background = new SolidColorBrush(background);
        Foreground = new SolidColorBrush(foreground);
        QuickAccessEmptyText.Foreground = new SolidColorBrush(isDark ? System.Windows.Media.Colors.White : System.Windows.Media.Colors.Black);

        DarkModeCheck.Foreground = Foreground;
        ShowOutlineCheck.Foreground = Foreground;
        OutlineEdgeTopCheck.Foreground = Foreground;
        OutlineEdgeBottomCheck.Foreground = Foreground;
        OutlineEdgeLeftCheck.Foreground = Foreground;
        OutlineEdgeRightCheck.Foreground = Foreground;
        RoundedCheck.Foreground = Foreground;
        HoverEffectsCheck.Foreground = Foreground;
        AutohideCheck.Foreground = Foreground;
        HideWhenMaximizedCheck.Foreground = Foreground;
        HideWhenWindowIntersectsCheck.Foreground = Foreground;
        AutoHideSlideCheck.Foreground = Foreground;
        AlwaysOnTopCheck.Foreground = Foreground;
        OpenAppsOnDockMonitorCheck.Foreground = Foreground;
        ApplyCaptionChrome(isDark);
        NavigateTo(_currentCategory);
    }

    private void ApplyCaptionChrome(bool isDark)
    {
        User32.ApplyCaptionTheme(new WindowInteropHelper(this).Handle, isDark);
    }

    private void SetThemeResource(string key, System.Windows.Media.Color color)
    {
        Resources[key] = new SolidColorBrush(color);
    }

    private void DisableDockButton_Click(object sender, RoutedEventArgs e)
    {
        Config.DockDisabled = !Config.DockDisabled;
    }

    private void RefreshDisableDockButton()
    {
        var disabled = Config.DockDisabled;
        DisableDockButton.Content = LocalizationService.Get(Config.Language, disabled ? "EnableDock" : "DisableDock");
        FluentChrome.SetIcon(DisableDockButton, disabled ? "\uE7B3" : "\uE7E8");
        FluentChrome.SetIsActive(DisableDockButton, disabled);
    }

    private void ResetDefaultsButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyPresetByName(DefaultPresetName);
        _selectedPresetName = DefaultPresetName;
        _lastNonCustomPresetName = DefaultPresetName;
        RebuildPresetCombo(DefaultPresetName);
        UpdatePresetUiState();
        SyncAllNumericBoxes();
        RefreshColorPresetSelections();
        RebuildQuickAccessTab();
        RefreshPinButtons();
    }

    private void ReloadAppButton_Click(object sender, RoutedEventArgs e)
    {
        if (System.Windows.Application.Current is App app)
        {
            app.RestartApplication(openSettingsAfterRestart: true);
        }
    }

    private void OutlineShowAllEdgesButton_Click(object sender, RoutedEventArgs e)
    {
        Config.OutlineEdgeTop = true;
        Config.OutlineEdgeBottom = true;
        Config.OutlineEdgeLeft = true;
        Config.OutlineEdgeRight = true;
    }

    private void RefreshWindowButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshSettingsWindow();
    }

    private void RefreshSettingsWindow()
    {
        EffectiveDpiX = VisualTreeHelper.GetDpi(this).PixelsPerInchX;
        DockMetrics.GetDpiPixelsPerInchX = () => VisualTreeHelper.GetDpi(this).PixelsPerInchX;
        ApplyPercentSliderRanges();
        RefreshTexts();
        ApplyTheme();
        SyncAllNumericBoxes();
        RefreshColorPresetSelections();
        InvalidateVisual();
        UpdateLayout();
    }

    private void OpenExeFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (System.Windows.Application.Current is App app)
        {
            app.OpenExecutableFolder();
        }
    }

    private void ResetThisInstanceButton_Click(object sender, RoutedEventArgs e)
    {
        if (System.Windows.Application.Current is App app)
        {
            app.BeginResetThisInstance();
        }
    }

    private void ResetAllInstancesButton_Click(object sender, RoutedEventArgs e)
    {
        if (System.Windows.Application.Current is App app)
        {
            app.BeginResetAllInstances();
        }
    }

    private void HardResetButton_Click(object sender, RoutedEventArgs e)
    {
        if (System.Windows.Application.Current is App app)
        {
            app.BeginHardReset();
        }
    }

    private void DeleteAllCustomColorsButton_Click(object sender, RoutedEventArgs e)
    {
        var l = Config.Language;
        var confirm = System.Windows.MessageBox.Show(
            LocalizationService.Get(l, "TroubleshootingDeleteAllCustomColorsConfirmMessage"),
            LocalizationService.Get(l, "TroubleshootingDeleteAllCustomColorsConfirmTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        DurableFile.DeleteFamily(_customColorService.ColorsPath);
        LoadSavedColorsState();
        RebuildQuickAccessTab();
        RefreshColorPresetSelections();
    }

    private void DeleteAllCustomPresetsButton_Click(object sender, RoutedEventArgs e)
    {
        var l = Config.Language;
        var confirm = System.Windows.MessageBox.Show(
            LocalizationService.Get(l, "TroubleshootingDeleteAllCustomPresetsConfirmMessage"),
            LocalizationService.Get(l, "TroubleshootingDeleteAllCustomPresetsConfirmTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        DurableFile.DeleteFamily(_presetService.PresetsPath);
        LoadPresetState();
        ApplyPresetByName(DefaultPresetName);
    }

    private void RegisterNumericBindings()
    {
        RegisterNumericBinding(DockOpacityValueBox, DockOpacitySlider, 0, 100, 0);
        RegisterNumericBinding(CornerRadiusValueBox, CornerRadiusSlider, DockConfig.GetCornerRadiusMin(), DockConfig.GetCornerRadiusMax(), 0);
        RegisterNumericBinding(OutlineOpacityValueBox, OutlineOpacitySlider, 0, 100, 0);
        RegisterNumericBinding(OutlineThicknessValueBox, OutlineThicknessSlider, DockConfig.GetOutlineThicknessMin(), DockConfig.GetOutlineThicknessMax(), 2);
        RegisterNumericBinding(IconSizeValueBox, IconSizeSlider, IconSizeMin, IconSizeMax, 0);
        RegisterNumericBinding(IconSpacingValueBox, IconSpacingSlider, IconSpacingMin, IconSpacingMax, 0);
        RegisterNumericBinding(HoverMagnificationValueBox, HoverMagnificationSlider, 0, 150, 0);
        RegisterNumericBinding(HoverRadiusValueBox, HoverRadiusSlider, HoverRadiusMinCm, HoverRadiusMaxCm, 2);
        RegisterNumericBinding(DockWidthValueBox, DockWidthSlider, DockWidthMin, DockWidthMax, 0);
        RegisterNumericBinding(DockHeightValueBox, DockHeightSlider, DockHeightMin, DockHeightMax, 0);
        RegisterNumericBinding(AutoHideProximityDistanceValueBox, AutoHideProximityDistanceSlider, AutoHideProximityMinCm, AutoHideProximityMaxCm, 2);
        RegisterNumericBinding(AutoHideSlideSpeedValueBox, AutoHideSlideSpeedSlider, 0, 1200, 0);
        RegisterNumericBinding(OffsetValueBox, OffsetSlider, -1500, 1500, 0);
        RegisterNumericBinding(AlignmentOffsetValueBox, AlignmentOffsetSlider, 0, 600, 0);
    }

    private void RegisterNumericBinding(WpfTextBox textBox, Slider slider, double min, double max, int fractionDigits)
    {
        _numericBindings[textBox] = new NumericBinding(slider, min, max, fractionDigits);
    }

    private void SyncAllNumericBoxes()
    {
        foreach (var (textBox, binding) in _numericBindings)
        {
            SyncTextBoxWithSlider(textBox, binding);
        }

        if (!AutoHideRevealDelayValueBox.IsKeyboardFocusWithin)
        {
            AutoHideRevealDelayValueBox.Text = Config.AutoHideRevealDelayMs.ToString();
        }

        if (!AutoHideHideDelayValueBox.IsKeyboardFocusWithin)
        {
            AutoHideHideDelayValueBox.Text = Config.AutoHideHideDelayMs.ToString();
        }
    }

    private void SyncTextBoxWithSlider(WpfTextBox textBox, NumericBinding binding, bool force = false)
    {
        // Do not overwrite the text while the user is editing it; background syncs
        // (presets, reloads, changes from other controls) wait until it loses focus.
        if (!force && textBox.IsKeyboardFocusWithin)
        {
            return;
        }

        var v = binding.Slider.Value;
        textBox.Text = binding.FractionDigits switch
        {
            0 => ((int)Math.Round(v)).ToString(CultureInfo.InvariantCulture),
            1 => v.ToString("0.#", CultureInfo.InvariantCulture),
            _ => v.ToString("0.##", CultureInfo.InvariantCulture)
        };
    }

    private void ApplyTextBoxValue(WpfTextBox textBox)
    {
        if (_numericBindings.TryGetValue(textBox, out var binding))
        {
            var text = textBox.Text.Replace(',', '.');
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                var min = ReferenceEquals(textBox, DockWidthValueBox)
                    ? DockWidthMin
                    : ReferenceEquals(textBox, HoverRadiusValueBox)
                        ? HoverRadiusMinCm
                        : ReferenceEquals(textBox, AutoHideProximityDistanceValueBox)
                            ? AutoHideProximityMinCm
                            : binding.Min;
                var max = ReferenceEquals(textBox, DockWidthValueBox)
                    ? DockWidthMax
                    : ReferenceEquals(textBox, HoverRadiusValueBox)
                        ? HoverRadiusMaxCm
                        : ReferenceEquals(textBox, AutoHideProximityDistanceValueBox)
                            ? AutoHideProximityMaxCm
                            : binding.Max;
                var clamped = Math.Clamp(parsed, min, max);
                binding.Slider.Value = clamped;
                SyncTextBoxWithSlider(textBox, binding, force: true);
                return;
            }

            SyncTextBoxWithSlider(textBox, binding, force: true);
            return;
        }

        if (!TryGetStandaloneNumericRange(textBox, out var standaloneMin, out var standaloneMax))
        {
            return;
        }

        if (!int.TryParse(textBox.Text, out var standaloneParsed))
        {
            textBox.Text = ReferenceEquals(textBox, AutoHideHideDelayValueBox)
                ? Config.AutoHideHideDelayMs.ToString()
                : Config.AutoHideRevealDelayMs.ToString();
            return;
        }

        var standaloneClamped = Math.Clamp(standaloneParsed, standaloneMin, standaloneMax);
        if (ReferenceEquals(textBox, AutoHideRevealDelayValueBox))
        {
            Config.AutoHideRevealDelayMs = standaloneClamped;
        }
        else if (ReferenceEquals(textBox, AutoHideHideDelayValueBox))
        {
            Config.AutoHideHideDelayMs = standaloneClamped;
        }

        textBox.Text = standaloneClamped.ToString();
    }

    private bool TryGetStandaloneNumericRange(WpfTextBox textBox, out int min, out int max)
    {
        if (ReferenceEquals(textBox, AutoHideRevealDelayValueBox))
        {
            min = 0;
            max = 4000;
            return true;
        }

        if (ReferenceEquals(textBox, AutoHideHideDelayValueBox))
        {
            min = 0;
            max = 4000;
            return true;
        }

        min = 0;
        max = 0;
        return false;
    }

    private static bool IsCandidateInputAllowed(WpfTextBox box, string input, int min, int max)
    {
        if (!Regex.IsMatch(input, @"^-?\d+$"))
        {
            return false;
        }

        var candidate = BuildCandidateText(box, input);
        if (candidate == "-" && min < 0)
        {
            return true;
        }

        if (!int.TryParse(candidate, out var value))
        {
            return false;
        }

        return min < 0 || value >= 0;
    }

    private static string BuildCandidateText(WpfTextBox box, string input)
    {
        var current = box.Text;
        var start = box.SelectionStart;
        var length = box.SelectionLength;
        var before = current[..start];
        var after = current[(start + length)..];
        return $"{before}{input}{after}";
    }

    private static bool IsValidHexColor(string value)
    {
        return Regex.IsMatch(value, "^#([0-9A-Fa-f]{6}|[0-9A-Fa-f]{3})$");
    }

    private static string NormalizeHexInput(string value)
    {
        var raw = value ?? string.Empty;
        if (raw.Length == 0)
        {
            return "#";
        }

        var withoutHashes = raw.Replace("#", string.Empty).Trim();
        return $"#{withoutHashes}".ToUpperInvariant();
    }

    private void RefreshColorPresetSelections()
    {
        SyncPresetSelection(SolidPresetCombo, Config.SolidColorHex);
        SyncPresetSelection(OutlinePresetCombo, Config.OutlineColorHex);
        UpdateColorEditorStates();
    }

    private void UpdateColorEditorStates()
    {
        HexTextBox.IsEnabled = true;
        CustomColorNameTextBox.IsEnabled = true;
        SaveCustomColorButton.IsEnabled = true;
        DeleteCustomColorButton.IsEnabled = CustomSavedColorsCombo.SelectedItem is NamedHexColor;
    }

    private static void SyncPresetSelection(WpfComboBox comboBox, string hexValue)
    {
        foreach (var rawItem in comboBox.Items)
        {
            if (rawItem is not ComboBoxItem item || item.Tag is not string tag)
            {
                continue;
            }

            var parsedTagHex = ExtractHexFromPresetTag(tag);
            if (parsedTagHex is not null && string.Equals(parsedTagHex, hexValue, StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedItem = item;
                return;
            }
        }

        comboBox.SelectedItem = null;
    }

    private static string? ExtractHexFromPresetTag(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return null;
        }

        if (string.Equals(tag, "custom", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (tag.StartsWith("custom:", StringComparison.OrdinalIgnoreCase))
        {
            return tag["custom:".Length..];
        }

        return tag;
    }

    private void UpdatePageHeader()
    {
        var l = Config.Language;
        var (titleKey, subtitleKey, icon) = _currentCategory switch
        {
            SettingsCategory.Info => ("CategoryInfo", "SubtitleInfo", "\uE946"),
            SettingsCategory.QuickAccess => ("CategoryQuickAccess", "SubtitleQuickAccess", "\uE765"),
            SettingsCategory.Position => ("CategoryPosition", "SubtitlePosition", "\uE8A9"),
            SettingsCategory.Appearance => ("CategoryAppearance", "SubtitleAppearance", "\uE790"),
            SettingsCategory.Shortcuts => ("CategoryShortcuts", "SubtitleShortcuts", "\uE8A7"),
            SettingsCategory.Behaviour => ("CategoryBehaviour", "SubtitleBehaviour", "\uE9E9"),
            SettingsCategory.Details => ("CategoryDetails", "SubtitleDetails", "\uE8A5"),
            SettingsCategory.Troubleshooting => ("CategoryTroubleshooting", "SubtitleTroubleshooting", "\uE90F"),
            _ => ("CategoryGeneral", "SubtitleGeneral", "\uE713")
        };

        PageHeaderTitle.Text = LocalizationService.Get(l, titleKey);
        PageHeaderSubtitle.Text = LocalizationService.Get(l, subtitleKey);
        PageHeaderIcon.Text = icon;
    }

    private void HighlightCategoryButton(System.Windows.Controls.Button button, bool isActive)
    {
        button.FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal;
        button.Opacity = 1;
        FluentChrome.SetIsActive(button, isActive);
        button.ClearValue(System.Windows.Controls.Control.BackgroundProperty);
        button.ClearValue(System.Windows.Controls.Control.BorderBrushProperty);
    }

    private void RebuildQuickAccessTab()
    {
        QuickAccessItemsHost.Children.Clear();

        foreach (var optionKey in GetVisiblePinnedOptions())
        {
            QuickAccessItemsHost.Children.Add(CreateQuickAccessCard(optionKey));
        }

        QuickAccessEmptyText.Visibility = QuickAccessItemsHost.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UnpinAllQuickAccessButton.Visibility = QuickAccessItemsHost.Children.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private Border CreateQuickAccessCard(string optionKey)
    {
        var card = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 12, 14, 12),
            Margin = new Thickness(0, 0, 0, 10),
            Tag = optionKey,
            AllowDrop = true
        };
        card.SetResourceReference(Border.BackgroundProperty, "CardBackgroundBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "CardBorderBrush");
        card.DragOver += QuickAccessCard_DragOver;
        card.Drop += QuickAccessCard_Drop;

        var host = new StackPanel();
        var header = new DockPanel { Tag = optionKey };
        header.PreviewMouseLeftButtonDown += QuickAccessHeader_PreviewMouseLeftButtonDown;
        header.PreviewMouseMove += QuickAccessHeader_PreviewMouseMove;
        var dragHint = new TextBlock
        {
            Text = "\uE76F",
            FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            FontSize = 14,
            Margin = new Thickness(0, 0, 8, 0),
            Cursor = System.Windows.Input.Cursors.SizeAll,
            VerticalAlignment = VerticalAlignment.Center
        };
        dragHint.SetResourceReference(TextBlock.ForegroundProperty, "MutedForegroundBrush");
        DockPanel.SetDock(dragHint, Dock.Left);
        header.Children.Add(dragHint);
        var title = new TextBlock
        {
            FontWeight = FontWeights.SemiBold,
            Text = GetOptionDisplayName(optionKey)
        };
        DockPanel.SetDock(title, Dock.Left);
        header.Children.Add(title);

        var unpin = new System.Windows.Controls.Button
        {
            Content = LocalizationService.Get(Config.Language, "Unpin"),
            Tag = optionKey,
            Padding = new Thickness(10, 2, 10, 2),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            MinWidth = 70
        };
        unpin.Click += UnpinOption_Click;
        header.Children.Add(unpin);

        host.Children.Add(header);
        var editor = CreateQuickAccessEditor(optionKey);
        ApplyQuickAccessOptionState(optionKey, editor);
        host.Children.Add(editor);
        card.Child = host;
        return card;
    }

    private IEnumerable<string> GetVisiblePinnedOptions()
    {
        return Config.QuickAccessPinnedOptions
            .Where(_supportedPins.Contains)
            .Where(IsQuickAccessOptionVisible);
    }

    private bool IsQuickAccessOptionVisible(string optionKey)
    {
        if (AutoHideDependentQuickAccessOptions.Contains(optionKey) && !Config.AutoHide)
        {
            return false;
        }

        if (HoverDependentQuickAccessOptions.Contains(optionKey) && !Config.EnableHoverEffects)
        {
            return false;
        }

        if (OutlineDependentQuickAccessOptions.Contains(optionKey) && !Config.ShowOutline)
        {
            return false;
        }

        if (string.Equals(optionKey, "DockWidth", StringComparison.Ordinal) && Config.AutoAdjustDockWidth)
        {
            return false;
        }

        if (string.Equals(optionKey, "CornerRadius", StringComparison.Ordinal) && !Config.RoundedCorners)
        {
            return false;
        }

        return true;
    }

    private void ResetPinnedQuickAccessOptions(IEnumerable<string> optionKeys)
    {
        foreach (var optionKey in optionKeys)
        {
            switch (optionKey)
            {
                case "Language":
                    Config.Language = AppLanguage.English;
                    break;
                case "Theme":
                    Config.Theme = AppTheme.Dark;
                    break;
                case "Edge":
                    Config.Edge = DockEdge.Top;
                    break;
                case "AutoAdjustDockWidth":
                    Config.AutoAdjustDockWidth = false;
                    break;
                case "DockWidth":
                    Config.DockWidth = DockConfig.GetDefaultDockWidthLogical(Config.Edge);
                    break;
                case "DockHeight":
                    Config.DockHeight = DockConfig.GetDockHeightDefault();
                    break;
                case "Offset":
                    Config.Offset = 0;
                    break;
                case "SolidColorHex":
                    Config.SolidColorHex = "#1F1F1F";
                    break;
                case "DockOpacity":
                    Config.DockOpacity = 60;
                    break;
                case "ShowOutline":
                    Config.ShowOutline = true;
                    Config.OutlineEdgeTop = true;
                    Config.OutlineEdgeBottom = true;
                    Config.OutlineEdgeLeft = true;
                    Config.OutlineEdgeRight = true;
                    break;
                case "OutlineColorHex":
                    Config.OutlineColorHex = "#FFFFFF";
                    break;
                case "OutlineOpacity":
                    Config.OutlineOpacity = 20;
                    break;
                case "OutlineThickness":
                    Config.OutlineThickness = DockConfig.GetOutlineThicknessDefault();
                    break;
                case "RoundedCorners":
                    Config.RoundedCorners = true;
                    break;
                case "CornerRadius":
                    Config.CornerRadius = DockConfig.GetCornerRadiusDefault();
                    break;
                case "IconSize":
                    Config.IconSize = DockConfig.GetIconSizeDefault();
                    break;
                case "IconSpacing":
                    Config.IconSpacing = DockConfig.GetIconSpacingDefault();
                    break;
                case "ButtonAlignment":
                    Config.ButtonAlignment = ButtonAlignment.Center;
                    break;
                case "EnableHoverEffects":
                    Config.EnableHoverEffects = true;
                    break;
                case "HoverMagnification":
                    Config.HoverMagnification = 90;
                    break;
                case "HoverEffectRadius":
                    Config.HoverEffectRadiusCm = DockConfig.GetDefaultHoverEffectRadiusCm();
                    break;
                case "AutoHide":
                    Config.AutoHide = false;
                    break;
                case "AlwaysOnTop":
                    Config.AlwaysOnTop = true;
                    break;
                case "AutoHideRevealMode":
                    Config.AutoHideRevealMode = AutoHideRevealMode.Proximity;
                    break;
                case "AutoHideProximityDistance":
                    Config.AutoHideProximityDistanceCm = DockConfig.GetDefaultAutoHideProximityDistanceCm();
                    break;
                case "AutoHideSlideAnimation":
                    Config.AutoHideSlideAnimation = true;
                    break;
                case "AutoHideSlideDurationMs":
                    Config.AutoHideSlideDurationMs = 150;
                    break;
                case "AutoHideRevealDelayMs":
                    Config.AutoHideRevealDelayMs = 150;
                    break;
                case "AutoHideHideDelayMs":
                    Config.AutoHideHideDelayMs = 250;
                    break;
            }
        }
    }

    private void UnpinAllQuickAccessButton_Click(object sender, RoutedEventArgs e)
    {
        Config.QuickAccessPinnedOptions.Clear();
        RebuildQuickAccessTab();
        RefreshPinButtons();
    }

    private void QuickAccessHeader_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DockPanel header || header.Tag is not string optionKey)
        {
            return;
        }

        if (IsOrHasAncestor<System.Windows.Controls.Button>(e.OriginalSource as DependencyObject))
        {
            return;
        }

        _draggedQuickAccessOptionKey = optionKey;
        _quickAccessDragStartPoint = e.GetPosition(QuickAccessItemsHost);
    }

    private void QuickAccessHeader_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_draggedQuickAccessOptionKey is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var position = e.GetPosition(QuickAccessItemsHost);
        var dragThresholdX = Math.Abs(position.X - _quickAccessDragStartPoint.X);
        var dragThresholdY = Math.Abs(position.Y - _quickAccessDragStartPoint.Y);
        if (dragThresholdX < SystemParameters.MinimumHorizontalDragDistance &&
            dragThresholdY < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        DragDrop.DoDragDrop((DependencyObject)sender, new System.Windows.DataObject(typeof(string), _draggedQuickAccessOptionKey), System.Windows.DragDropEffects.Move);
        _draggedQuickAccessOptionKey = null;
    }

    private void QuickAccessCard_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(string))
            ? System.Windows.DragDropEffects.Move
            : System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private void QuickAccessCard_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (sender is not Border targetCard || targetCard.Tag is not string targetKey)
        {
            return;
        }

        if (!e.Data.GetDataPresent(typeof(string)) || e.Data.GetData(typeof(string)) is not string sourceKey)
        {
            return;
        }

        var sourceIndex = Config.QuickAccessPinnedOptions.IndexOf(sourceKey);
        var targetIndex = Config.QuickAccessPinnedOptions.IndexOf(targetKey);
        if (sourceIndex < 0 || targetIndex < 0 || sourceIndex == targetIndex)
        {
            return;
        }

        Config.QuickAccessPinnedOptions.Move(sourceIndex, targetIndex);
        _draggedQuickAccessOptionKey = null;
        RebuildQuickAccessTab();
        RefreshPinButtons();
    }

    private static bool IsOrHasAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        var current = source;
        while (current is not null)
        {
            if (current is T)
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private void ApplyQuickAccessOptionState(string optionKey, FrameworkElement editor)
    {
        if (!AutoHideDependentQuickAccessOptions.Contains(optionKey))
        {
            return;
        }

        editor.SetBinding(IsEnabledProperty, new WpfBinding(nameof(DockConfig.AutoHide))
        {
            Source = Config,
            Mode = System.Windows.Data.BindingMode.OneWay
        });
    }

    private FrameworkElement CreateQuickAccessEditor(string optionKey)
    {
        switch (optionKey)
        {
            case "Language":
                var language = new WpfComboBox
                {
                    Margin = new Thickness(0, 6, 0, 0),
                    SelectedValuePath = "Tag"
                };
                language.Items.Add(new ComboBoxItem { Content = LocalizationService.Get(Config.Language, "Spanish"), Tag = AppLanguage.Spanish });
                language.Items.Add(new ComboBoxItem { Content = LocalizationService.Get(Config.Language, "English"), Tag = AppLanguage.English });
                language.SetBinding(WpfComboBox.SelectedValueProperty, new WpfBinding(nameof(DockConfig.Language))
                {
                    Source = Config,
                    Mode = System.Windows.Data.BindingMode.TwoWay
                });
                return language;

            case "Theme":
                var darkMode = new WpfCheckBox { Margin = new Thickness(0, 6, 0, 0), Content = LocalizationService.Get(Config.Language, "DarkMode") };
                darkMode.IsChecked = IsDarkMode;
                darkMode.Checked += (_, _) => IsDarkMode = true;
                darkMode.Unchecked += (_, _) => IsDarkMode = false;
                return darkMode;

            case "Edge":
                var edge = new WpfComboBox
                {
                    Margin = new Thickness(0, 6, 0, 0),
                    SelectedValuePath = "Tag"
                };
                edge.Items.Add(new ComboBoxItem { Content = LocalizationService.Get(Config.Language, "Top"), Tag = DockEdge.Top });
                edge.Items.Add(new ComboBoxItem { Content = LocalizationService.Get(Config.Language, "Bottom"), Tag = DockEdge.Bottom });
                edge.Items.Add(new ComboBoxItem { Content = LocalizationService.Get(Config.Language, "Left"), Tag = DockEdge.Left });
                edge.Items.Add(new ComboBoxItem { Content = LocalizationService.Get(Config.Language, "Right"), Tag = DockEdge.Right });
                edge.SetBinding(WpfComboBox.SelectedValueProperty, new WpfBinding(nameof(DockConfig.Edge))
                {
                    Source = Config,
                    Mode = System.Windows.Data.BindingMode.TwoWay
                });
                return edge;

            case "AutoAdjustDockWidth":
                return CreateQuickCheckBox(nameof(DockConfig.AutoAdjustDockWidth), "AutoAdjustDockWidth");
            case "DockWidth":
                return CreateQuickDockWidthSlider();
            case "DockHeight":
                return CreateQuickPercentSlider(
                    nameof(DockConfig.DockHeight),
                    DockConfig.GetDockHeightMin(),
                    DockConfig.GetDockHeightMax(),
                    1);
            case "Offset":
                return CreateQuickSlider(nameof(DockConfig.Offset), -1500, 1500, 10);
            case "DockOpacity":
                return CreateQuickSlider(nameof(DockConfig.DockOpacity), 0, 100, 5);
            case "OutlineOpacity":
                return CreateQuickSlider(nameof(DockConfig.OutlineOpacity), 0, 100, 5);
            case "IconSize":
                return CreateQuickPercentSlider(
                    nameof(DockConfig.IconSize),
                    DockConfig.GetIconSizeMin(),
                    DockConfig.GetIconSizeMax(),
                    1);
            case "IconSpacing":
                return CreateQuickPercentSlider(
                    nameof(DockConfig.IconSpacing),
                    DockConfig.GetIconSpacingMin(),
                    DockConfig.GetIconSpacingMax(),
                    1);
            case "ButtonAlignment":
                return CreateButtonAlignmentCombo();
            case "EnableHoverEffects":
                return CreateQuickCheckBox(nameof(DockConfig.EnableHoverEffects), "HoverEffectsEnabled");
            case "HoverMagnification":
                return CreateQuickSlider(nameof(DockConfig.HoverMagnification), 0, 150, 5);
            case "HoverEffectRadius":
                return CreateQuickHoverRadiusSlider();

            case "SolidColorHex":
                return CreateQuickColorEditor(nameof(DockConfig.SolidColorHex));
            case "OutlineColorHex":
                return CreateQuickColorEditor(nameof(DockConfig.OutlineColorHex));

            case "ShowOutline":
                return CreateQuickShowOutlineEditor();
            case "OutlineThickness":
                return CreateQuickSlider(
                    nameof(DockConfig.OutlineThickness),
                    DockConfig.GetOutlineThicknessMin(),
                    DockConfig.GetOutlineThicknessMax(),
                    0.25);
            case "RoundedCorners":
                return CreateQuickCheckBox(nameof(DockConfig.RoundedCorners));
            case "CornerRadius":
                return CreateQuickSlider(
                    nameof(DockConfig.CornerRadius),
                    DockConfig.GetCornerRadiusMin(),
                    DockConfig.GetCornerRadiusMax(),
                    1);
            case "AutoHide":
                return CreateQuickCheckBox(nameof(DockConfig.AutoHide));
            case "AlwaysOnTop":
                return CreateQuickCheckBox(nameof(DockConfig.AlwaysOnTop));
            case "AutoHideRevealMode":
                return CreateAutoHideRevealModeCombo();
            case "AutoHideProximityDistance":
                return CreateQuickAutoHideProximitySlider();
            case "AutoHideSlideAnimation":
                return CreateQuickCheckBox(nameof(DockConfig.AutoHideSlideAnimation), "AutoHideSlideEnabled");
            case "AutoHideSlideDurationMs":
                return CreateQuickSlider(nameof(DockConfig.AutoHideSlideDurationMs), 0, 1200, 25);
            case "AutoHideRevealDelayMs":
                return CreateQuickSlider(nameof(DockConfig.AutoHideRevealDelayMs), 0, 4000, 50);
            case "AutoHideHideDelayMs":
                return CreateQuickSlider(nameof(DockConfig.AutoHideHideDelayMs), 0, 4000, 50);

            default:
                return new TextBlock { Margin = new Thickness(0, 6, 0, 0), Text = optionKey };
        }
    }

    private FrameworkElement CreateQuickSlider(string propertyName, double minimum, double maximum, double tickFrequency)
    {
        var slider = new Slider
        {
            Margin = new Thickness(0, 6, 0, 0),
            Minimum = minimum,
            Maximum = maximum,
            TickFrequency = tickFrequency,
            IsSnapToTickEnabled = true
        };
        slider.SetBinding(Slider.ValueProperty, new WpfBinding(propertyName)
        {
            Source = Config,
            Mode = System.Windows.Data.BindingMode.TwoWay,
            UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged
        });
        return slider;
    }

    private FrameworkElement CreateQuickPercentSlider(string propertyName, double minimum, double maximum, double tickFrequency)
    {
        var slider = new Slider
        {
            Margin = new Thickness(0, 6, 0, 0),
            Minimum = minimum,
            Maximum = maximum,
            TickFrequency = tickFrequency,
            IsSnapToTickEnabled = false
        };
        slider.SetBinding(Slider.ValueProperty, new WpfBinding(propertyName)
        {
            Source = Config,
            Mode = System.Windows.Data.BindingMode.TwoWay,
            UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged
        });
        return slider;
    }

    private FrameworkElement CreateQuickDockWidthSlider()
    {
        var slider = new Slider
        {
            Margin = new Thickness(0, 6, 0, 0),
            TickFrequency = 1,
            IsSnapToTickEnabled = false
        };
        slider.SetBinding(Slider.MinimumProperty, new WpfBinding(nameof(DockWidthMin))
        {
            Source = this,
            Mode = System.Windows.Data.BindingMode.OneWay
        });
        slider.SetBinding(Slider.MaximumProperty, new WpfBinding(nameof(DockWidthMax))
        {
            Source = this,
            Mode = System.Windows.Data.BindingMode.OneWay
        });
        slider.SetBinding(Slider.ValueProperty, new WpfBinding(nameof(DockConfig.DockWidth))
        {
            Source = Config,
            Mode = System.Windows.Data.BindingMode.TwoWay,
            UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged
        });
        return slider;
    }

    private FrameworkElement CreateQuickHoverRadiusSlider()
    {
        var slider = new Slider
        {
            Margin = new Thickness(0, 6, 0, 0),
            TickFrequency = 0.02,
            IsSnapToTickEnabled = false
        };
        slider.SetBinding(Slider.MinimumProperty, new WpfBinding(nameof(HoverRadiusMinCm))
        {
            Source = this,
            Mode = System.Windows.Data.BindingMode.OneWay
        });
        slider.SetBinding(Slider.MaximumProperty, new WpfBinding(nameof(HoverRadiusMaxCm))
        {
            Source = this,
            Mode = System.Windows.Data.BindingMode.OneWay
        });
        slider.SetBinding(Slider.ValueProperty, new WpfBinding(nameof(DockConfig.HoverEffectRadiusCm))
        {
            Source = Config,
            Mode = System.Windows.Data.BindingMode.TwoWay,
            UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged
        });
        return slider;
    }

    private FrameworkElement CreateQuickAutoHideProximitySlider()
    {
        var slider = new Slider
        {
            Margin = new Thickness(0, 6, 0, 0),
            TickFrequency = 0.02,
            IsSnapToTickEnabled = false
        };
        slider.SetBinding(Slider.MinimumProperty, new WpfBinding(nameof(AutoHideProximityMinCm))
        {
            Source = this,
            Mode = System.Windows.Data.BindingMode.OneWay
        });
        slider.SetBinding(Slider.MaximumProperty, new WpfBinding(nameof(AutoHideProximityMaxCm))
        {
            Source = this,
            Mode = System.Windows.Data.BindingMode.OneWay
        });
        slider.SetBinding(Slider.ValueProperty, new WpfBinding(nameof(DockConfig.AutoHideProximityDistanceCm))
        {
            Source = Config,
            Mode = System.Windows.Data.BindingMode.TwoWay,
            UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged
        });
        return slider;
    }

    private FrameworkElement CreateQuickHexEditor(string propertyName)
    {
        var textBox = new WpfTextBox { Margin = new Thickness(0, 6, 0, 0) };
        textBox.SetBinding(WpfTextBox.TextProperty, new WpfBinding(propertyName)
        {
            Source = Config,
            Mode = System.Windows.Data.BindingMode.TwoWay,
            UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged
        });
        return textBox;
    }

    private FrameworkElement CreateQuickColorEditor(string propertyName)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };

        var presets = new WpfComboBox
        {
            Margin = new Thickness(0, 0, 0, 6)
        };
        AddQuickColorOptions(presets);

        var textBox = new WpfTextBox();
        textBox.SetBinding(WpfTextBox.TextProperty, new WpfBinding(propertyName)
        {
            Source = Config,
            Mode = System.Windows.Data.BindingMode.TwoWay,
            UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged
        });

        var getValue = propertyName == nameof(DockConfig.SolidColorHex)
            ? (Func<string>)(() => Config.SolidColorHex)
            : () => Config.OutlineColorHex;
        var setValue = propertyName == nameof(DockConfig.SolidColorHex)
            ? (Action<string>)(v => Config.SolidColorHex = v)
            : v => Config.OutlineColorHex = v;

        presets.SelectionChanged += (_, _) =>
        {
            if (presets.SelectedItem is not ComboBoxItem item || item.Tag is not string selectedTag)
            {
                return;
            }

            if (string.Equals(selectedTag, "custom", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            setValue(selectedTag);
            textBox.Text = getValue();
            SyncPresetSelection(presets, getValue());
        };

        textBox.TextChanged += (_, _) =>
        {
            if (!textBox.Text.StartsWith('#'))
            {
                var caret = textBox.CaretIndex;
                textBox.Text = $"#{textBox.Text}";
                textBox.CaretIndex = Math.Min(textBox.Text.Length, caret + 1);
            }

            SyncPresetSelection(presets, getValue());
        };
        textBox.LostFocus += (_, _) =>
        {
            if (!IsValidHexColor(textBox.Text))
            {
                textBox.Text = getValue();
            }
        };
        SyncPresetSelection(presets, getValue());

        panel.Children.Add(presets);
        panel.Children.Add(textBox);
        return panel;
    }

    private void AddQuickColorOptions(WpfComboBox comboBox)
    {
        foreach (var color in _savedColors.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
        {
            comboBox.Items.Add(new ComboBoxItem { Content = color.Name, Tag = color.Hex });
        }

    }

    private FrameworkElement CreateQuickCheckBox(string propertyName, string? labelKey = null)
    {
        var check = new WpfCheckBox
        {
            Margin = new Thickness(0, 6, 0, 0),
            Content = labelKey is null ? null : LocalizationService.Get(Config.Language, labelKey)
        };
        check.SetBinding(WpfCheckBox.IsCheckedProperty, new WpfBinding(propertyName)
        {
            Source = Config,
            Mode = System.Windows.Data.BindingMode.TwoWay
        });
        return check;
    }

    private FrameworkElement CreateQuickShowOutlineEditor()
    {
        var l = Config.Language;
        var root = new StackPanel();

        var showCheck = new WpfCheckBox
        {
            Margin = new Thickness(0, 6, 0, 0),
            Content = LocalizationService.Get(l, "OutlineEnabled")
        };
        showCheck.SetBinding(WpfCheckBox.IsCheckedProperty, new WpfBinding(nameof(DockConfig.ShowOutline))
        {
            Source = Config,
            Mode = System.Windows.Data.BindingMode.TwoWay
        });
        showCheck.SetBinding(System.Windows.Controls.Control.ForegroundProperty, new WpfBinding(nameof(Foreground)) { Source = this });

        var edgesBlock = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        edgesBlock.SetBinding(UIElement.IsEnabledProperty, new WpfBinding(nameof(DockConfig.ShowOutline))
        {
            Source = Config,
            Mode = System.Windows.Data.BindingMode.OneWay
        });

        var edgesLabel = new TextBlock
        {
            FontWeight = FontWeights.SemiBold,
            Opacity = 0.88,
            Margin = new Thickness(0, 0, 0, 8),
            Text = LocalizationService.Get(l, "OutlineEdgesLabel")
        };
        edgesLabel.SetBinding(TextBlock.ForegroundProperty, new WpfBinding(nameof(Foreground)) { Source = this });

        var wrap = new WrapPanel();
        wrap.Children.Add(CreateQuickOutlineEdgeCheck(nameof(DockConfig.OutlineEdgeTop), "OutlineEdgeTop"));
        wrap.Children.Add(CreateQuickOutlineEdgeCheck(nameof(DockConfig.OutlineEdgeBottom), "OutlineEdgeBottom"));
        wrap.Children.Add(CreateQuickOutlineEdgeCheck(nameof(DockConfig.OutlineEdgeLeft), "OutlineEdgeLeft"));
        wrap.Children.Add(CreateQuickOutlineEdgeCheck(nameof(DockConfig.OutlineEdgeRight), "OutlineEdgeRight"));

        edgesBlock.Children.Add(edgesLabel);
        edgesBlock.Children.Add(wrap);

        root.Children.Add(showCheck);
        root.Children.Add(edgesBlock);
        return root;
    }

    private WpfCheckBox CreateQuickOutlineEdgeCheck(string propertyName, string localizationKey)
    {
        var c = new WpfCheckBox
        {
            Margin = new Thickness(0, 0, 12, 4),
            Content = LocalizationService.Get(Config.Language, localizationKey)
        };
        c.SetBinding(WpfCheckBox.IsCheckedProperty, new WpfBinding(propertyName)
        {
            Source = Config,
            Mode = System.Windows.Data.BindingMode.TwoWay
        });
        c.SetBinding(System.Windows.Controls.Control.ForegroundProperty, new WpfBinding(nameof(Foreground)) { Source = this });
        return c;
    }

    private string GetOptionDisplayName(string optionKey)
    {
        return optionKey switch
        {
            "Language" => LocalizationService.Get(Config.Language, "Language"),
            "Theme" => LocalizationService.Get(Config.Language, "Theme"),
            "Edge" => LocalizationService.Get(Config.Language, "DockAlignment"),
            "AutoAdjustDockWidth" => LocalizationService.Get(Config.Language, "AutoAdjustDockWidth"),
            "DockWidth" => LocalizationService.Get(Config.Language, "DockWidthPx"),
            "DockHeight" => LocalizationService.Get(Config.Language, "DockHeightPx"),
            "Offset" => LocalizationService.Get(Config.Language, "Offset"),
            "SolidColorHex" => LocalizationService.Get(Config.Language, "SolidPreset"),
            "DockOpacity" => LocalizationService.Get(Config.Language, "DockOpacity"),
            "ShowOutline" => LocalizationService.Get(Config.Language, "OutlineEnabled"),
            "OutlineColorHex" => LocalizationService.Get(Config.Language, "OutlineColor"),
            "OutlineOpacity" => LocalizationService.Get(Config.Language, "OutlineOpacity"),
            "OutlineThickness" => LocalizationService.Get(Config.Language, "OutlineThickness"),
            "RoundedCorners" => LocalizationService.Get(Config.Language, "RoundedCorners"),
            "CornerRadius" => LocalizationService.Get(Config.Language, "CornerRadius"),
            "IconSize" => LocalizationService.Get(Config.Language, "IconSizePx"),
            "IconSpacing" => LocalizationService.Get(Config.Language, "IconSpacingPx"),
            "ButtonAlignment" => LocalizationService.Get(Config.Language, "ButtonAlignment"),
            "EnableHoverEffects" => LocalizationService.Get(Config.Language, "HoverEffectsEnabled"),
            "HoverMagnification" => LocalizationService.Get(Config.Language, "HoverMagnification"),
            "HoverEffectRadius" => LocalizationService.Get(Config.Language, "HoverRadiusCm"),
            "AutoHide" => LocalizationService.Get(Config.Language, "AutoHide"),
            "AlwaysOnTop" => LocalizationService.Get(Config.Language, "AlwaysOnTop"),
            "AutoHideRevealMode" => LocalizationService.Get(Config.Language, "AutoHideRevealMode"),
            "AutoHideProximityDistance" => LocalizationService.Get(Config.Language, "AutoHideProximityDistance"),
            "AutoHideSlideAnimation" => LocalizationService.Get(Config.Language, "AutoHideSlideEnabled"),
            "AutoHideSlideDurationMs" => LocalizationService.Get(Config.Language, "AutoHideSlideSpeed"),
            "AutoHideRevealDelayMs" => LocalizationService.Get(Config.Language, "AutoHideRevealDelay"),
            "AutoHideHideDelayMs" => LocalizationService.Get(Config.Language, "AutoHideHideDelay"),
            _ => optionKey
        };
    }

    private void RefreshButtonAlignmentOptions()
    {
        var isVertical = Config.Edge is DockEdge.Left or DockEdge.Right;
        if (isVertical)
        {
            ButtonAlignmentStartOption.Content = LocalizationService.Get(Config.Language, "Top");
            ButtonAlignmentCenterOption.Content = LocalizationService.Get(Config.Language, "Middle");
            ButtonAlignmentEndOption.Content = LocalizationService.Get(Config.Language, "Bottom");
            return;
        }

        ButtonAlignmentStartOption.Content = LocalizationService.Get(Config.Language, "Left");
        ButtonAlignmentCenterOption.Content = LocalizationService.Get(Config.Language, "Center");
        ButtonAlignmentEndOption.Content = LocalizationService.Get(Config.Language, "Right");
    }

    private FrameworkElement CreateButtonAlignmentCombo()
    {
        var combo = new WpfComboBox
        {
            Margin = new Thickness(0, 6, 0, 0),
            SelectedValuePath = "Tag"
        };

        var isVertical = Config.Edge is DockEdge.Left or DockEdge.Right;
        combo.Items.Add(new ComboBoxItem
        {
            Content = LocalizationService.Get(Config.Language, isVertical ? "Top" : "Left"),
            Tag = ButtonAlignment.Start
        });
        combo.Items.Add(new ComboBoxItem
        {
            Content = LocalizationService.Get(Config.Language, isVertical ? "Middle" : "Center"),
            Tag = ButtonAlignment.Center
        });
        combo.Items.Add(new ComboBoxItem
        {
            Content = LocalizationService.Get(Config.Language, isVertical ? "Bottom" : "Right"),
            Tag = ButtonAlignment.End
        });

        combo.SetBinding(WpfComboBox.SelectedValueProperty, new WpfBinding(nameof(DockConfig.ButtonAlignment))
        {
            Source = Config,
            Mode = System.Windows.Data.BindingMode.TwoWay
        });

        return combo;
    }

    private FrameworkElement CreateAutoHideRevealModeCombo()
    {
        var combo = new WpfComboBox
        {
            Margin = new Thickness(0, 6, 0, 0),
            SelectedValuePath = "Tag"
        };

        combo.Items.Add(new ComboBoxItem
        {
            Content = LocalizationService.Get(Config.Language, "AutoHideRevealModeEdgeTouch"),
            Tag = AutoHideRevealMode.EdgeTouch
        });
        combo.Items.Add(new ComboBoxItem
        {
            Content = LocalizationService.Get(Config.Language, "AutoHideRevealModeProximity"),
            Tag = AutoHideRevealMode.Proximity
        });

        combo.SetBinding(WpfComboBox.SelectedValueProperty, new WpfBinding(nameof(DockConfig.AutoHideRevealMode))
        {
            Source = Config,
            Mode = System.Windows.Data.BindingMode.TwoWay
        });

        return combo;
    }

    private void RefreshPinButtons()
    {
        foreach (var button in FindVisualChildren<System.Windows.Controls.Button>(this))
        {
            if (button.Tag is not string tag || !tag.StartsWith("Pin:", StringComparison.Ordinal))
            {
                continue;
            }

            var key = tag["Pin:".Length..];
            var pinned = Config.QuickAccessPinnedOptions.Contains(key);
            button.Content = null;
            FluentChrome.SetIcon(button, pinned ? "\uE841" : "\uE718");
            button.ToolTip = LocalizationService.Get(Config.Language, pinned ? "Pinned" : "Pin");
            if (pinned)
            {
                button.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "AccentBrush");
            }
            else
            {
                button.ClearValue(System.Windows.Controls.Control.ForegroundProperty);
            }
        }
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject dependencyObject) where T : DependencyObject
    {
        if (dependencyObject == null)
        {
            yield break;
        }

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(dependencyObject); i++)
        {
            var child = VisualTreeHelper.GetChild(dependencyObject, i);
            if (child is T typedChild)
            {
                yield return typedChild;
            }

            foreach (var nestedChild in FindVisualChildren<T>(child))
            {
                yield return nestedChild;
            }
        }
    }

    private sealed record NumericBinding(Slider Slider, double Min, double Max, int FractionDigits);
}
