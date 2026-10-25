# Windock

A macOS-style dock for Windows 10, built with C# and WPF on .NET 8.

Windock replaces the Windows taskbar with a floating, blurred dock whose icons grow under the pointer, bounce while apps open, and show live window previews. It also brings a status area (tray, Wi-Fi, volume, battery, clock), a quick launcher, and an optional Alt+Tab switcher. Every feature has its own option in the Settings window, and every change applies at once.

> **Status: v0.9.0.** Used daily as a taskbar replacement on Windows 10 22H2.

## Contents

- [Features](#features)
- [Requirements](#requirements)
- [Installation](#installation)
- [Getting started](#getting-started)
- [Using the dock](#using-the-dock)
- [Settings](#settings)
- [Keyboard shortcuts](#keyboard-shortcuts)
- [If the Windows taskbar does not come back](#if-the-windows-taskbar-does-not-come-back)
- [Uninstalling](#uninstalling)
- [Building from source](#building-from-source)
- [Safety and privacy](#safety-and-privacy)
- [Project structure](#project-structure)
- [Documentation](#documentation)
- [License](#license)

## Features

**The dock**
- Floats at the bottom center of the screen, with a blurred background, rounded corners, and app name tooltips.
- Icons grow smoothly under the pointer (magnification), and bounce while an app is starting.
- Shows pinned apps and running apps, with one dot per open window (up to 3). The Recycle Bin sits at the right end.
- Hover a running app to see live previews of its windows, like the Windows taskbar. Click a preview to switch to it, or ✕ to close it.
- Drag and drop: drag programs or shortcuts from the Desktop or Explorer onto the dock to pin them, drag icons to reorder them, and drag an icon up out of the dock to remove it.
- Right-click a program or shortcut in Explorer › **Pin to Windock**.
- Auto-hide like a Mac: **Never**, **When a window covers the dock**, or **Always**. Touch the bottom edge of the screen to bring it back.
- Hides itself while a fullscreen game or video is in front.

**Replacing the taskbar**
- The Windows taskbar is hidden while Windock runs, and put back when Windock quits, crashes, or starts again after being killed.
- The Windows key still opens the Start menu.
- **Windock mode / Windows mode**: one click in Settings switches back to the normal Windows taskbar without losing any pins or settings. The last mode is remembered.

**Status area** (right end of the dock)
- **^ Tray**: a Windock popup with every tray icon. Left, right and double clicks are passed to the real icon, so the taskbar stays hidden.
- **Wi-Fi / network**, **volume** (scroll to change, middle-click to mute, click for a slider), **battery**, **input language**, **clock** with a calendar, and **notifications**.
- Every button can be turned off. The clock can be 24 or 12 hours, with the date and seconds.

**Dynamic Island** (top center of the screen)
- A black pill with the clock, the date and the battery. Point at it and it springs open with the full date and battery time.
- **Now playing** from Spotify, browsers and media players: cover art and moving bars; open it for the title, progress, and previous / play-pause / next. Click the cover to bring the player to the front.
- Short pop-ups for **volume**, **brightness** (laptop screen), **charger plugged in / out and low battery**, and **keyboard, mouse or monitor connected / disconnected**.
- Slides up out of the way when a window covers it (e.g. a maximized browser) and comes back for pop-ups or when you touch the top edge above it. Can also stay on top always.
- The clock, date and battery shown in the island leave the dock, so they are never shown twice.
- More panels in the open island: click its left or right side (or scroll) to slide to **Devices** (keyboards, mice, displays, the current audio output and Bluetooth devices, with battery when reported) and **Weather** for your city (Open-Meteo, no account; only the city name and its coordinates are sent).

**Launcher** (`Alt+Space`)
- Type an app name and press Enter to open it. It can also search files on the Desktop and in Documents.

**Alt+Tab switcher** (off by default)
- Live previews of every window, grouped by app. Keep holding Alt and type to search, press Del to close a window.
- Can be limited to the screen the pointer is on, and hands Alt+Tab back to Windows in exclusive fullscreen games.

**Attention and notifications**
- When an app flashes or sends a Windows notification (for example a WhatsApp message), its icon bounces or gets a red dot until you open it.
- Apps with unread messages show the count in the red dot (for example WhatsApp "2").

**Theme**
- Dock color (presets or `#RRGGBB`), or follow the wallpaper color. Corner radius, border, indicator color, and an accent color for menus and Settings.

**Smart Recycle Bin**
- Drop files on the Recycle Bin icon to move them to the Recycle Bin (they can still be restored). The icon shows full or empty. Right-click › **Empty Recycle Bin…** goes through Windows' own confirmation.

**Light and safe**
- About 50 MB of RAM in Task Manager, and 0% CPU while idle.
- Runs as a normal user, never asks for Administrator, does not modify system files, and never deletes your files.

## Requirements

| | |
|---|---|
| Windows | Windows 10 22H2 (build 19045), x64. Windows 11 is not tested. |
| To install from source | [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) |
| To run a published build | [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (included with the SDK) |

## Installation

1. Install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) if you do not have it yet. Check with:

   ```bash
   dotnet --version
   ```

2. Download the code, either with Git or with **Code › Download ZIP** on GitHub:

   ```bash
   git clone https://github.com/ReBioNC/Windock.git
   ```

3. From the repo folder, run the install script:

   ```bash
   powershell -ExecutionPolicy Bypass -File scripts/install.ps1 -Launch
   ```

The script builds Windock and installs it for the current user in `%LocalAppData%\Programs\Windock`. No Administrator rights are needed. It creates **Windock** and **Windock Settings** shortcuts on the Desktop and in the Start Menu. With `-Launch`, Windock starts right away and opens its Settings window.

Script options:

| Option | Effect |
|---|---|
| `-Launch` | Start Windock when the install finishes |
| `-NoDesktopShortcuts` | Only create Start Menu shortcuts |

**Updating:** pull the latest code and run the same script again. A running Windock is closed first (which restores the taskbar), then replaced. Your pins and settings are kept.

## Getting started

- On the first run, Windock pins File Explorer and common apps it finds (Chrome, Edge, Discord, Steam, Spotify), hides the Windows taskbar, and shows the dock.
- Windock does **not** start with Windows by default. Turn on **Settings › Behavior › Start with Windows** if you want it to.
- Settings and logs live in `%AppData%\Windock`.

**Opening Settings.** While the taskbar is hidden, the tray icon is hidden too. Open Settings in any of these ways:
- the **Windock Settings** shortcut on the Desktop or in the Start Menu (or start `Windock.exe` again),
- right-click an empty part of the dock › **Dock settings…**,
- the Windock icon in the tray popup (**^** in the status area).

## Using the dock

| Action | Result |
|---|---|
| Click an app that is not running | Opens it; the icon bounces until its window appears |
| Click a running app | Brings its last window to the front (restoring it if minimized) |
| Click the app whose window is active | Minimizes that window |
| Click an app with several windows | Shows previews of all its windows; click one to switch |
| Hover a running app | Live window previews with a ✕ per window |
| `Shift` + click | Opens a new window or instance |
| Right-click an app | Window list, **New window**, **Keep in dock** / **Remove from dock**, **Don't show in dock**, **Show in Explorer**, **Close all windows** |
| Right-click an empty part of the dock | **Dock settings…**, **Quit** |
| Drag a pinned icon sideways | Changes the pin order |
| Drag a pinned icon up out of the dock | Removes the pin (a running app stays in the running part) |
| Drag a running app into the pinned part | Pins it there |
| Drag a program or shortcut onto the dock | Pins it at that spot (the file itself is not moved) |
| Drop files on the Recycle Bin | Moves them to the Recycle Bin (restorable) |
| Click the Recycle Bin | Opens the Recycle Bin |

**Close all windows** sends a normal close request, so apps can still ask to save your work. Windock never force-kills apps.

## Settings

Every option applies to the dock at once and is saved automatically. There is no Save button. The **Windock mode / Windows mode** buttons at the top switch between the dock and the plain Windows taskbar.

| Page | Options |
|---|---|
| **Appearance** | Icon size, magnification and its maximum size, background opacity, blur, running indicators |
| **Theme** | Dock color (presets, hex, follow the wallpaper), corner radius, border, indicator color, accent color |
| **Status area** | Area on/off, each button, 24/12-hour clock, date, seconds, battery percentage |
| **Dynamic Island** | On/off, hide when covered or always show, clock / date / battery in the island, now playing, each kind of pop-up, pop-up duration, open on hover, size, color, Devices and Weather panels, weather city, °C/°F |
| **Behavior** | Windows taskbar mode (hide completely, auto-hide, leave visible), dock auto-hide, bounce while opening, start with Windows, icons asking for attention (bounce / red dot / off, once / until opened), Windows notifications, unread counts |
| **Dock items** | Pin list (drag to reorder, add an `.exe` or `.lnk`, remove, restore defaults), hidden apps, Recycle Bin options, the Explorer **Pin to Windock** menu |
| **Launcher** | On/off, shortcut, number of results, also search files |
| **Alt+Tab** | Replace Windows' Alt+Tab, grouping, card size, live previews, current screen only, Windows Alt+Tab in fullscreen games |
| **Safety** | Emergency shortcut, **Restore the taskbar now**, **Open data folder** |
| **About** | Version, GitHub link, settings and log file locations |

**Reset to defaults** asks first, and never removes your pins.

## Keyboard shortcuts

| Shortcut | Action | Change it in |
|---|---|---|
| `Ctrl+Alt+Shift+W` | Emergency: quit Windock and bring the taskbar back | Safety |
| `Alt+Space` | Open the launcher | Launcher |
| `Alt+Tab` / `Alt+Shift+Tab` | Windock's window switcher (when turned on) | Alt+Tab |

## If the Windows taskbar does not come back

Windock restores the taskbar when it quits, when it crashes, and when it starts again after being killed. If the taskbar still does not show:

1. Press `Ctrl+Alt+Shift+W` (Windock's emergency shortcut).
2. If it is still missing, open Task Manager (`Ctrl+Shift+Esc`), find **Windows Explorer**, and click **Restart**.
3. If the taskbar still hides itself, open **Settings › Personalization › Taskbar** and turn off **Automatically hide the taskbar in desktop mode**.

If something else goes wrong, the log is in `%AppData%\Windock\logs\windock-<date>.log`.

## Uninstalling

```bash
powershell -ExecutionPolicy Bypass -File scripts/uninstall.ps1
```

This closes Windock (restoring the taskbar), removes the program, its shortcuts, the Explorer **Pin to Windock** menu and the startup entry. Your settings in `%AppData%\Windock` are kept; add `-RemoveSettings` to remove them too.

## Building from source

Run from source:

```bash
dotnet run --project src/Windock
```

Run the tests:

```bash
dotnet test
```

Build a single-file exe (needs the .NET 8 Desktop Runtime to run):

```bash
dotnet publish src/Windock -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o artifacts/windock
```

## Safety and privacy

- Runs as a normal user (`asInvoker`); never asks for Administrator.
- Writes files only in `%LocalAppData%\Programs\Windock` (the program) and `%AppData%\Windock` (settings and logs).
- Registry writes only under `HKEY_CURRENT_USER`: the startup entry (only when turned on) and the Explorer **Pin to Windock** menu (can be turned off, removed by the uninstaller).
- The taskbar's auto-hide setting is changed only while the dock hides the taskbar, recorded first, and always restored.
- No DLL injection into other apps. Apps are closed with a normal close request, never killed.
- Files dropped on the Recycle Bin can always be restored. Windock never deletes files permanently.
- Notification badges read Windows' notification database read-only, and only the app, type, time and badge count — never the notification text.
- No network connections.

## Project structure

```
src/Windock.Core   pure logic (settings, dock model, layout, animation, policies) — unit tested
src/Windock        the WPF app (dock windows, Settings, tray, launcher, switcher; Win32 calls in Interop/)
tests/             xUnit tests for Windock.Core
scripts/           install.ps1, uninstall.ps1
spikes/            phase 0 experiments, not shipped
docs/              product, design and architecture documents
```

## Documentation

| Document | Content |
|---|---|
| [docs/PRD.md](docs/PRD.md) | Goals, requirements, risks, roadmap |
| [docs/DESIGN.md](docs/DESIGN.md) | Visual spec, animations, interactions, defaults, the Settings window |
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | Components, data flow, taskbar and AppBar, error handling, testing |
| [docs/superpowers/specs/2026-10-18-windock-v0.6-design.md](docs/superpowers/specs/2026-10-18-windock-v0.6-design.md) | v0.6 feature design |
| [docs/spikes/](docs/spikes/) | Technical experiments (blur, hiding the taskbar) |
| [docs/testing/v0.1-manual-checklist.md](docs/testing/v0.1-manual-checklist.md) | v0.1 manual test checklist |

## License

[MIT](LICENSE) © 2026 Cristian Dion
