# Windock — Product Requirements Document (PRD)

| | |
|---|---|
| Document version | 1.0 |
| Date | 2026-10-16 |
| Status | Draft, awaiting review |
| Scope | Windock v1.0 (Dock) |
| Related documents | [DESIGN.md](DESIGN.md), [ARCHITECTURE.md](ARCHITECTURE.md) |

## 1. Background

The user runs Windows 10 (22H2, build 19045) and wants macOS-like looks and animations. The existing options do not fit well:

- **Windhawk:** most of its mods target Windows 11.
- **Third-party docks (MyDockFinder, Nexus Dock):** paid or closed source, and not as configurable as wanted.
- **Theme patchers:** they modify system files, which risks breaking Windows.

Windock is a home-made app that brings a macOS-like experience to Windows 10 safely. It does not modify system files and does not need Administrator access. Every behavior can be configured from inside the app. The project also serves as a GitHub portfolio piece.

## 2. Goals

1. A macOS-style dock that replaces the Windows taskbar for everyday use.
2. Feels like a Mac: icons grow under the pointer, icons bounce while an app opens, blur, rounded corners.
3. Lightweight: unnoticeable on a laptop, CPU near 0% when idle.
4. Safe: it cannot damage Windows or the user's files, and there is always a way back to the normal look.
5. Fully customizable: every behavior and visual of the dock is set from the Settings window, and changes show immediately.

## 3. Non-goals for v1.0

These parts are deliberately postponed to later phases (see section 9):

- A menu bar at the top of the screen
- Launchpad and Spotlight
- Window animations (Scale or Genie when opening or minimizing)
- Multi-monitor support. v1.0 uses the primary monitor only.
- Pinning Microsoft Store (UWP) apps. Running UWP apps are still shown, but pinning them is not supported yet.
- Dragging files from Explorer onto the dock, and dragging icons out of the dock to remove them
- Folders or stacks in the dock, and notification badges
- Replacing the red-yellow-green window buttons, the system font, or other apps' menus

## 4. Users

**Primary user:** the project owner, on a gaming laptop (Ryzen 9 5900HX, 16 GB RAM, RTX 3060) with Windows 10, who mostly uses Chrome, Discord, Steam and fullscreen games, and is comfortable with C#.

**Secondary users:** GitHub visitors who want to try a macOS-style dock on Windows 10.

## 5. Functional requirements

Priority: **P0** = must be in v1.0, **P1** = should be in v1.0 but may land at the end of the phase.

### 5.1 Dock

| ID | Requirement | Priority |
|---|---|---|
| F-01 | The dock floats at the bottom center of the primary monitor, with rounded corners and a semi-transparent background | P0 |
| F-02 | The dock shows pinned apps (fixed order) followed by running apps that are not pinned, with a separator between them. The Recycle Bin icon is always at the right end | P0 |
| F-03 | Running apps are marked with a small dot under their icon | P0 |
| F-04 | Clicking the icon of an app that is not running starts it | P0 |
| F-05 | Clicking the icon of a running app that is not active brings up and activates its last window, restoring it if it is minimized | P0 |
| F-06 | Clicking the icon of an app whose window is active minimizes that window (the Windows behavior, which users know better) | P0 |
| F-07 | Several windows of one app are grouped under one icon. If the app has more than one window and one of them is active, a click moves to the next window instead of minimizing | P1 |
| F-08 | Right-clicking an icon opens a menu: New window, Keep in dock / Remove from dock, Show in Explorer, Close all windows | P0 |
| F-09 | Icons grow smoothly following the pointer (magnification) | P0 |
| F-10 | An icon bounces while its app is starting, until its window appears (10 seconds at most) | P1 |
| F-11 | Pinned icons can be reordered by dragging inside the dock | P1 |
| F-12 | Clicking the Recycle Bin opens it in Explorer. Windock never deletes or empties anything | P0 |
| F-13 | Maximized windows do not cover the dock, because screen space is reserved | P0 |
| F-14 | The dock hides automatically while a fullscreen app (game, video) is on the primary monitor | P0 |
| F-15 | The dock stays above normal windows but does not appear in Alt+Tab | P0 |

### 5.2 Windows taskbar

| ID | Requirement | Priority |
|---|---|---|
| T-01 | "Hide completely" mode (default): the Windows taskbar is hidden while Windock runs | P0 |
| T-02 | "Auto-hide" mode: uses the Windows built-in auto-hide | P1 |
| T-03 | "Leave it visible" mode: the taskbar is not touched | P0 |
| T-04 | When Windock exits, normally or by crashing, the taskbar is put back as it was | P0 |
| T-05 | If Windows Explorer restarts, Windock applies the taskbar mode and the screen space reservation again | P0 |
| T-06 | The Windows key still opens the Start menu | P0 |

### 5.3 Settings

Every option lives in the Settings window and applies immediately without a restart. Defaults are in [DESIGN.md §6](DESIGN.md#6-default-settings).

| ID | Option | Priority |
|---|---|---|
| S-01 | Dock position: bottom, left, right | P0 for bottom, P1 for left and right |
| S-02 | Icon size (slider) | P0 |
| S-03 | Magnification: on/off, and maximum size | P0 |
| S-04 | Dock background transparency, and blur mode: off or standard blur | P0 |
| S-05 | Running-app indicator dots: on/off | P0 |
| S-06 | Windows taskbar mode: hide completely, auto-hide, leave visible | P0 |
| S-07 | Dock auto-hide (appears when the pointer touches the screen edge) | P1 |
| S-08 | Bounce animation when opening apps: on/off | P1 |
| S-09 | Start with Windows | P1 |
| S-10 | Manage pins: add (choose an .exe or .lnk), remove, and reorder | P0 |
| S-11 | Emergency shortcut that turns the dock off, changeable | P0 |
| S-12 | A "Reset to defaults" button | P0 |

### 5.4 Safety and control

| ID | Requirement | Priority |
|---|---|---|
| A-01 | A system tray icon with a menu: Settings, Pause dock, Quit | P0 |
| A-02 | A global emergency shortcut (default `Ctrl+Alt+Shift+W`) quits Windock at once and gives the taskbar back | P0 |
| A-03 | Only one Windock instance can run. Starting Windock a second time opens Settings | P0 |
| A-04 | If Windock is killed, for example from Task Manager, the taskbar state is recovered the next time Windock starts. A manual way is documented in the README too | P0 |

## 6. Non-functional requirements

| ID | Requirement | Target |
|---|---|---|
| N-01 | RAM when idle | under 100 MB (target about 60 MB) |
| N-02 | CPU when idle | about 0%, no timers or polling while nothing happens |
| N-03 | Animation smoothness | 60 fps on a 60 Hz monitor, no flicker |
| N-04 | Time to appear after start | the dock shows in under 2 seconds |
| N-05 | Privileges | always runs as a normal user (`asInvoker`), never asks for Administrator |
| N-06 | Where it writes | only `%AppData%\Windock\` and `HKCU\...\Run`, the latter only when S-09 is on. The taskbar auto-hide setting may be changed temporarily too (see ARCHITECTURE §7) |
| N-07 | Deleting files | never deletes files outside `%AppData%\Windock\` |
| N-08 | DPI | sharp at 100%, 125%, 150% and 175% scaling (Per-Monitor DPI Aware v2) |
| N-09 | Platform | Windows 10 22H2 (build 19045) x64. Windows 11 is not tested in v1.0 |
| N-10 | Runtime | .NET 8 Desktop Runtime |

## 7. Success measures

1. The user uses Windock instead of the taskbar for 7 days in a row without going back to the taskbar.
2. No case of the taskbar "disappearing for good" or needing a restart to recover in normal use.
3. Task Manager numbers meet N-01 and N-02 after 8 hours of use.
4. Every option S-01 to S-12 can be changed and shows immediately without a restart.

## 8. Risks and assumptions

| Risk | Impact | Mitigation |
|---|---|---|
| Blur on Windows 10 cannot be limited to the dock's shape, because blur always covers the whole window | Looks | The blur background window is separate from the icon window (ARCHITECTURE §4). Spike S1 proves this before the dock is fully built |
| Acrylic ignores window regions (square corners) and can make dragging windows sluggish | Looks | Only standard blur is offered, and blur can be turned off (spike S1) |
| Hiding the taskbar leaves an empty strip on screen, or the taskbar comes back when the Windows key is pressed | Core function | Spike S2 tests this behavior on Windows 10 22H2 before implementation |
| Windock is killed while the taskbar is hidden | User "locked out" | Recovery when Windock starts again, restarting Explorer, and manual steps in the README |
| Antivirus or SmartScreen warns because the exe is not signed | Trust | Open source on GitHub, and no injection into other processes |
| Grouping UWP app windows (ApplicationFrameHost) | Wrong icons | Group by AppUserModelID, with a generic icon as fallback |

## 9. Roadmap

| Phase | Content | Release |
|---|---|---|
| 0. Spike | S1: blur and a click-through transparent window. S2: hiding the taskbar and reserving screen space on Windows 10 | — |
| 1. Core dock | F-01–F-06, F-08, F-09, F-12–F-15, T-01, T-03–T-06, S-02–S-06, S-10–S-12, A-01–A-04 | v0.1 |
| 2. Completion | F-07, F-10, F-11, T-02, S-01 (left and right), S-07–S-09 | v1.0 |
| 3. Menu bar | A top menu bar: active app name, clock, battery, Wi-Fi, volume | v1.1 |
| 4. Launchpad and Spotlight | A fullscreen app grid and quick search | v1.2 |
| 5. Window animations | Scale when opening or minimizing, then Genie (optional) | v2.0 |

Phases 3 to 5 will each get their own PRD and design before work starts.
