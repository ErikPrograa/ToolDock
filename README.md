<p align="center">
  <img src="TDicons/td_icon.png" alt="" width="128">
</p>

# ToolDock

[![English](https://img.shields.io/badge/English-2F6F8F?style=for-the-badge)](README.md)
[![Español](https://img.shields.io/badge/Espa%C3%B1ol-3A3D48?style=for-the-badge)](README.es.md)

A dock launcher for Windows that complements the taskbar with its own shortcuts, placement, appearance, and behavior.

ToolDock is a C# / WPF desktop app on .NET 9, built to stay portable: one `.exe` in its own folder, no installer, and no writes to the Windows Registry. Put the executable in its own folder. Configuration files are written next to it, so the data stays separate from other files and you can back up or move that folder to another PC.

> ToolDock is still in development. Bugs and unstable behavior may remain.

## A few features

These clips are only a sample. ToolDock is highly customizable: placement, appearance, shortcuts, and behavior can all be changed from Settings.

### Drag and drop shortcuts

Drag an `.exe` or a `.lnk` from File Explorer and drop it on the dock to add it.

<img width="854" height="480" alt="DragAndDrop" src="https://github.com/user-attachments/assets/c4b7428c-6550-4be1-b752-a378c82f9cbe" />


### Dock position

Anchor the dock to the top, bottom, left, or right edge of the monitor. On the left and right it switches to a vertical layout.

<img width="854" height="480" alt="ChangePosition" src="https://github.com/user-attachments/assets/00b26908-8727-4d99-b4c1-670eaa4530fb" />


### Autohide

The dock can hide at its edge and come back when the pointer gets close. It can also stay visible until a window on that monitor is maximized or a window overlaps the dock. These modes are mutually exclusive: enabling one turns the other off.

<img width="854" height="480" alt="Autohide" src="https://github.com/user-attachments/assets/360231a9-adbf-4870-a3ab-2db91700c661" />


### Reserve work area

The dock can reserve desktop space, like the taskbar, so maximized windows stop beside it instead of covering it.

<img width="854" height="480" alt="ReserveWorkArea" src="https://github.com/user-attachments/assets/0412b0b8-deda-40b6-976c-50d6417a96b4" />


### Open apps on the dock's display

Launching a shortcut can move that window onto the monitor where the dock is.

<img width="1707" height="480" alt="ForceDisplay" src="https://github.com/user-attachments/assets/1a860beb-fd46-4db5-b66f-bed0dde7cb76" />


## What it includes

- Anchoring to all four edges, with a vertical layout on the left and right.
- Shortcuts to `.exe`, `.lnk`, and Microsoft Store (UWP) apps.
- Reordering, locking the order, custom icons, and a magnification effect on hover.
- Color, opacity, per-side outline, rounded corners, and presets.
- Autohide, always on top, work-area reservation, and hiding over fullscreen, maximized, or overlapping windows.
- Multiple instances, global hotkeys, and a Spanish / English interface.

## Requirements

- Windows 11
- To build: [.NET SDK 9](https://dotnet.microsoft.com/download) or newer
- Visual Studio 2022 or newer, optional

## Build and run

From the `ToolDock` folder:

```powershell
dotnet restore
dotnet build
dotnet run
```

Portable publish (one self-contained executable, win-x64):

```powershell
dotnet publish -c Release -r win-x64 --self-contained true
```

The executable is written to:

`ToolDock\bin\Release\net9.0-windows\win-x64\publish\ToolDock.exe`

Put that `.exe` in its own folder. Settings are created next to it.

Run the focused regression checks from the repository root:

```powershell
dotnet run --project tests/ToolDock.RegressionTests
```

## Quick start

1. Run `ToolDock.exe`.
2. Right-click the tray icon and open **Settings**.
3. Drag an `.exe` or a `.lnk` onto the dock, or add them from **App Shortcuts**.
4. Left-click an icon to open it. Right-click it to remove it or change its icon.

The full guide is in [Docs/English/USER_DOCUMENTATION_EN.md](Docs/English/USER_DOCUMENTATION_EN.md).

## Documentation

| | English | Español |
|---|---|---|
| Usage | [User documentation](Docs/English/USER_DOCUMENTATION_EN.md) | [Documentación de usuario](Docs/Español/DOCUMENTACION_USUARIO.md) |
| Technical | [Technical log](Docs/English/TECHNICAL_LOG_EN.md) | [Bitácora técnica](Docs/Español/BITACORA_TECNICA.md) |

## Files next to the executable

| File | Contents |
|---|---|
| `config.json` | Settings and shortcuts for the first instance |
| `config.instanceN.json` | Settings for instance N (N ≥ 2) |
| `Presets_TD.json` | Presets saved by the user |
| `Colors_TD.json` | Custom colors |
| `Shortcuts_TD.json` | Suggested name when exporting shortcuts only |
| `Backups_TD/` | Previous copy of `config.json` (and of `config.instanceN.json`), saved as `.bak` |

`config.json` is listed in `.gitignore`. The other files appear when you use the app and are not part of the source tree either. If a configuration file is cut off, ToolDock tries to recover the copy in `Backups_TD`.

## Starting with Windows

ToolDock has no “start with Windows” option. Turning that on from inside an app usually means writing a Registry key, creating a Task Scheduler job, installing a Windows service, or adding another startup hook. ToolDock does not do any of that. It never registers itself with Windows, so it stays portable and leaves nothing behind: move or delete its folder and the app is gone.

To launch it when you sign in, create the shortcut yourself:

1. Press `Win + R`, type `shell:startup`, and confirm.
2. Place a shortcut to `ToolDock.exe` in that folder.

You can run several instances at once. Each one keeps its own settings in the same folder: the first uses `config.json`, and each additional instance uses `config.instanceN.json`. To open more than one when Windows starts, add `--instances 2` (or `--instances=2`) to the shortcut target. Use the number of instances you want.

## License

ToolDock is released under the MIT License. See [LICENSE](LICENSE).
