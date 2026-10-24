# Spike S1 — Blur and transparent overlay

Date: 2026-10-16 · Machine: Windows 10 22H2 (19045), 1920×1080, 100% scaling, about 200 Hz display
Code: `spikes/S1.BlurOverlay` · Automated probe: PowerShell script (screenshot of the bottom strip, `WindowFromPoint`, `mouse_event` clicks, CPU via `Get-Process`)

## Results per criterion (ARCHITECTURE §13)

| Criterion | Result | Evidence |
|---|---|---|
| Clicks in transparent areas reach the window behind | **Pass** | `WindowFromPoint` 110 px above the plate → the app window behind; on the plate → the overlay |
| Clicking the plate does not steal focus | **Pass** | Notepad stays in the foreground after clicking the plate (2/2 tries). An early failure turned out to be a click that missed, onto the taskbar or a transparent area |
| Not shown in Alt+Tab | Pass by style | `WS_EX_TOOLWINDOW` is set on both windows; not checked through the Alt+Tab UI |
| Idle CPU about 0% | **Pass** | 0.000 s of CPU in the 5 s after the `Rendering` handler was removed |
| Blur region follows the plate at 60 fps without lag | **Pass with a design change** (see below) | About 198 fps, 0 frames > 8 ms, at most 2.8 ms of work per frame |
| 125% DPI scaling | **Not tested** | Changing display scaling is a system setting; left to the user's final test |

## Findings

1. **The system blur ignores `SetWindowRgn` when the window does not change size.** A 640-wide backdrop with a rounded-rect region still blurs the full 640 px (the region only clips content, not the DWM effect). This holds for layered and non-layered WPF windows alike.
2. **The blur follows the window size.** Moving and resizing the backdrop window to the plate rectangle every frame (`SetWindowPos`) gives an exact blur, and with a rounded-rect region its corners are rounded too (`ACCENT_ENABLE_BLURBEHIND = 3`).
3. **Resizing a WPF window every frame is too expensive:** 676 of 916 frames took > 8 ms, with a 245 ms peak, because WPF lays out and renders that window again on every `WM_SIZE`.
4. **A raw Win32 window (`CreateWindowEx`, `WS_POPUP`, no WPF) as the backdrop solves it:** 0 frames > 8 ms, same look.
5. **Acrylic (`ACCENT_ENABLE_ACRYLICBLURBEHIND = 4`) ignores the region:** square corners show outside the rounded plate. Not good enough to ship.
6. Working set of the empty WPF spike: about 116–123 MB (Task Manager's "Memory" is the private working set, usually smaller). The real numbers were checked in Task 18 against N-01.

## Decisions

- `BackdropWindow` is implemented as a **raw Win32 window** (not a WPF `Window`), moved and resized to the plate rectangle every frame with `SetWindowPos(..., hWndInsertAfter = DockWindow, SWP_NOACTIVATE)`, then a rounded-rect `SetWindowRgn` with radius 16.
- Blur modes in v0.1: **Off** and **Standard blur** only. An `Acrylic` value in `settings.json` is still read (compatibility) but treated as Standard blur, and is not offered in Settings.
- Overlay (`DockWindow`): WPF `AllowsTransparency=True` plus `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`, confirmed to work.
