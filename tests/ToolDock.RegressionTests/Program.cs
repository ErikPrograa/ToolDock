using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using ToolDock;
using ToolDock.Models;
using ToolDock.Services;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        DockMetrics.WorkAreaSpanLogicalProvider = _ => 1920;

        (string Name, Action Run)[] tests =
        [
            ("Independent distance ranges and notifications", DistanceRanges),
            ("Centimeter values survive DPI refresh", DistanceUnits),
            ("Conditional autohide options and resets", ConditionalOptions),
            ("Configuration JSON compatibility and roundtrip", ConfigurationRoundtrip),
            ("Preset JSON and application preserve independent values", PresetRoundtrip),
            ("Settings sliders, numeric editors and checkbox bindings", SettingsControls),
            ("Settings preset tracking, copying and translations", SettingsPresetsAndTranslations),
            ("Native window filters and visible-frame geometry", NativeAutoHideTests.Run)
        ];

        var failures = 0;
        foreach (var (name, run) in tests)
        {
            try
            {
                run();
                Console.WriteLine($"PASS {name}");
            }
            catch (Exception exception)
            {
                failures++;
                Console.Error.WriteLine($"FAIL {name}: {exception.GetBaseException()}");
            }
        }

        application.Shutdown();
        Console.WriteLine($"{tests.Length - failures}/{tests.Length} regression checks passed.");
        return failures == 0 ? 0 : 1;
    }

    private static void DistanceRanges()
    {
        var config = new DockConfig { HoverEffectRadius = 400, AutoHideProximityDistance = 1 };
        Equal(400, config.HoverEffectRadius, "Reducing activation distance must preserve hover radius");
        config.HoverEffectRadius = 300;
        Equal(1, config.AutoHideProximityDistance, "Changing hover radius must preserve activation distance");
        Equal(300, config.HoverEffectRadius, "Hover radius can exceed activation distance");

        var notifications = new List<string?>();
        config.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
        config.AutoHideProximityDistance = 2;
        Check(!notifications.Contains(nameof(DockConfig.HoverEffectRadius)), "Activation changes must not notify a hover change");

        config.AutoHideProximityDistance = 0;
        Equal(1, config.AutoHideProximityDistance, "Activation minimum");
        config.AutoHideProximityDistance = 1000;
        Equal(400, config.AutoHideProximityDistance, "Activation maximum");
        config.HoverEffectRadius = 0;
        Equal(20, config.HoverEffectRadius, "Hover minimum");
        config.AutoHideProximityDistance = 1;
        config.HoverEffectRadius = 1000;
        Equal(400, config.HoverEffectRadius, "Hover maximum is independent");

        config.ResetShortcuts();
        Equal(40, config.HoverEffectRadius, "Shortcut reset restores hover even with small activation distance");
        Equal(1, config.AutoHideProximityDistance, "Shortcut reset leaves activation distance alone");
        config.HoverEffectRadius = 400;
        config.ResetBehaviour();
        Equal(400, config.HoverEffectRadius, "Behaviour reset leaves hover radius alone");
        Equal(60, config.AutoHideProximityDistance, "Behaviour reset restores activation distance");
    }

    private static void DistanceUnits()
    {
        var config = new DockConfig
        {
            AutoHideProximityDistanceCm = 0.03,
            HoverEffectRadiusCm = DockMetrics.LogicalToCm(400)
        };
        var previousDpi = DockMetrics.GetDpiPixelsPerInchX;
        try
        {
            foreach (var dpi in new[] { 96d, 144d, 192d })
            {
                DockMetrics.GetDpiPixelsPerInchX = () => dpi;
                config.NotifyShellMetricsChanged();
                Equal(1, config.AutoHideProximityDistance, $"Activation at {dpi} DPI");
                Equal(400, config.HoverEffectRadius, $"Hover at {dpi} DPI");
                Near(DockMetrics.LogicalToCm(1), config.AutoHideProximityDistanceCm, "Logical centimeters stay constant");
            }
        }
        finally
        {
            DockMetrics.GetDpiPixelsPerInchX = previousDpi;
        }
    }

    private static void ConditionalOptions()
    {
        var config = new DockConfig { HideWhenWindowIntersects = true };
        Check(!config.HideWhenWindowIntersects, "Intersection condition requires autohide");
        config.AutoHide = true;
        config.HideWhenWindowIntersects = true;
        config.HideWhenMaximized = true;
        config.AlwaysOnTop = false;
        Check(!config.HideWhenWindowIntersects && config.HideWhenMaximized, "Selecting maximized disables intersection without requiring always-on-top");
        config.HideWhenWindowIntersects = true;
        Check(config.HideWhenWindowIntersects && !config.HideWhenMaximized, "Selecting intersection disables maximized");
        config.AutoHide = false;
        Check(!config.HideWhenWindowIntersects && !config.HideWhenMaximized, "Disabling autohide clears both conditions");
        config.AutoHide = true;
        config.HideWhenWindowIntersects = true;
        config.ResetBehaviour();
        Check(!config.AutoHide && !config.HideWhenWindowIntersects, "Behaviour reset clears the new option");
        Check(!DockPresetData.CreateDefault().HideWhenWindowIntersects, "Default presets preserve previous behaviour");
        Check(new DockConfig().DoNotShowOverFullscreen && DockPresetData.CreateDefault().DoNotShowOverFullscreen, "Fullscreen suppression is on by default");
    }

    private static void ConfigurationRoundtrip()
    {
        var directory = Directory.CreateTempSubdirectory("ToolDock-regression-");
        try
        {
            var path = Path.Combine(directory.FullName, "config.json");
            var service = new ConfigService(path);
            var config = IndependentConfig();
            service.Save(config);
            Check(!File.Exists(Path.Combine(directory.FullName, "Temp_TD")), "A finished save removes the temp folder");
            Check(Directory.GetFiles(directory.FullName, "*.tmp").Length == 0, "A finished save leaves no temp next to the JSON");
            var loaded = service.Load();
            Check(loaded.HideWhenWindowIntersects && !loaded.HideWhenMaximized, "The selected condition survives save/load");
            Equal(400, loaded.HoverEffectRadius, "Saved hover radius survives");
            Equal(1, loaded.AutoHideProximityDistance, "Saved small activation distance survives");

            var legacy = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            legacy.Remove(nameof(DockConfig.HideWhenWindowIntersects));
            legacy[nameof(DockConfig.HideWhenMaximized)] = true;
            File.WriteAllText(path, legacy.ToJsonString());
            loaded = service.Load();
            Check(!loaded.HideWhenWindowIntersects && loaded.HideWhenMaximized, "Existing configs retain maximized-only behaviour");
            Equal(400, loaded.HoverEffectRadius, "Existing settings are not discarded by a schema change");

            File.WriteAllText(path, $$"""
                {"ConfigSchemaVersion":{{DockConfig.CurrentSchemaVersion}},"HideWhenWindowIntersects":true,
                 "HoverEffectRadius":400,"AutoHideProximityDistance":1,"AutoHide":true}
                """);
            loaded = service.Load();
            Check(loaded.HideWhenWindowIntersects, "JSON property ordering must not lose the condition");
            Equal(400, loaded.HoverEffectRadius, "JSON property ordering must not clamp hover");
            foreach (var flags in new[]
            {
                "\"HideWhenMaximized\":true,\"HideWhenWindowIntersects\":true",
                "\"HideWhenWindowIntersects\":true,\"HideWhenMaximized\":true"
            })
            {
                File.WriteAllText(path, $$"""
                    {"ConfigSchemaVersion":{{DockConfig.CurrentSchemaVersion}},{{flags}},"AutoHide":true}
                    """);
                loaded = service.Load();
                Check(loaded.HideWhenWindowIntersects && !loaded.HideWhenMaximized,
                    "Legacy conflicting conditions prefer intersection regardless of JSON property order");
            }
            loaded.AutoHide = false;
            service.Save(loaded);
            Check(!service.Load().HideWhenWindowIntersects, "Disabled condition remains cleared on reload");
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static void PresetRoundtrip()
    {
        var preset = DockPresetData.FromConfig(IndependentConfig());
        var restored = JsonSerializer.Deserialize<DockPresetData>(JsonSerializer.Serialize(preset))!;
        var target = new DockConfig { AutoHideProximityDistance = 1 };
        restored.ApplyTo(target);
        Check(target.HideWhenWindowIntersects && !target.HideWhenMaximized, "Preset restores only the selected condition");
        Equal(400, target.HoverEffectRadius, "Preset hover is not capped by the target's activation distance");
        Equal(1, target.AutoHideProximityDistance, "Preset retains minimum activation distance");
        restored.HideWhenMaximized = true;
        restored.ApplyTo(target);
        Check(target.HideWhenWindowIntersects && !target.HideWhenMaximized, "Applying a legacy conflicting preset prefers intersection");
        object[] parseArguments = [JsonSerializer.Serialize(new[] { new DockPreset { Name = "Legacy", Data = restored } }), null!];
        Check((bool)Call(typeof(PresetService), "TryParse", parseArguments)!, "Legacy preset parses");
        var normalized = ((List<DockPreset>)parseArguments[1]).Single().Data;
        Check(normalized.HideWhenWindowIntersects && !normalized.HideWhenMaximized, "Loaded preset is normalized for UI matching");
        restored.HideWhenWindowIntersects = false;
        restored.ApplyTo(target);
        Check(!target.HideWhenWindowIntersects && target.HideWhenMaximized, "A maximized-only preset switches the selected mode");
        DockPresetData.CreateDefault().ApplyTo(target);
        Check(!target.AutoHide && !target.HideWhenWindowIntersects && !target.HideWhenMaximized, "Default preset clears all conditional autohide");
    }

    private static void SettingsControls()
    {
        var config = IndependentConfig();
        var window = new SettingsWindow(config);
        try
        {
            Call(window, "RegisterNumericBindings");
            Call(window, "ApplyPercentSliderRanges");
            FlushBindings();
            var hover = (Slider)window.FindName("HoverRadiusSlider");
            var activation = (Slider)window.FindName("AutoHideProximityDistanceSlider");
            Near(DockMetrics.LogicalToCm(400), hover.Maximum, "Main hover slider has an independent maximum");
            Near(DockMetrics.LogicalToCm(1), activation.Minimum, "Main activation slider exposes the new minimum");

            var activationBox = (TextBox)window.FindName("AutoHideProximityDistanceValueBox");
            activationBox.Text = "0.03";
            Call(window, "ApplyTextBoxValue", activationBox);
            var hoverBox = (TextBox)window.FindName("HoverRadiusValueBox");
            hoverBox.Text = "10.58";
            Call(window, "ApplyTextBoxValue", hoverBox);
            FlushBindings();
            Equal(1, config.AutoHideProximityDistance, "Numeric editor accepts sub-20-DIP activation");
            Equal(400, config.HoverEffectRadius, "Numeric editor accepts hover above activation distance");

            var quickHover = (Slider)Call(window, "CreateQuickHoverRadiusSlider")!;
            var quickActivation = (Slider)Call(window, "CreateQuickAutoHideProximitySlider")!;
            FlushBindings();
            Near(hover.Maximum, quickHover.Maximum, "Quick-access hover uses the same independent range");
            Near(activation.Minimum, quickActivation.Minimum, "Quick-access activation uses the same minimum");
            config.AutoHideProximityDistance = 10;
            FlushBindings();
            Equal(400, config.HoverEffectRadius, "UI bindings do not reintroduce the radius cap");
            Near(DockMetrics.LogicalToCm(400), hover.Maximum, "Changing activation keeps the hover slider maximum");

            var checkBox = (CheckBox)window.FindName("HideWhenWindowIntersectsCheck");
            checkBox.SetCurrentValue(ToggleButton.IsCheckedProperty, false);
            FlushBindings();
            Check(!config.HideWhenWindowIntersects, "Checkbox writes the configuration");
            config.HideWhenWindowIntersects = true;
            FlushBindings();
            Check(checkBox.IsChecked == true, "Checkbox reflects the configuration");
            var maximizedCheck = (CheckBox)window.FindName("HideWhenMaximizedCheck");
            maximizedCheck.SetCurrentValue(ToggleButton.IsCheckedProperty, true);
            FlushBindings();
            Check(maximizedCheck.IsChecked == true && checkBox.IsChecked == false &&
                config.HideWhenMaximized && !config.HideWhenWindowIntersects, "Maximized checkbox unchecks intersection");
            checkBox.SetCurrentValue(ToggleButton.IsCheckedProperty, true);
            FlushBindings();
            Check(checkBox.IsChecked == true && maximizedCheck.IsChecked == false &&
                config.HideWhenWindowIntersects && !config.HideWhenMaximized, "Intersection checkbox unchecks maximized");

            var toggleNames = ((StackPanel)checkBox.Parent).Children.Cast<UIElement>()
                .SelectMany(child => child is CheckBox toggle ? new[] { toggle } :
                    child is DockPanel row ? row.Children.OfType<CheckBox>() : Enumerable.Empty<CheckBox>())
                .Select(toggle => toggle.Name);
            Check(toggleNames.SequenceEqual(new[]
            {
                "AlwaysOnTopCheck", "DoNotShowOverFullscreenCheck", "OpenAppsOnDockMonitorCheck",
                "ReserveWorkAreaCheck", "AutohideCheck", "HideBehindTaskbarCheck",
                "HideWhenMaximizedCheck", "HideWhenWindowIntersectsCheck"
            }), "Behaviour toggles follow the requested order");
            config.AutoHide = false;
            FlushBindings();
            Check(!checkBox.IsEnabled && checkBox.IsChecked == false, "Checkbox disables and clears with autohide");
        }
        finally
        {
            window.Close();
        }
    }

    private static void SettingsPresetsAndTranslations()
    {
        var config = IndependentConfig();
        var window = new SettingsWindow(config);
        try
        {
            var preset = DockPresetData.FromConfig(config);
            var copy = (DockPresetData)Call(window, "SanitizePresetDataColors", preset)!;
            Check(copy.HideWhenWindowIntersects, "Settings preset sanitizer preserves intersection option");
            Check((bool)Call(typeof(SettingsWindow), "ArePresetDataEquivalent", preset, preset, false)!, "Equivalent presets match");
            var changed = DockPresetData.FromConfig(config);
            changed.HideWhenWindowIntersects = false;
            Check(!(bool)Call(typeof(SettingsWindow), "ArePresetDataEquivalent", preset, changed, false)!, "Different intersection options distinguish presets");
            Check((bool)Call(typeof(SettingsWindow), "IsPresetTrackedProperty", nameof(DockConfig.HideWhenWindowIntersects))!, "Intersection changes trigger preset tracking");

            Call(window, "RefreshTexts");
            var checkBox = (CheckBox)window.FindName("HideWhenWindowIntersectsCheck");
            Equal("Hide when a window intersects the dock", checkBox.Content as string, "Requested English label");
            config.Language = AppLanguage.Spanish;
            var spanish = LocalizationService.Get(AppLanguage.Spanish, "HideWhenWindowIntersects");
            Check(spanish != "HideWhenWindowIntersects" && spanish != LocalizationService.Get(AppLanguage.English, "HideWhenWindowIntersects"), "Spanish translation exists");
            Equal(spanish, checkBox.Content as string, "Changing language refreshes the new checkbox");
            Check(!window.IsLoaded && !window.IsVisible, "Settings checks never show a window or run startup persistence");
        }
        finally
        {
            window.Close();
        }
    }

    private static DockConfig IndependentConfig() => new()
    {
        AutoHide = true,
        HideWhenWindowIntersects = true,
        AutoHideProximityDistance = 1,
        HoverEffectRadius = 400
    };

    private static object? Call(object target, string name, params object[] arguments)
    {
        var type = target as Type ?? target.GetType();
        var method = type.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
            ?? throw new MissingMethodException(type.Name, name);
        return method.Invoke(target is Type ? null : target, arguments);
    }

    private static void FlushBindings() =>
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void Equal<T>(T expected, T actual, string message) =>
        Check(EqualityComparer<T>.Default.Equals(expected, actual), $"{message}: expected {expected}, got {actual}");

    private static void Near(double expected, double actual, string message) =>
        Check(Math.Abs(expected - actual) < 0.000001, $"{message}: expected {expected}, got {actual}");
}
