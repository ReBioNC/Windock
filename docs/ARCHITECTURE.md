# Windock — Technical Architecture

| | |
|---|---|
| Document version | 1.0 |
| Date | 2026-10-16 |
| Status | Draft, awaiting review |
| Related documents | [PRD.md](PRD.md), [DESIGN.md](DESIGN.md) |

## 1. Summary

Windock is a single WPF desktop app on .NET 8 that runs as a normal user. It draws the dock in a transparent window as wide as the screen, reserves screen space through the Windows AppBar API, and hides the Windows taskbar temporarily. The list of open windows comes from Windows events (`SetWinEventHook`), not polling. The core logic (dock model, layout, animation, settings) lives in a separate library that does not depend on WPF or Win32, so it can be unit-tested.

## 2. Technology

| Part | Choice | Reason |
|---|---|---|
| Language and runtime | C# 12, .NET 8 (LTS) | Already installed (SDK 8.0.400), and the user knows C# |
| UI | WPF | GPU-rendered through DirectX, supports per-pixel transparent windows, light compared to Electron |
| Win32 interop | `Microsoft.Windows.CsWin32` (source generator) and hand-written P/Invoke | Generated signatures come from official metadata, which reduces signature bugs |
| Tray icon | `System.Windows.Forms.NotifyIcon` (`UseWindowsForms`) | Part of .NET, no extra dependency |
| Serialization | `System.Text.Json` | Part of .NET |
| Tests | xUnit | Standard and light |
| CI | GitHub Actions (`windows-latest`) | Build and test on every push |

There is no third-party UI library. Every style is hand-made.

## 3. Solution layout

```
Windock/
├─ Windock.sln
├─ src/
│  ├─ Windock.Core/                  // no WPF, no Win32 — pure logic
│  │  ├─ Apps/          AppId, DockItem, DockModel, policies (click, auto-hide, attention, switcher, …)
│  │  ├─ Layout/        DockLayoutEngine, DockMetrics, LayoutResult
│  │  ├─ Animation/     DockAnimator, BounceAnimator, Tween, Easing
│  │  ├─ Launcher/      LauncherSearch
│  │  ├─ Status/        StatusFormat, StatusGlyphs
│  │  ├─ Settings/      DockSettings, SettingsStore, SettingsJson, SettingsNormalizer
│  │  └─ Abstractions/  IWindowSource, IClock, …
│  └─ Windock/                       // the WPF app
│     ├─ App.xaml(.cs)               // entry point
│     ├─ Composition/                // AppHost (wiring, lifecycle, recovery), AutoHideController, …
│     ├─ Interop/                    // the only place that calls Win32
│     ├─ Dock/          DockWindow, BackdropWindow, EdgeTriggerWindow, DockController, DockRenderer, …
│     ├─ Status/        StatusArea and its popups (volume, calendar, tray)
│     ├─ Launcher/      LauncherWindow
│     ├─ Switcher/      WindowSwitcher, SwitcherWindow (Alt+Tab)
│     ├─ Settings/      SettingsWindow and its pages
│     └─ Tray/          TrayIcon
├─ tests/
│  └─ Windock.Core.Tests/
├─ spikes/                           // phase 0 experiments, not shipped
├─ scripts/                          // install.ps1, uninstall.ps1
└─ docs/
```

Dependency rule: `Windock` may depend on `Windock.Core`, never the other way round. Code outside `Interop/` does not call Win32 directly.

## 4. Dock windows

The dock is made of three Windock windows:

| Window | Size | Content | Style |
|---|---|---|---|
| `DockWindow` | As wide as the monitor; height = plate height + edge gap + magnification room (`magnificationSize − iconSize`, or the bounce height, + 16 for the tooltip). Default 66 + 4 + 32 + 16 = 118 | Plate (color, opacity, border), icons, indicator dots, tooltip, status area | `AllowsTransparency=True`, topmost, `WS_EX_TOOLWINDOW` (not in Alt+Tab), `WS_EX_NOACTIVATE` (clicks do not steal focus) |
| `BackdropWindow` | Exactly the plate, moved and resized every frame | The blur only | **A raw Win32 window** (`CreateWindowEx`, `WS_POPUP`, not a WPF window, because resizing a WPF window every frame is too expensive). Blur through `SetWindowCompositionAttribute` (`ACCENT_ENABLE_BLURBEHIND`). Corners rounded with a rounded-rect `SetWindowRgn`. Placed right under `DockWindow` in the z-order. Tool window and no-activate. See [spike S1](spikes/S1-blur-overlay.md) |
| `EdgeTriggerWindow` | A 2 DIP strip along the screen edge | Empty (alpha 1, invisible) | Only active while the dock auto-hides, to notice the pointer touching the edge. Re-raised to the top of the z-order on every foreground change, because Windows can leave a topmost window below ordinary ones |

**Why the blur is separate:** on Windows 10 the system blur always covers the whole window rectangle. If the blur were on the screen-wide `DockWindow`, the whole bottom strip of the screen would be blurred. Separated, the blur only has the plate's shape, while growing icons can still leave the plate upwards in the transparent window. Validated in spike S1 (§13).

**Click-through:** in a WPF layered window, pixels with alpha 0 pass clicks to the window behind. Everything outside the plate and icons stays alpha 0. So that magnification does not jitter when the pointer is in the gap between growing icons, `DockWindow` draws an invisible "active zone" (alpha 1) covering the plate and the full height of the growing icons. The zone shrinks back when magnification ends.

**Focus:** because of `WS_EX_NOACTIVATE`, the active app window stays active when the dock is clicked. That is how Windock knows which window is active (needed for F-06). Windock may still call `SetForegroundWindow` because its process received the user's last input.

## 5. Layout and the render loop

The layout is computed by `DockLayoutEngine` in Core as a pure function:

```
LayoutResult Compute(LayoutInput input)
   // items (key, kind: icon / separator / fixed-width status button, slot progress 0..1),
   // icon size, magnification size and on/off, pointer position (null when outside),
   // magnify progress (0..1, for a smooth enter/leave), screen width, window height
→ plate rectangle, active-zone rectangle, item rectangles (position, size, indicator position)
```

The magnification formula is in [DESIGN.md §3](DESIGN.md#3-magnification).

`DockRenderer` (WPF) takes the `LayoutResult` and updates the position and size of elements on a `Canvas`. The position, size and region of `BackdropWindow` are updated from the plate rectangle with one `SetWindowPos` and one `SetWindowRgn`.

**Render loop:** `DockRenderer` subscribes to `CompositionTarget.Rendering` (once per frame) only while something moves, that is when one of these holds:
- the pointer is inside the active zone,
- `magnifyProgress` is changing,
- an item is bouncing, entering or leaving,
- auto-hide is sliding.

When everything is still, the handler is removed, so there is no per-frame work and CPU goes back to about 0% (N-02). Animation time uses `IClock` (based on `Stopwatch`), not frame counts, so animation speed is the same on 60 Hz and 144 Hz monitors.

## 6. Data flow

```
           Win32 events                                   settings.json
 (SetWinEventHook, TaskbarCreated,                             │
  ABN_*, WM_DISPLAYCHANGE, hotkey,                             ▼
  shell hook, notification store)                       SettingsStore ──(SettingsChanged)──┐
            │                                                  │                            │
            ▼                                                  │                            │
     WindowTracker ──(WindowAdded/Removed/                     │                            │
            │         ForegroundChanged/TitleChanged)          │                            │
            ▼                                                  ▼                            ▼
        DockModel  ◄───────────── pins from settings ─────────┘                 AppBarService
            │  (ItemsChanged)                                                   TaskbarModeCoordinator
            ▼                                                                   DockWindow (size)
   DockController / DockRenderer ◄── IconProvider (icons, async)
            │
            │ click / menu
            ▼
       AppLauncher ──► start app / activate / minimize / close
            │
            └──(launch)──► DockController starts that item's bounce
```

### 6.1 WindowTracker

- **Events listened to:** `SetWinEventHook` with `WINEVENT_OUTOFCONTEXT` for `EVENT_OBJECT_SHOW`, `EVENT_OBJECT_HIDE`, `EVENT_OBJECT_DESTROY`, `EVENT_SYSTEM_FOREGROUND`, `EVENT_OBJECT_NAMECHANGE`, `EVENT_SYSTEM_MINIMIZESTART`, `EVENT_SYSTEM_MINIMIZEEND`, `EVENT_OBJECT_CLOAKED`, `EVENT_OBJECT_UNCLOAKED` and `EVENT_OBJECT_LOCATIONCHANGE`.
- **At start,** the initial list is read once with `EnumWindows`.
- **A window counts as an "app window"** when all of these hold, close to the taskbar's own rules:
  - top-level and `IsWindowVisible`
  - no owner, unless it has `WS_EX_APPWINDOW`
  - no `WS_EX_TOOLWINDOW`, unless it has `WS_EX_APPWINDOW`
  - not cloaked (`DwmGetWindowAttribute(DWMWA_CLOAKED) == 0`), so hidden UWP windows and other virtual desktops are left out
  - not a child window, and not one of Windock's own untitled windows
- **Events are filtered by handle** (`idObject == OBJID_WINDOW`, `idChild == CHILDID_SELF`), so events from controls inside windows are ignored.

### 6.2 App identity (AppId)

One dock icon = one `AppId`. It is chosen in this order:

1. **Packaged apps (Store / UWP / system apps such as Settings):** the process's package identity (`GetApplicationUserModelId`), or the window's AppUserModelID. Read through `SHGetPropertyStoreForWindow` and `PKEY_AppUserModel_ID` when needed.
2. **Classic apps:** the process exe path, from `QueryFullProcessImageName` with `PROCESS_QUERY_LIMITED_INFORMATION`, normalized to a full lower-case path. A window AUMID set by a classic app (Chrome sets one) is ignored, so shortcuts still match.
3. **UWP frames:** an `ApplicationFrameHost.exe` window takes the process of its child `CoreWindow`. A frame shown before its app moves in is looked at again when shown, when activated, and a few times in the following seconds.

**Pins** are stored as `.exe`, `.lnk` or `shell:AppsFolder\<AUMID>` paths. For `.lnk`, the target and AUMID are resolved through `IShellLinkW` and `IPropertyStore`, so pins match running windows. A pinned exe that belongs to a package (an `AppxManifest.xml` in its folder) is resolved to its AUMID from the manifest, because packaged apps cannot be started by their exe.

### 6.3 DockModel

`DockModel` (Core) keeps the list of `DockItem`s in display order: pins, a separator, running apps that are not pinned (in the order they appeared, minus hidden apps), a separator, then the Recycle Bin. It updates from `IWindowSource` events and pin changes, and raises `ItemsChanged` with a diff (added, removed, updated) so the renderer can animate items entering and leaving.

Each `DockItem` holds: `AppId`, `DisplayName`, launch target, icon path, `IsPinned`, `Windows` (handles, most recently active first) and `IsActive`.

### 6.4 AppLauncher

| Action | Implementation |
|---|---|
| Open | `Process.Start` with `UseShellExecute=true` for paths or `.lnk`. For an AUMID: `explorer.exe shell:AppsFolder\<AUMID>` |
| Activate | `ShowWindow(SW_RESTORE)` when minimized, then `SetForegroundWindow` |
| Minimize | `ShowWindow(SW_MINIMIZE)` |
| Close all | `PostMessage(WM_CLOSE)` to each window. Never `TerminateProcess` |
| Show in Explorer | `explorer.exe /select,"<path>"` |
| Recycle Bin | `explorer.exe shell:RecycleBinFolder` |

Windows owned by Administrator processes may refuse some actions because of UIPI. Failures are logged and ignored, without crashing.

### 6.5 IconProvider

- **Icon source:** `IShellItemImageFactory.GetImage` with `SIIGBF_ICONONLY`, at the needed size. This works for exes, shortcuts and `shell:AppsFolder` items. Store apps use their package's unplated logo when available.
- **Thread:** icons are loaded on one STA worker thread, because the API is COM. The resulting frozen `BitmapSource` is sent to the UI thread.
- **Cache:** in memory, keyed by `(AppId or path, pixel size)`.
- **Fallback:** a generic app icon when loading fails.

## 7. Taskbar and AppBar

### 7.1 Reserving screen space (AppBarService)

Screen space is reserved by a small invisible helper window, not by `DockWindow`, because the AppBar system may move registered windows and that must not disturb the dock.

- **Register:** `ABM_NEW` with a callback message, then `ABM_QUERYPOS` and `ABM_SETPOS` for the dock's side. Thickness = plate height + edge gap (default 70). Magnification room is not reserved.
- **`ABN_FULLSCREENAPP`:** used as a cue to check whether a fullscreen app is in front (F-14).
- **`ABN_POSCHANGED`:** recompute the position.
- **Dock auto-hide on:** `ABM_REMOVE`, so windows use the whole screen like on a Mac.
- **Exit:** always `ABM_REMOVE`.

### 7.2 Hiding the taskbar (TaskbarModeCoordinator, Win32TaskbarController)

| Mode | What happens |
|---|---|
| Hide completely | 1. Save the taskbar's original state (`ABM_GETSTATE`) to `state.json`. 2. Set the taskbar to auto-hide (`ABM_SETSTATE`, `ABS_AUTOHIDE`) so it gives up its screen space. 3. **After about 700 ms** (the auto-hide transition shows the taskbar again), `ShowWindow(SW_HIDE)` on `Shell_TrayWnd` and every `Shell_SecondaryTrayWnd`, then "keep it hidden" (see the Windows key below) |
| Auto-hide | Steps 1 and 2 only |
| Leave it visible | The taskbar is not touched. The dock's space is reserved above the taskbar |

**Step 2 changes a saved Windows setting** (the same as ticking "Automatically hide the taskbar" in Settings). So it is recorded and always undone:

- **Normal exit, emergency shortcut, switching to Windows mode or changing the mode:** `ShowWindow(SW_SHOW)`, then `ABM_SETSTATE` back to the original state, then clear the marker in `state.json`.
- **Shutdown, restart or sign-out:** on `WM_QUERYENDSESSION` the taskbar is restored before Windows ends the process; if the shutdown is cancelled, `WM_ENDSESSION(false)` applies the mode again.
- **Crash with an exception:** a global handler (§9) runs the same restore before the process ends.
- **Killed** (Task Manager, power loss): `state.json` still holds the marker. When Windock starts again, the original state is restored before the mode is applied again. If the user never starts Windock again, the manual way is in the README.
- **Explorer restart:** Windock listens for the `TaskbarCreated` message (`RegisterWindowMessage`), then applies the taskbar mode and registers the AppBar again.
- **Windows key:** the Start menu opens normally ([spike S2](spikes/S2-taskbar.md)). "Keep it hidden": on `EVENT_OBJECT_SHOW` for the taskbar or on `EVENT_SYSTEM_FOREGROUND`, if the taskbar is visible and the foreground is not `Windows.UI.Core.CoreWindow` (Start/Search), the taskbar is hidden again.
- **AppBar:** `ABM_SETPOS` is only called when the rectangle from `ABM_QUERYPOS` changed, to avoid an `ABN_POSCHANGED` storm.

## 8. Settings and state

Every file lives in `%AppData%\Windock\`:

| File | Content |
|---|---|
| `settings.json` | User settings (schema below) |
| `settings.json.bak` | The last valid copy |
| `state.json` | Taskbar recovery data: `{ "taskbarModified": true, "originalAbState": 0, "pid": 1234 }` |
| `session.lock` | Present while Windock runs; left behind only by an unclean exit |
| `logs/windock-YYYYMMDD.log` | Text log |

`settings.json` schema (version 1, abridged):

```json
{
  "version": 1,
  "appearance": {
    "position": "Bottom",
    "iconSize": 48,
    "magnification": { "enabled": true, "size": 80 },
    "backgroundOpacity": 0.55,
    "blur": "Standard",
    "showRunningIndicators": true
  },
  "behavior": {
    "taskbarMode": "Hidden",
    "autoHide": "Never",
    "bounceOnLaunch": true,
    "startWithWindows": false,
    "explorerContextMenu": true,
    "mode": "Windock",
    "attention": "Bounce",
    "attentionRepeat": "UntilOpened",
    "attentionFromNotifications": true,
    "showBadgeCounts": true
  },
  "pins": [
    { "path": "C:\\Windows\\explorer.exe" },
    { "path": "C:\\ProgramData\\Microsoft\\Windows\\Start Menu\\Programs\\Google Chrome.lnk" }
  ],
  "safety": { "emergencyHotkey": "Ctrl+Alt+Shift+W" },
  "theme": { "plateColor": "#1E1E1E", "cornerRadius": 16, "showBorder": true, "indicatorColor": "#FFFFFF", "accentColor": "#2F6FE4", "followWallpaper": false },
  "status": { "enabled": true, "showTray": true, "showWifi": true, "showVolume": true, "showBattery": true, "showClock": true, "clock24Hour": true },
  "launcher": { "enabled": true, "hotkey": "Alt+Space", "maxResults": 8, "includeFiles": false },
  "switcher": { "enabled": false, "groupByApp": true, "cardSize": "Medium", "currentScreenOnly": false, "livePreviews": true, "windowsSwitcherInFullscreen": true },
  "trash": { "showRecycleBin": true, "confirmBeforeTrash": true },
  "hiddenApps": [],
  "firstRunCompleted": true
}
```

`SettingsStore` rules:
- **Every value is clamped to its valid range when read.** For example `iconSize` 32–96, and `magnification.size` at least `iconSize`.
- **Reading is tolerant per field:** a missing or wrong field falls back to its default without losing the rest.
- **Atomic writes:** write a `.tmp` file, then replace the real file. Saves are debounced by 500 ms.
- **Broken JSON:** try `.bak`. If that fails too, use the defaults, and keep the broken file as `settings.corrupt-<time>.json` for inspection.
- **`version` is for migrations** in later versions.
- **`SettingsChanged`** says which sections changed, so components only react to what concerns them. For example, changing the opacity does not re-register the AppBar.

**Start with Windows:** the `Windock` value under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`. Written only while the option is on, removed when it is turned off.

## 9. App lifecycle, threads and errors

**Start:**
1. The `Local\Windock.SingleInstance` mutex. If another instance runs, signal it through a named `EventWaitHandle` to open Settings, then exit.
2. Install the global error handlers.
3. Read `state.json`, and restore the taskbar if a previous session left something behind.
4. Read the settings.
5. Create `WindowTracker`, `DockModel`, then the dock windows.
6. Apply the AppBar and the taskbar mode.
7. Register the hotkeys, the tray icon, and the optional watchers (notifications, Alt+Tab hook).

**Threads:** most logic runs on the UI thread. `WINEVENT_OUTOFCONTEXT` hooks deliver events through the message loop of the thread that installed them, the UI thread. Extra threads: an STA worker for icons, an STA thread that builds the launcher index while the launcher is open, the notification store reader (thread pool), and — only while the Alt+Tab option is on — a thread with its own message loop for the low-level keyboard hook, so a busy UI frame never delays it past Windows' hook timeout.

**Global error handlers:** `AppDomain.UnhandledException`, `Application.DispatcherUnhandledException` and `TaskScheduler.UnobservedTaskException`. They log, then run `RestoreAll()` (taskbar, `ABM_REMOVE`, release hotkeys and hooks). Recoverable UI-thread errors, such as failing to draw one icon, are marked handled so the dock keeps running.

| Situation | Handling |
|---|---|
| An icon fails to load | Generic icon, logged |
| An app fails to start | Bounce stops, a balloon from the tray icon, logged |
| Broken `settings.json` | See §8 |
| Explorer restart | Apply again (§7.2) |
| Resolution, DPI or monitor change (`WM_DISPLAYCHANGE`, `WM_DPICHANGED`) | Recompute the window size and register the AppBar again |
| Waking from sleep (`PowerModeChanged`) | Apply the taskbar mode again |
| A shortcut is already used by another app | A warning in Settings, and the user picks another shortcut |

**Exit:** `RestoreAll()`, save pending settings, then close the windows.

## 10. Safety rules (apply to all code)

1. **Manifest `requestedExecutionLevel="asInvoker"`.** Never asks for Administrator.
2. **No DLL injection and no code running inside other processes.** Hooks used: `WINEVENT_OUTOFCONTEXT` window events, a shell hook window, and — only while the matching features are in use — global low-level keyboard (`WH_KEYBOARD_LL`, Alt+Tab option) and mouse (`WH_MOUSE_LL`, only while a dock menu is open) hooks, which run in Windock's own process. To list tray icons, Windock allocates a scratch buffer in Explorer's memory for toolbar messages to fill, reads it, and frees it right away; it never writes to Explorer's own data.
3. **No permanent deletion.** Removing a pin only changes `settings.json`. Files dropped on the Recycle Bin icon are moved to the Recycle Bin with undo (`FOF_ALLOWUNDO`, Windows asks before anything would be destroyed). Emptying the Recycle Bin only happens when the user picks it from the menu, through Windows' own confirmation.
4. **The registry is only written under `HKCU\...\Run` and `HKCU\Software\Classes\{exefile,lnkfile}\shell\WindockPin`** (the "Pin to Windock" menu, which can be turned off in Settings and is removed by `uninstall.ps1`). The taskbar setting is only changed through `ABM_SETSTATE`, always recorded in `state.json` first, and always undone.
5. **Apps are only closed with `WM_CLOSE`**, so they can still ask "save changes?".
6. **No network connections.**
7. **Read-only access to Windows' notification store** (`wpndatabase.db`): only the app id, notification type, time and badge value are read, never notification text.

## 11. Testing strategy

**Unit tests (`Windock.Core.Tests`):**
- `DockModel`: order of pins, separators and running apps; grouping windows by `AppId`; hidden apps; active state; the `ItemsChanged` diff.
- `DockLayoutEngine`: scale at distance 0, R/2 and ≥ R; plate width with and without magnification; icons anchored at the bottom line; fixed-width status buttons.
- `BounceAnimator`, `Tween`, `Easing`: values at given times with a fake `IClock`.
- `SettingsStore` / `SettingsJson`: defaults, clamping, broken JSON, `.bak`, atomic writes (in a temporary folder), debouncing, tolerant reading.
- Policies and helpers: click policy, auto-hide policy, attention tracker, notification changes, launcher search, switcher list, package identity, status texts, hotkey parsing.

Win32 components sit behind interfaces (`IWindowSource`, `ITaskbarController`, `IClock`, …), so `DockModel` and other logic are tested with fakes.

**Manual test checklist** (before each release):
- [ ] Maximize a window: it stops right above the dock (auto-hide Never) or uses the full screen (auto-hide on)
- [ ] Fullscreen game or video: the dock disappears, then comes back after leaving
- [ ] Restart Explorer from Task Manager: the dock and taskbar mode recover on their own
- [ ] End the Windock task in Task Manager, then start it again: the taskbar is restored first
- [ ] Emergency shortcut: the dock quits and the taskbar comes back
- [ ] Windows key: the Start menu opens, then the taskbar hides again after Start closes
- [ ] DPI scaling 100%, 125% and 150%: sharp icons and correct sizes
- [ ] Sleep then wake: everything still correct
- [ ] UWP apps (Settings, Calculator): shown with the right icon, joined to their pins
- [ ] An Administrator app's window: shown, and failed actions do not crash
- [ ] Task Manager after 8 hours: RAM under 100 MB, idle CPU about 0%

## 12. Build and release

- **Build:** `dotnet build`, and `dotnet test` for Core.
- **CI:** GitHub Actions on `windows-latest` runs build and tests on every push and pull request.
- **Release:** `dotnet publish src/Windock -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true` produces a single exe that needs the .NET 8 Desktop Runtime. `scripts/install.ps1` does this and installs it for the current user.
- **Versions** use SemVer (`v0.1.0`, …), and commits use Conventional Commits (`feat:`, `fix:`, `docs:`, …).

## 13. Phase 0 spikes

Spike code lives in `spikes/`. It only answers technical questions and is not used in the app directly.

| Spike | Question | Passes when |
|---|---|---|
| S1 — Blur and overlay | Can a blurred plate with a rounded-rect shape follow an animated width while the icons are in a transparent window above it? | The blur region follows the plate at 60 fps without visible lag; clicks in transparent areas reach the window behind; not in Alt+Tab; clicks do not steal focus |
| S2 — Taskbar and AppBar | Does `ABS_AUTOHIDE` plus `SW_HIDE` hide the taskbar without leaving a gap, and how does the Windows key behave? | Maximized windows fill up to the dock; the Start menu opens; an Explorer restart recovers; the taskbar's original state comes back after exiting and after being killed |

If S1 failed, the fallback was no system blur, with a semi-opaque plate drawn in `DockWindow` (Blur = Off). If S2 showed the Start menu always bringing the taskbar back, that is documented and the taskbar is hidden again through events (§7.2).

## 14. Requirements to components

| Requirement (PRD) | Main components |
|---|---|
| F-01, F-09, F-15 | DockWindow, BackdropWindow, DockLayoutEngine, DockRenderer |
| F-02, F-03, F-07 | DockModel, WindowTracker |
| F-04–F-06, F-08, F-12 | AppLauncher, DockContextMenu |
| F-10 | BounceAnimator, DockController |
| F-11 | DockController (drag), SettingsStore |
| F-13, F-14 | AppBarService, FullscreenDetector |
| T-01–T-06 | TaskbarModeCoordinator, Win32TaskbarController, AppBarService |
| S-01–S-12 | SettingsWindow, SettingsStore |
| A-01 | TrayIcon |
| A-02 | HotkeyService |
| A-03, A-04 | AppHost (lifecycle), TaskbarModeCoordinator |
