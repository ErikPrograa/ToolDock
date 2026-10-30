# ToolDock regression checks

Run on Windows with the .NET 9 SDK or newer:

```powershell
dotnet run --project tests/ToolDock.RegressionTests/ToolDock.RegressionTests.csproj -c Debug
```

This dependency-free console runner returns a nonzero exit code on failure. It checks independent distance limits, DPI refresh, configuration compatibility, presets, numeric editors, sliders, checkbox bindings and localization. Settings windows are constructed but never shown or loaded. Configuration persistence uses a temporary directory that is removed after the test; the user's portable configuration is not used.

The runner also checks the fullscreen-suppression default, that saves leave no loose `.tmp` files beside the JSON, and classification of instance configuration filenames separately from presets, colors, and shortcut exports. It does not execute troubleshooting resets or verify the complete close/delete/recycle/relaunch sequence.

Color-picker checks cover HEX/RGB/HSV roundtrips, numeric limits, invalid input, spectrum coordinates, and brightness changes through black. Integration checks automatically close or confirm the modal picker before rendering and verify that only confirmation copies the color into Settings; they do not save a custom color or use real mouse input.

These checks do not replace an interactive check of autohide animation, cursor reveal, or mixed-DPI displays.

Native checks also create a temporary window outside the virtual desktop without activating it. They exercise window filtering, both hide conditions, overlap versus edge contact, one-DIP activation and stable intersection bounds on all four dock edges. The dock itself is never shown. All test windows are closed afterwards.

Screen-edge reveal checks cover the final physical pixel, rejection of a pixel inward or on an adjacent display, the dock's activation span, reserved work-area boundaries, negative display origins, the hidden-strip mouse-enter path, and delayed reveal after leaving the edge. Geometry is checked with 96/120/144/192 DPI metric providers and synthetic display bounds; this does not emulate a real mixed-DPI desktop. The tests never move the cursor.

Drag-state checks pass overlap, pointer-reveal, and held-button states directly to the autohide decision method. Both reveal modes must start hiding or revealing while the button remains down, respect configured delays, and cancel pending transitions when overlap changes. Pointer activation retains priority, and ordinary/maximized-only autohide still waits for button release before starting to hide. These checks do not synthesize mouse input or perform a real window drag.

An additional native check positions the hidden dock beside the existing off-desktop fixture and toggles the fixture's visibility to simulate intersection returning between a pointer tick and the reveal callback. The callback must keep the dock hidden until that intersection clears, including in Proximity mode.

If stale WPF intermediates cause missing `*.g.cs` errors after switching build configurations, regenerate them with `dotnet build tests/ToolDock.RegressionTests -c Debug -t:Rebuild`, then run the checks again.
