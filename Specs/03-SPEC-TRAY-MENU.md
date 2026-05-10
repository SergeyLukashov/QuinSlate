# SPEC: Tray context menu

## What
A right-click menu on the tray icon giving the user access to top-level
app controls. The only place to quit or change persistent settings.

## Menu items

    Open                     (show the panel; same as left-click or hotkey)
    ─────────────────────
    Launch on startup   [✓]  (checkbox; toggles registry run key)
    ─────────────────────
    About
    Exit

Order and separators are as above. No other items in v1.

## Behaviour

Open
- If the panel is hidden, show it and bring it to focus.
- If the panel is already visible, bring it to focus (do not hide it).

Launch on startup
- Checkbox reflects the live registry value, read at the moment the menu
  opens, not a cached setting.
- Toggling writes or removes the registry key immediately (see SPEC_STARTUP).

About
- Show a small modal or flyout with: app name, version number, and the
  active hotkeys (show/hide and capture shortcuts).
- No links, no update check, no telemetry notice.

Exit
- Flush any pending debounced buffer writes synchronously.
- Call `Shell_NotifyIcon` with `NIM_DELETE` to remove the tray icon.
- Terminate the process cleanly.

## Implementation

Built as part of the `Shell_NotifyIcon` setup in `TrayIcon.cs`. Use a
Win32 `HMENU` created with `CreatePopupMenu` and `AppendMenu`. Display
with `TrackPopupMenu` in response to `WM_RBUTTONUP`.

Set the foreground window to the tray HWND before calling
`TrackPopupMenu`, then post `WM_NULL` afterward — this is the standard
fix for the Win32 tray menu disappear-on-click bug.
