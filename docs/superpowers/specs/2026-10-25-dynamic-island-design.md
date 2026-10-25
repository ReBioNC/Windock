# Dynamic Island — design (v0.10)

Agreed with the user on 2026-10-25 (conversation in Indonesian; mockups shown in chat).

## Goal

An iPhone-style "Dynamic Island" at the top centre of the primary screen: a black pill that shows the time and
live activity, grows with a spring animation when the pointer is over it, and pops up briefly for system events.
It can take over the clock, date and battery from the dock's status area.

Out of scope for this version: Windows notifications (need package identity), hiding Windows' own volume OSD,
battery levels of Bluetooth devices, a list of connected devices.

## What it shows

| State | Compact pill | Expanded (hover) |
|---|---|---|
| Idle | clock · short date · battery glyph (each only if the island owns it) | big clock, long date, battery % and time left / charging |
| Media playing | cover art left, animated bars right (pause glyph when paused) | cover, title, artist · app, progress, previous / play-pause / next |
| Event (2 s default) | volume or brightness bar with value; "Charging" / "On battery" / "Battery low"; "Keyboard / Mouse / Display connected / disconnected" | — (events do not expand) |

- Click on the media card's cover or title brings the player app to the front.
- Events: volume/brightness replace each other at once; other events queue (oldest first, at most 3).

## Visibility (auto-hide, user's choice)

- Default **Hide when a window covers it**: the island slides up out of view while the window in front overlaps
  its area (maximized browser, etc.). It comes back when the pointer touches the top edge above it, when an event
  arrives, or when the desktop is in front.
- **Always show** is the alternative.
- Always hidden while a fullscreen app is in front and in Windows mode.
- The area around the pill is transparent and click-through.

## Clock, date, battery: dock or island

`IslandSettings.ShowClock/ShowDate/ShowBattery`. When the island is on and owns an item, the dock does not show it
and the Status area page hides that item's toggle (with a hint pointing at the Dynamic Island page).

## Settings (Settings > Dynamic Island)

Enabled · visibility (auto-hide / always) · show clock, date, battery in the island · media activity ·
events: volume, brightness, battery, devices · expand on hover · event duration (1–5 s) · size (S/M/L) ·
color (black / dock plate color). All applied live.

## Architecture

All inside Windock.exe (one process). New window, not a new app.

- **Core (pure, tested):** `IslandSettings`, `IslandOwnership` (who shows clock/date/battery), `IslandModel`
  (state: idle / media / event, expanded, event queue and expiry via `IDelayScheduler`),
  `IslandVisibilityPolicy`, `BatteryEvents` (power status diff → event), `DeviceEventFilter` (coalesces the burst
  of interface arrivals one device makes).
- **App:**
  - `IslandController` — wires sources to the model, the model to the window; covered / edge / fullscreen logic.
  - `IslandWindow` — layered topmost tool window; pill size animated (BackEase, ~0.4 s).
  - `MediaSource` — Windows `GlobalSystemMediaTransportControlsSessionManager` (WinRT; app TFM becomes
    `net8.0-windows10.0.19041.0`). Created only while the media activity is on.
  - Volume — reuses `StatusArea.Audio` (`AudioEndpoint`), flashes only on real level/mute changes.
  - `BrightnessSource` — `RegisterPowerSettingNotification(GUID_VIDEO_CURRENT_MONITOR_BRIGHTNESS)`; the
    first (initial) value is a baseline, not an event. Laptop panels only.
  - Battery — `SystemEvents.PowerModeChanged` + `GetSystemPowerStatus`, low-battery check each minute.
  - `DeviceWatcher` — `RegisterDeviceNotification` for keyboard, mouse and monitor interface classes; names from
    the device's bus-reported description or friendly name.

## RAM / CPU

No polling except the clock (per minute) and the progress bar (per second, only while expanded and playing).
Music bars animate at 20 fps. Media WinRT objects are released when the media activity is turned off.

## Testing

Core unit tests for settings JSON, ownership, model transitions and queue, visibility policy, battery diff and
device coalescing. Manual: hover/expand, media with Spotify/browser, volume keys, brightness keys, plug charger,
plug USB mouse/monitor, maximized window auto-hide, fullscreen video.
