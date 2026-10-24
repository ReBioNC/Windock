# Windock — UI/UX Design

| | |
|---|---|
| Document version | 1.0 |
| Date | 2026-10-16 |
| Status | Draft, awaiting review |
| Related documents | [PRD.md](PRD.md), [ARCHITECTURE.md](ARCHITECTURE.md) |

All sizes in this document are in DIP (device-independent pixels, 1 DIP = 1 px at 100% scaling). Physical sizes are multiplied by the monitor's DPI scale.

## 1. Design principles

1. **Like macOS, not a pixel copy.** Take its feel and motion, then follow Windows habits where they are more comfortable. Example F-06: clicking the icon of the active window minimizes that window.
2. **Motion first, decoration later.** Magnification and bouncing must be smooth at 60 fps. Blur and shadows may be reduced if they hurt smoothness.
3. **Idle is cheap.** No animation or work runs while the user is not interacting with the dock.
4. **Everything is configurable, the defaults are already good.** Users should not need to open Settings to get a good look.

## 2. Dock anatomy (bottom position)

```
                    ┌───────────────────────────────────────────────┐
                    │ [icon] [icon] [icon] [icon] │ [icon] [icon] │ [🗑] │   ← plate
                    │   •                    •       •                │   ← indicator dots
                    └───────────────────────────────────────────────┘
  ─────────────────────────────── bottom edge of the screen ───────────────────────────────
```

| Element | Default specification |
|---|---|
| Plate (dock background) | Height = icon size + 2 × 6 (vertical padding) + 6 (indicator space). Default 48 + 12 + 6 = **66** |
| Gap between plate and screen edge | 4 |
| Plate width | Follows the content: number of icons × (icon size + 8 spacing) + 2 × 8 horizontal padding + separator widths |
| Plate corners | Radius 16 |
| Plate color | Black `#1E1E1E` at 55% opacity over the blur. 1 DIP white border at 12% opacity |
| Blur | The system's own blur radius (see ARCHITECTURE §4) |
| Icons | Square, default size 48, drawn from a 256 px source with high-quality scaling |
| Icon spacing | 8 |
| Separator | 1 DIP vertical line, 60% of the icon height, white at 25% opacity, 8 spacing on each side |
| Indicator dots | One dot per open window (max. 3), 4 diameter circles 7 apart, white at 85% opacity, 3 DIP under the icon, centered |
| Recycle Bin icon | The system Recycle Bin icon (empty or full), always rightmost after the second separator |
| App name tooltip | Appears 12 DIP above the icon after the pointer rests for 300 ms. A small dark plate, radius 6, 12 pt text |

The left and right positions (phase 2) use the same anatomy rotated by 90°: the plate is vertically centered, the indicator dots sit on the screen-edge side, and the tooltip on the inner side.

## 3. Magnification

Icons grow by the horizontal distance from the pointer to the icon's center (`d`, in DIP).

```
R      = 2.5 × base icon size             // reach of the effect
t      = min(|d| / R, 1)
scale  = 1 + (Smax − 1) × cos²(t × π/2)   // 1 → Smax, smooth at both ends
Smax   = magnificationSize / iconSize
```

- **With the defaults** (icon 48, magnification 80): Smax ≈ 1.67, and the icon right under the pointer shows at 80 DIP.
- **Icons grow upwards**, anchored at their bottom line, so they "leave" the plate. That is why the dock window is taller than its plate (ARCHITECTURE §4).
- **The plate widens** with the total width of the grown icons and stays centered. Its height does not change.
- **Smooth in and out:** when the pointer enters or leaves the dock, the magnification amount moves from 0 to 1 (or back) over 150 ms with *ease-out cubic* easing. Icons must never jump.
- **Without magnification:** when S-03 is off, icons keep their base size and only a faint highlight (white at 10% opacity) shows behind the icon under the pointer.

## 4. Animations

| Animation | Specification |
|---|---|
| Bounce (opening an app) | The icon rises by 0.5 × icon size over 300 ms (*ease-out quad*), falls over 300 ms (*ease-in quad*), rests 100 ms, and repeats. It stops when the app's first window appears or after 10 seconds. A bounce in progress lands first instead of stopping mid-air |
| New icon enters the dock | The slot width grows from 0 to full in 200 ms while the icon fades in from opacity 0 and scale 0.6 |
| Icon leaves the dock | The reverse of entering, 200 ms |
| Clicking an icon | The icon darkens (70% brightness) while pressed |
| Dock auto-hide | Modes **Never / When a window covers it / Always**. The plate and icons slide off screen in 250 ms (*ease-in cubic*) after the pointer has left for 600 ms. They slide back in 250 ms (*ease-out cubic*) when the pointer touches the last 2 DIP at the screen edge (or a file is dragged there). While the desktop is in front (wallpaper clicked, Win+D, every window minimized) the dock stays up. In any mode other than "Never", no screen space is reserved |
| Dragging to reorder pins | The dragged icon follows the pointer at 80% opacity, and the other icons move aside within 150 ms |
| Fullscreen apps | The dock hides at once without animation, so games are not disturbed |

Every animation runs from one render loop (ARCHITECTURE §5) that is active only while something animates or the pointer is over the dock.

## 5. Interaction

| Action | Result |
|---|---|
| Left-click, app not running | Start the app, and the icon bounces (when S-08 is on) |
| Left-click, app running, not active | Activate its last window, restoring it if minimized |
| Left-click, app running with one window, active | Minimize that window |
| Left-click, app running with several windows | Previews of all its windows appear above the icon (like the Windows taskbar); click a card to switch to that window |
| Hover a running app's icon (0.7 s; the name tooltip appears at 0.3 s) | Window previews: title, live picture (DWM thumbnail), a ✕ button per window. Moving to another running icon swaps the previews at once; leaving both the icon and the previews closes them after about 350 ms. Apps that are not running show the name tooltip |
| `Shift` + left-click | Open a new window or instance |
| Right-click | Context menu (see below) |
| Drag a pinned icon to another position | The other icons move aside to open a gap; the pin order changes and is saved at once (F-11) |
| Drag a pinned icon up, out of the dock (> 80 DIP) | A "Remove from dock" label appears; releasing removes the pin with a fade. An app that is still running stays in the running-apps part |
| Drag a running (unpinned) app into the pinned part | The app is pinned at the gap |
| Drag a program (.exe) or shortcut (.lnk) from Explorer/Desktop onto the dock | A gap opens at the pointer; releasing pins it there. The original file is not moved |
| Right-click a program or shortcut in Explorer/Desktop › **Pin to Windock** | Pinned at the end of the list (option in Settings › Dock items) |
| Click the Recycle Bin | Open the Recycle Bin in Explorer |
| Right-click an empty part of the plate | Menu: Dock settings…, Quit |

**App icon context menu:**

```
  App name                 (title, not clickable)
  ─────────────────
  Window 1 title…          (list of open windows, max 10)
  Window 2 title…
  ─────────────────
  New window
  Keep in dock  /  Remove from dock
  Don't show in dock       (running apps that are not pinned)
  Show in Explorer
  ─────────────────
  Close all windows        (only while running; sends a normal close, never a kill)
```

The context menu uses a dark style that matches the dock (radius 8, background `#2A2A2A`, white text).

## 6. Default settings

| Option | Default | Range or choices |
|---|---|---|
| Dock position | Bottom | Bottom, Left, Right |
| Icon size | 48 | 32–96 |
| Magnification | On | On / Off |
| Magnification size | 80 | icon size – 128 |
| Background opacity | 55% | 0–100%, at least 30% when Blur = Off |
| Blur | Standard blur | Off, Standard blur (Acrylic is not offered: it ignores the rounded corners, see spike S1) |
| Running indicators | On | On / Off |
| Windows taskbar mode | Hide completely | Hide completely, Auto-hide, Leave it visible |
| Dock auto-hide | Never | Never / When a window covers it / Always |
| Bounce while opening | On | On / Off |
| Start with Windows | Off | On / Off |
| Emergency shortcut | `Ctrl+Alt+Shift+W` | any key combination with at least two modifiers |
| Icons asking for attention (v0.6) | Bounce, until opened | Bounce / Red dot / Off; Once / Until opened |
| Also from Windows notifications (v0.6.8) | On | On / Off |
| Unread count in the red dot (v0.7) | On | On / Off |
| Dock color (v0.6) | `#1E1E1E` | presets or `#RRGGBB`; or follow the wallpaper color (off) |
| Corner radius (v0.6) | 16 | 0–30 |
| Border (v0.6) | On | On / Off |
| Indicator color (v0.6) | `#FFFFFF` | presets or `#RRGGBB` |
| Accent color (v0.6) | `#2F6FE4` | presets or `#RRGGBB` |
| Status area (v0.6) | On, every button shown except battery percentage and seconds | each button On / Off; 24/12-hour clock, date, seconds |
| Launcher (v0.6) | On, `Alt+Space`, 8 results, no files | shortcut with at least one modifier; 5–12 results; also search Desktop/Documents files |
| Recycle Bin (v0.6) | Shown, ask before moving files | On / Off |
| Hidden apps (v0.6) | empty | list in Dock items |
| Alt+Tab switcher (v0.8) | Off (Windows' own Alt+Tab) | On / Off; grouped or flat; card size; live previews; current screen only; Windows Alt+Tab in exclusive fullscreen games |
| Initial pins | File Explorer, plus detected apps from this list: Chrome, Edge, Discord, Steam, Spotify | — |

## 7. Settings window

A normal window (movable and closable), 800 × 580, dark themed, opened from the tray icon, the dock menu, or by starting Windock again.

```
┌──────────────────────────────────────────────────────────────────┐
│  Windock Settings                                         [ — ✕ ]│
├──────────────────────────────────────────────────────────────────┤
│  [ Windock mode ] [ Windows mode ]   The dock is on and ...      │
├────────────────┬─────────────────────────────────────────────────┤
│  ▸ Appearance  │  Appearance                                     │
│    Theme       │                                                 │
│    Status area │  Icon size          ──────●──────────  48       │
│    Behavior    │  Magnification      [✓]                         │
│    Dock items  │  Maximum size       ─────────●───────  80       │
│    Launcher    │  Background opacity ───────●─────────  55%      │
│    Alt+Tab     │  Blur               ( ) Off  (•) Standard blur  │
│    Safety      │  Running indicators [✓]                         │
│    About       │                                                 │
│                │                             [ Reset to defaults ]│
└────────────────┴─────────────────────────────────────────────────┘
```

| Page | Content |
|---|---|
| Appearance | S-02, S-03, S-04, S-05 |
| Theme (v0.6) | Dock color (presets, hex, follow the wallpaper), corner radius, border, indicator color, accent color |
| Status area (v0.6) | Area on/off, each button, clock format, date, seconds, battery percentage |
| Behavior | S-06, S-07, S-08, S-09, icons asking for attention, notifications, unread counts |
| Dock items | Pin list that can be dragged to reorder, with Add (choose an .exe or .lnk) and Remove buttons. S-10. Hidden apps list, Recycle Bin options, Explorer menu |
| Launcher (v0.6) | On/off, shortcut, number of results, also search files |
| Alt+Tab (v0.8) | Replace Alt+Tab, order, card size, previews, current screen only, fullscreen games |
| Safety | S-11 (shortcut recorder box), "Restore the taskbar now" button, "Open data folder" button |
| About | Version, GitHub link, settings and log file locations |

Behavior rules:
- **Every change applies to the dock immediately** and is saved 500 ms after the last change. There is no "Save" button.
- **Sliders update the dock live** while being dragged.
- **"Reset to defaults" asks for confirmation first.** The pin list is not reset; it has its own button on the Dock items page.
- **The Windock mode / Windows mode buttons** switch between the dock and plain Windows without losing any pins or settings.

## 8. Tray icon

A monochrome Windock icon in the system tray. Left-click opens Settings. Right-click opens a menu:

```
  Settings…
  Use Windows mode     (or "Use Windock mode" — same as the mode buttons in Settings)
  ─────────────
  Quit
```

## 9. Accessibility

- **Tooltip and menu text follow the system text size.**
- **Background opacity is at least 30%** when blur is off, so icons stay readable over bright wallpapers.
- **When the Windows "Show animations" setting is off,** magnification still works, but bouncing, auto-hide and icons entering or leaving become instant.
