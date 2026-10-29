# ToolDock — Technical log

Record of the current code: what the app does, how it is put together, and what sits outside this baseline. The user guide is [USER_DOCUMENTATION_EN.md](USER_DOCUMENTATION_EN.md).

ToolDock is a work in progress. This log describes the tree as it is, not a frozen design.

## Platform

- Language: C#
- UI: WPF
- Target: `net9.0-windows` (`ToolDock/ToolDock.csproj`)
- Release publish: `win-x64`, self-contained, single file, ReadyToRun, no debug symbols
- Bundle compression is off (`EnableCompressionInSingleFile=false`) so assemblies are not unpacked into private RAM on every start
- App icons: `TDicons/icon.ico` and `TDicons/trayicon.ico`

There is no installer and no solution file (`.sln`) in the repo. Build with the SDK:

```powershell
cd ToolDock
dotnet build
dotnet run
dotnet publish -c Release -r win-x64 --self-contained true
```

## Source map

```text
ToolDock/
  App.xaml.cs              lifecycle, tray, instances, arguments, resets
  MainWindow.xaml(.cs)     dock: layout, z-order, autohide, drag & drop, hover, hotkeys, launch
  SettingsWindow.xaml(.cs) settings by category
  UwpAppPickerWindow.cs    Store-app picker
  FluentChrome.cs          Segoe Fluent glyphs on buttons
  Models/
    DockConfig.cs          persistent state, ranges, factory values
    DockMetrics.cs         DIPs, cm, monitor span percent, DPI
    DockItemViewModel.cs   on-screen icon (hover scale and offset)
    PresetModels.cs        preset snapshot (schema 3)
    DockShortcutsFileExport.cs   shortcut-only export/import
  Services/
    AppPaths.cs            data folder = process directory, not the single-file temp dir
    ConfigService.cs       config.json
    DurableFile.cs         retried reads, atomic write, .bak, quarantine
    PresetService.cs       Presets_TD.json
    CustomColorService.cs  Colors_TD.json
    DockShortcutsFileService.cs
    DockPresetReset.cs     apply Default / the reference preset
    LocalizationService.cs ES/EN strings in one place
    IconService.cs         Win32 extraction (PrivateExtractIcons / SHGetFileInfo)
    UwpAppService.cs       shell:AppsFolder, AUMID, package icon
    DisplayService.cs      monitors
    DockLaunchPlacement.cs moves the new window onto the dock monitor
    NativeTrayIcon.cs      native tray icon
    DockContextMenuTheme.cs
  Interop/
    User32.cs, Shell32.cs, DisplayConfig.cs
```

## Startup

`ShutdownMode` is `OnExplicitShutdown`: closing the dock window does not exit. Exit is the tray menu, which calls `SaveNow` before `Shutdown`.

Arguments (`App.ParseStartupOptions`):

| Argument | Effect |
|---|---|
| `--instances N` or `--instances=N` | Starts N instances (1–32). Extras are passed `--spawned` |
| `--spawned` | This instance does not multiply again |
| `--open-settings` | Opens Settings after startup |
| `--reset-worker` | Internal process: waits until the instances exit, deletes or recycles the files, then starts the app again |

Instances take a local `Mutex` whose name includes a hash of the data folder. The first uses `config.json`; instance N (≥ 2) uses `config.instanceN.json`. Practical cap: 32.

The tray icon is native (`NativeTrayIcon`), not the WinForms `NotifyIcon`. Left-click opens Hotkeys. The context menu is themed with the light/dark theme.

## Configuration model

`DockConfig` implements `INotifyPropertyChanged`. The current `ConfigSchemaVersion` is **3**: canonical measurements are logical pixels (DIPs at 96). WPF scales them with the monitor DPI. Centimeters and span percentages are presentation only (`JsonIgnore`) and are recomputed in `DockMetrics`.

`DockConfig.IsLoading` stops setters from clamping while the JSON is still incomplete. `ArmStartupWidthGuard` delays width clamping for about 12 seconds when the work area may still be wrong (shell still starting, or a monitor that is off).

`DockDisabled` is not serialized. `LockShortcuts` is saved, but it is not part of `ResetBehaviour` or `ResetToDefaults`.

Ranges enforced by the model (not only by the UI):

| Field | Min | Max | Factory |
|---|---:|---:|---:|
| IconSize | 16 | 32 | 24 |
| IconSpacing | 5 | 44 | 19 |
| DockHeight | 32 | 64 | 48 |
| DockWidth | from icon size and count (calculation floor: 4) | work-area span | 990, clamped |
| DockOpacity | 0 | 100 | 60 |
| CornerRadius | 0 | 32 | 18 |
| OutlineThickness | 0.25 | 6 | 1 |
| OutlineOpacity | 0 | 100 | 20 |
| HoverMagnification | 0 | 150 | 90 |
| HoverEffectRadius | 20 | 400 | 40 |
| AutoHideProximityDistance | 1 | 400 | 60 |
| Offset | −4000 | 4000 | 0 |
| AlignmentOffset | 0 | 2000 | 0 |
| Autohide slide | 0 | 1200 ms | 150 |
| Autohide delays | 0 | 4000 ms | show 150, hide 250 |

Other factory values: top edge, centered alignment, rounded corners on all four sides, background `#1F1F1F`, white outline on all four sides, hover on, autohide off, proximity reveal, always-on-top on. English language, dark theme.

Combinations the model rejects outside of load:

- Hide-behind-taskbar requires autohide and always-on-top.
- Reserving the work area is incompatible with autohide.
- Hide-when-maximized and hide-when-intersecting require autohide.

Invalid HEX is not applied: `NormalizeColorInput` adds `#` and `IsValidHexColor` accepts `#RGB` or `#RRGGBB`. There is no error state on the text box; the previous value stays.

## Persistence

`AppPaths.DataDirectory` is the directory of `Environment.ProcessPath`. A single-file executable does not use `AppContext.BaseDirectory`, because that directory can be the temporary extraction folder and can change on every start.

`DurableFile` retries reads when the file is briefly locked (typical at sign-in). It writes to a temp file and then replaces the target. The previous configuration is kept as a `.bak` in `Backups_TD`, next to the executable. An empty or unreadable JSON is quarantined before the backup is restored, so a good `.bak` is not overwritten. If the file exists but cannot be read, `ConfigService` refuses to persist over it: a save at that moment would replace the configuration with factory defaults.

The main window saves on a 250 ms debounce, plus an explicit save on exit or before a restart.

Presets (`PresetService`): entries whose `DataSchemaVersion` is below 3 are ignored. That drops older units instead of reinterpreting them.

`config.json`, `Presets_TD.json`, and `Colors_TD.json` stay out of the repository (`.gitignore` covers `config.json`; the others appear when the app is used).

## On-screen dock

`MainWindow` is a borderless window, excluded from Alt+Tab. Position comes from the chosen edge, the monitor (`MonitorDeviceName` or the primary), the offset, and the edge gap. Left and right switch the `ItemsPanel` to vertical.

Z-order (`ApplyZOrder` / `SetDockWindowZOrder`):

- Always on top, or behind the taskbar when that option is armed.
- Do not draw over fullscreen.
- Reserve work area through the work-area API, exclusive with autohide.

Autohide: show and hide timers, edge or proximity mode, optional slide animation. A file drag from Explorer reveals the dock when it is hidden.

`HideWhenMaximized` and `HideWhenWindowIntersects` are mutually exclusive opt-in modes: selecting one clears the other in the model. Legacy configurations and presets with both enabled prefer intersection, regardless of JSON property order. With neither enabled, normal autohide applies. Window detection ignores invisible, minimized, cloaked, and shell windows. Intersection uses the visible dock frame at its resting position, even while its content is hidden or sliding, and compares screen coordinates so it also handles windows spanning displays. Without overlap, this mode keeps the dock visible even if a window is maximized.

Hover is not a `Storyboard`. On `MouseMove`, each icon gets a scale from its distance to the pointer and an offset so growth does not overlap (`UpdateFisheyeScale`). Its radius has a fixed 20–400 DIP range, independent of the 1–400 DIP autohide activation distance, including after loading settings, applying presets, or refreshing DPI metrics.

Launch: `UseShellExecute` for disk paths. UWP uses `shell:AppsFolder\<AUMID>`. When `OpenAppsOnDockMonitor` is on, `DockLaunchPlacement` watches new top-level windows (or the one that becomes foreground) for 5 seconds and moves them to the dock monitor, at most twice per window.

External drop: `.exe` and `.lnk` only, no duplicate paths. The file picker offers the same filter plus “all files”, but `TryAddDockItem` still rejects other extensions. UWP items come from `UwpAppService.GetInstalledApps` (`shell:AppsFolder` items whose path contains `!`).

Icons: `IconService` picks an extraction size from DPI and hover. `HighQualityIcons` raises that to 256 px. Resolution order is custom icon, UWP logo, the `.lnk` `IconLocation`, the shortcut target's icon, a `.ico` beside the executable, then the shell associated icon. An optional `IconPath` (`.ico`, `.png`, `.jpg`, `.exe`, `.dll`) replaces the target's icon.

Global hotkeys: three ids (`0xB001` show/hide, `0xB002` bring forward, `0xB003` disable). Modifiers use the `RegisterHotKey` flags (Alt 1, Control 2, Shift 4, Win 8).

## Settings UI that is not active

Pin buttons (`PinButton`) and the pinned-settings panel inside Hotkeys are `Visibility=Collapsed`. The pin and unpin code is kept so it can be turned back on. It is not a user-facing feature.

The settings window's Close button is hidden as well; the window close button does the same job.

## Decisions worth not reverting casually

1. **Actually portable.** Data sits next to the executable, with no Registry use and no in-app “start with Windows”. The user creates a `shell:startup` shortcut if they want one.
2. **Durable writes.** A `WriteAllText` cut off when Windows shuts down left a half-written JSON, and the next start overwrote it with factory defaults.
3. **Logical pixels as the canonical unit (schema 3).** Mixing physical pixels, centimeters, and percentages in JSON made the same file look different after a monitor change. Centimeters and percentages are derived when drawing.
4. **Do not save when the file is locked.** Showing in-memory defaults is better than destroying the JSON.
5. **Startup width guard.** The work area can be wrong for the first few seconds. Clamping the saved width then left it narrow permanently.
6. **One string service.** Spanish and English both come from `LocalizationService`. A new string needs both `switch` branches.
7. **Single-file without compression.** Bundle compression forced assemblies to be unpacked into private RAM.

## Known open items

This is not a commitment. It is what the code still leaves open:

- Pinning options to a quick-access panel is implemented and hidden.
- Invalid HEX has no visual error; the box simply does not apply the change.
- `DockLaunchPlacement` is heuristic: not every app can be moved.
- Hover has no configurable easing; scale is assigned on mouse move.
- Focused regression checks are available in `tests/ToolDock.RegressionTests`; interactive checks on real displays remain necessary for window movement and animation.

## How to extend this log

When a change affects behavior, persistence, or limits, add a dated entry. Do not rewrite the map above for a small change: the entry is enough, and the map should be updated when the structure no longer matches.

```md
## YYYY-MM-DD — Short title
- Changes:
- Files:
- Risks:
- Validation:
```

## 2026-10-19 — Documented baseline

- Changes: the markdown files at the repo root described an older build (blur, icon size 76, fixed width, `config.json` via `AppContext.BaseDirectory`, no presets, UWP, instances, or hotkeys). They were rewritten against the current code and moved to `Docs/`. A root `README.md` was added.
- Files: `README.md`, `Docs/Español/BITACORA_TECNICA.md`, `Docs/Español/DOCUMENTACION_USUARIO.md`, `Docs/English/TECHNICAL_LOG_EN.md`, `Docs/English/USER_DOCUMENTATION_EN.md`. The four markdown files at the repo root were removed. `ToolDock/README.md` is now a pointer so it does not contradict this guide.
- Risks: none at runtime. Links that pointed at the root markdown files should use `Docs/English/` or `Docs/Español/`.
- Validation: checked against `DockConfig`, `App.xaml.cs`, `SettingsWindow.xaml`, `LocalizationService`, and the persistence services. The app was not run for this documentation pass.

## 2026-10-28 — Window overlap and independent distances

- Changes: added optional hide-on-intersection alongside hide-on-maximize; lowered activation distance to 1 DIP and removed its cap on hover radius. Both settings keep their own bounds. The configuration and preset schemas stay at version 3; existing files default the new condition to off.
- Files: configuration and preset models, main/settings windows, Win32 interop, localization, preset reset, and `tests/ToolDock.RegressionTests`.
- Risks: window geometry across displays and slide animation need interactive checks; overlap must use the resting frame to avoid hide/reveal oscillation.
- Validation command from the repository root: `dotnet run --project tests/ToolDock.RegressionTests` (no external test packages).
- Follow-up: made the window conditions mutually exclusive and reordered Behaviour toggles. Regression checks cover switching from either checkbox, legacy conflicting JSON/presets, the requested row order, and maximized windows without intersection.
- Result: Debug and Release rebuilds passed with 0 warnings and 0 errors; all 8 regression groups passed, including native window filters, condition combinations, edge contact, and stable visible/hidden bounds on all four edges. Mixed-DPI hardware and visual animation remain manual checks.
