# ToolDock — User documentation

Guide for the current build. ToolDock is a work in progress: bugs and rough edges may still show up.

Contents: [what it is](#what-it-is), [getting started](#getting-started), [the dock](#the-dock), [tray](#system-tray), [settings](#settings), [files](#application-files), [multiple instances](#multiple-instances), [launch on startup](#launch-on-startup), [troubleshooting](#troubleshooting), [limits](#current-limits).

## What it is

ToolDock is a secondary dock for Windows 11. It keeps shortcuts to desktop programs and Microsoft Store apps along one edge of the screen.

It does not replace the taskbar. You can leave it visible, hide it until the pointer comes close, or reserve its space so maximized windows do not cover it.

It is meant to stay portable:

- No installer.
- Settings are not written to the Windows Registry.
- The executable lives in its own folder. Copying that folder keeps settings, presets, and colors.

## Getting started

1. Put `ToolDock.exe` in an empty folder, for example `D:\Apps\ToolDock\`.
2. Run it. The dock appears, and so does a notification-area icon.
3. Right-click the tray icon and choose **Settings**.
4. Under **Size & Position**, pick an edge (top, bottom, left, or right) and a size.
5. Add shortcuts by dragging them from File Explorer, or from **App Shortcuts**.

The interface language is under **General**. The factory default is English with a dark theme.

## The dock

### Opening an app

Left-click an icon. If the path no longer exists, ToolDock reports that and does not launch it.

With **Open apps on dock's display** (Behaviour), ToolDock tries to move the new window onto the display that holds the dock. Windows picks a display when the process starts; the move happens afterwards and is not always possible, especially for Store apps.

### Adding shortcuts

Two file types are accepted:

- Programs: `.exe`
- Windows shortcuts: `.lnk`

Ways to add them:

- Drag one or more from File Explorer and drop them on the dock. If the dock is autohidden, dragging a file toward the edge reveals it.
- In Settings, **App Shortcuts** → **Add Shortcut…**
- In the same section, **Add UWP App…** lists packaged Microsoft Store apps.

The same file is not added twice. A UWP app is identified by its AppUserModelID, not by a disk path.

### Order and icons

- Drag an icon inside the dock to reorder it.
- Right-click an icon:
  - **Remove from dock**
  - **Assign custom icon…** (`.ico`, `.png`, `.jpg`, `.exe`, or `.dll`)
  - **Reset icon**, when a custom icon is already set
- Double-click or right-click an empty part of the dock for a quick menu: settings, reload, edge, icon alignment, autohide, and lock shortcuts.

**Lock Shortcuts** stops reordering. You can still launch, remove, and add items. The switch is on the empty-area menu.

**High quality icons** (App Shortcuts) extracts icons at a higher resolution. Hover looks better on some shortcuts and the app uses a little more memory.

### Hover effect

With **Enable hover effects**, nearby icons grow and shift apart so they do not overlap. Magnification runs from 0% to 150%. Radius is edited in centimeters and stored internally as logical pixels.

Hover radius is independent of the autohide activation distance. Its range is 20–400 logical pixels (about 0.53–10.58 cm), even when the activation distance is smaller.

## System tray

Left-click opens **Hotkeys**. Right-click opens the menu:

| Item | What it does |
|---|---|
| Settings | Opens the settings window |
| Set on top | Shows the dock and brings it forward |
| Disable dock | Hides it for this session only. It comes back the next time you start the app |
| Reload dock | Reloads icons, DPI, and position without exiting |
| Reload app | Saves, exits, and starts ToolDock again (settings open on startup) |
| Troubleshooting | Program folder and the three reset levels |
| Exit | Saves and quits |

With more than one instance, the tray tooltip includes the number, for example `ToolDock (2)`.

## Settings

The window has a sidebar. In **Size & Position**, **Appearance**, **App Shortcuts**, and **Behaviour**, **Reset** restores that section to the reference preset. In **General**, it restores English and the dark theme.

The reference preset is the one currently selected. If the current preset is **Custom**, ToolDock uses the last real preset you had selected (by default, **Default**).

Double-click a slider to restore that control the same way.

Along the bottom:

- **Reset preset** reloads the reference preset.
- **Reset all to default** sets English, the dark theme, and the rest of the factory values. It does not clear dock shortcuts, saved presets, or custom colors.
- **Refresh window** redraws the settings window.
- **Reload app** restarts ToolDock.

### Info

Summarizes usage and configuration files. **Launch on startup** explains how to create a Windows Startup shortcut; it is not an automatic startup switch. Info also explains how to open multiple instances.

### General

- **Language:** Spanish or English. Tray text follows the same choice.
- **Dark mode:** theme for Settings and menus. The dock's own color is chosen under Appearance.
- **Disable dock:** same as the tray item. It is not saved: the dock is active again after a restart.
- **Display:** which display the dock anchors to. **Default (primary)** follows the primary display.
- **Configuration presets:** see [Presets](#presets).

### Hotkeys

Three global hotkeys. Click the button, then press the combination (Ctrl, Alt, Shift, and the Windows key are allowed). The clear button leaves the hotkey unassigned.

| Hotkey | Effect |
|---|---|
| Show/Hide dock | Toggles visibility when autohide is off |
| Set dock on top | Same as tray **Set on top** |
| Disable dock | Toggles the session-only disabled state |

With autohide on, only the **Show/Hide dock** label is dimmed. You can still assign, change, or clear the combination, but it is not active until autohide is turned off. **Set dock on top** and **Disable dock** remain available.

**Set dock on top** shows and raises the dock once. It does not toggle visibility or enable the persistent **Always on top** option.

If another program already registered the same combination, ToolDock cannot take it.

### Size & Position

On-screen measurements are logical pixels (the same unit Windows uses when it scales a display). On a display scaled to 150%, 48 logical pixels cover more physical pixels. The **Details** page shows the conversion.

| Control | What it does | Typical range |
|---|---|---|
| Dock Position | Edge: top, bottom, left, right. Side edges switch the dock to vertical | — |
| Auto adjust dock width | Length follows the number of icons | — |
| Dock width (px) | Length along the edge | Minimum depends on icons; maximum is the work area |
| Dock height (px) | Thickness of the dock | 32–64 |
| Alignment offset (px) | Moves the dock along the edge | −1500 to 1500 on the slider |
| Offset from edge (px) | Space between the dock and the screen edge | 0–600 |

### Appearance

**Background**

- **Rounded corners** and **Corner radius (px)**, from 0–32, with a checkbox for each corner.
- **Background Color**, from a list of built-in colors.
- **Dock opacity**, from 0 (transparent) to 100 (opaque). Factory value is 60.

**Outline**

- **Show outline** turns it on or off; **Outline on edges** selects each side separately (top, bottom, left, right). Sides belong to the dock, not the screen. With the dock on top, the bottom edge is the one that faces the desktop.
- Color, opacity (factory: 20), and **Outline thickness (px)** from 0.25 to 6 logical pixels. Fractional values are antialiased; appearance depends on display scaling.

**Custom Colors**

Type a HEX color (`#RRGGBB` or `#RGB`; a missing `#` is added), give it a name, and save it. It is stored in `Colors_TD.json` and can be selected again or deleted. It works for both the background and the outline.

### App Shortcuts

- **Save Shortcuts** exports only the icon list (paths, names, custom icons, and UWP apps). It does not export colors or other settings. The suggested file name is `Shortcuts_TD.json`.
- **Load Shortcuts** replaces the current list with the file. Missing `.exe` and `.lnk` files are skipped.
- **Remove all shortcuts** clears the dock. It asks for confirmation and cannot be undone.
- Under **Buttons**, **Button alignment** places the icons: **Left**, **Center**, or **Right**; **Top**, **Middle**, or **Bottom** when vertical.
- **Icon size (px)**: 16–32. Factory: 24.
- **Icon spacing (px)**: 5–44. Factory: 19.
- **High quality icons** and **Hover effects**, described above.

### Behaviour

| Option | Effect |
|---|---|
| Always on top | The dock stays above normal windows |
| Do not show over fullscreen apps | Hides the dock while a fullscreen app has focus on the dock's display; on by default |
| Open apps on dock's display | Tries to move the launched window to the dock's display |
| Reserve space in work area | Windows treats the dock like a bar: maximized windows do not cover it. Cannot be combined with autohide |
| Autohide | The dock slides into its edge and comes back according to the chosen mode |
| Hide behind taskbar | Available only when autohide and always-on-top are both on |
| Hide when a window is maximized | With autohide on, a maximized window on the dock's display allows it to hide |
| Hide when a window intersects the dock | With autohide on, a window overlapping the dock allows it to hide, even if that window is not maximized |

Fullscreen suppression works with autohide on or off.

The two window conditions are off by default and mutually exclusive. When one is enabled, the dock stays visible unless that condition is met. Intersection is checked even while you drag a window, with the configured reveal and hide delays. Minimized or hidden windows, the desktop, and the taskbar do not trigger these conditions. With neither enabled, normal autohide applies.

**Reveal dock when...** selects how the pointer reveals the dock:

| Mode | How to reveal |
|---|---|
| Touch screen edge | Touch the outermost physical pixel of the selected display edge, within the dock's activation span, and stay there for the reveal delay. This uses the full screen edge, including when the taskbar reduces the work area |
| Cursor near screen edge | Move within the configured activation distance near the dock |

Other autohide controls:

- **Activation distance (cm)**: proximity distance, independent of hover radius. Range: about 0.03–10.58 cm (1–400 logical pixels); factory value: about 1.6 cm.
- **Animate autohide (slide)**: enables the slide animation.
- **Slide speed (ms)**: animation duration, 0–1200 ms (factory: 150). Higher values make the slide slower.
- **Reveal delay (ms)** and **Hide delay (ms)**: 0–4000 ms; factory values: 150 and 250 respectively.

The pointer can still reveal the dock when a window condition is met. Dragging files onto it, tray **Set on top**, and the **Set dock on top** hotkey also reveal it.

### Details

Read-only. For the current display and DPI it shows the effective dock, icon, and hover sizes, plus the work area and resolution.

### Troubleshooting

| Action | What it changes |
|---|---|
| Open app folder | Opens the `.exe` folder in File Explorer |
| Generate detailed log | Writes a `.txt` with configuration, displays, DPI, and dock state, for a bug report |
| Delete all custom colors | Removes `Colors_TD.json`. Built-in colors stay |
| Delete all custom presets | Removes `Presets_TD.json` and applies the Default preset |
| Reset this instance | Closes this instance, deletes its configuration and associated backup files, and opens it again with Settings. No confirmation |
| Reset all instances | Closes all instances of this executable, deletes every instance configuration in its folder and associated backups, and opens the same number of instances again. Settings stays closed. No confirmation |
| Hard reset | Closes all instances of this executable and sends every JSON file in its folder, plus `Backups_TD/` and `Temp_TD/`, to the Recycle Bin. Then opens one instance without Settings. Asks for confirmation |

Instance resets remove the affected settings and dock shortcut lists, including configurations of instances that are not currently running when resetting all instances. Saved presets, custom colors, and exported shortcut files remain. Hard reset also includes JSON files placed in the program folder by other tools; keep ToolDock in its own folder.

## Presets

A preset stores size, position, appearance, icon metrics and hover (not the shortcut list), and behaviour.

- **Default** ships with the app and cannot be deleted.
- **Custom** appears when you change something away from the loaded preset.
- To keep a setup, type a name under **Save custom as new preset** and click **Save preset**. If the name already exists, ToolDock asks before overwriting.
- **Delete preset** removes the selected preset when it is one you created.

Older presets, from before measurements were stored as logical pixels, are dropped when `Presets_TD.json` is read.

## Application files

All of them live in the same folder as `ToolDock.exe`:

| File | Purpose |
|---|---|
| `config.json` | Settings and shortcuts for the first instance |
| `config.instanceN.json` | The same for instance N, when N is 2 or higher |
| `Presets_TD.json` | Presets you saved |
| `Colors_TD.json` | Named colors |
| `Shortcuts_TD.json` | Only if you export the shortcut list; you can save it under another name |
| `Backups_TD/` | Previous copy of the configuration, saved as `.bak`. If a JSON file is cut off (for example when the PC shuts down), ToolDock tries to recover this copy |
| `Temp_TD/` | Temporary files used while saving JSON; removed when empty |
| `*.corrupt` | An unreadable file set aside so it does not overwrite the backup |

Copying that folder backs up your configuration. You do not need to copy the `.exe` if you only want the data.

`Temp_TD/` is managed automatically and is not needed in a backup.

## Multiple instances

You can run ToolDock more than once, up to 32 times. Each instance has its own settings, shortcuts, and tray icon. Saved presets and custom colors share the same files in the application folder.

To start several at once:

```text
ToolDock.exe --instances 2
```

`--instances=2` works too.

## Launch on startup

ToolDock stays portable and does not register itself to launch automatically. Use a shortcut in the Windows Startup folder to launch it when you sign in.

If you want it at sign-in:

1. Press `Win + R`, type `shell:startup`, and confirm.
2. Create a shortcut there to `ToolDock.exe`.
3. For two docks, edit the shortcut target and append: `--instances 2`.

## Current limits

- Aimed at Windows 11.
- Dropped files must be `.exe` or `.lnk`. Anything else is ignored. Store apps are added from the UWP picker.
- Visual effects and the thin outline depend on DPI and on how Windows composes the desktop.
- Moving an app to the dock's display is not guaranteed: some windows choose their own display.
- **Disable dock** lasts only until you exit.
