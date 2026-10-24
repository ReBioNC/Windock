# Spike S2 — Hiding the taskbar and the AppBar

Date: 2026-10-16 · Machine: Windows 10 22H2 (19045), 1920×1080, 100% scaling, original taskbar: bottom, not auto-hiding (`ABM_GETSTATE = 0`)
Code: `spikes/S2.Taskbar` (`--scenario=N`, `--restore`, `--fullscreen`) · Automated probe: PowerShell script (taskbar state, work area, maximized Notepad, Windows key via `keybd_event`, forced kill, screenshots)

## Answers to the five questions

| # | Question | Result |
|---|---|---|
| 1 | Hide + reserve 70, then maximize: any gap or overlap? | **No.** Work area `(0,0)-(1920,1010)`; maximized Notepad stops at 1010 (+8 px of standard frame shadow) |
| 2 | Windows key: does Start open? Does the taskbar come back with it? | Start **opens normally**. With the final sequence (below) the taskbar stays hidden; only the "Type here to search" box shows while Start is open. Nothing is left behind after Start closes |
| 3 | Explorer restart: is `TaskbarCreated` received? | **Not tested** — restarting Explorer closes the user's File Explorer windows. `TaskbarCreated` behavior is documented; left to the user's final checklist |
| 4 | Forced kill, then `--restore`: does the original setting come back? | **Yes.** After the kill: the taskbar stays hidden, `state=1`, work area still 1010 (the dead process's AppBar was not released yet). After `--restore`: `state=0`, taskbar visible, work area back to 1040 |
| 5 | `ABN_FULLSCREENAPP` while a fullscreen window is up? | **Yes:** `lParam=1` when a fullscreen window appears, `lParam=0` when it closes |

## Findings

1. **`SW_HIDE` right after `ABM_SETSTATE(ABS_AUTOHIDE)` is undone by Windows.** The auto-hide transition shows `Shell_TrayWnd` again (a 2 px line at the bottom edge, which shows the taskbar when the pointer goes down — clashing with the dock). Hiding **about 700 ms after** `ABM_SETSTATE` keeps it hidden.
2. **When the taskbar becomes visible again,** hide it again if the foreground window is not Start/Search (`Windows.UI.Core.CoreWindow`) and not the taskbar itself. With this rule Start stays usable.
3. **`ABN_POSCHANGED` storm:** calling `ABM_SETPOS` on every `ABN_POSCHANGED` triggers about 20 notifications in a row. `ABM_SETPOS` is only called when the rectangle from `ABM_QUERYPOS` changed.
4. A hidden `HwndSource` window (`WS_POPUP`, `WS_EX_TOOLWINDOW`, never shown) can own the AppBar and receive its callbacks.
5. A process that dies without `ABM_REMOVE` leaves its reserved space until the next `ABM_SETSTATE` or an Explorer restart. Recovery at start (Tasks 8/16) handles it.

## Final sequence (used by Tasks 15–16)

1. `RecoveryStateStore.Write(original)` → `ABM_SETSTATE(original | ABS_AUTOHIDE)`.
2. `AppBarHost` `ABM_NEW` + `ABM_QUERYPOS`/`ABM_SETPOS` (only when the rectangle changed).
3. After 700 ms: `ShowWindow(SW_HIDE)` for `Shell_TrayWnd` and every `Shell_SecondaryTrayWnd`, then turn on "keep it hidden".
4. Keep it hidden: on `EVENT_OBJECT_SHOW` for the taskbar or `EVENT_SYSTEM_FOREGROUND`, if the taskbar is visible and the foreground is not `Windows.UI.Core.CoreWindow`/`Shell_TrayWnd` → `SW_HIDE`.
5. Restore: turn off "keep it hidden", `SW_SHOW`, `ABM_SETSTATE(original)`, `ABM_REMOVE`, clear the state.

The 700 ms delay and the "keep it hidden" logic live in `Win32TaskbarController` (App), so `TaskbarModeCoordinator` in Core does not change.
