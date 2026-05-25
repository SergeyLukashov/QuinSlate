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

Built using a native WinUI 3 `MenuFlyout` displayed inside a helper borderless transparent window.
When `Show` is called on `TrayMenu`, it:
1. Obtains the current mouse cursor location using Win32 `GetCursorPos`.
2. Creates a 1x1 borderless, non-activating, transparent helper WinUI 3 `Window`.
3. Positions the helper window exactly at the cursor coordinates.
4. Renders a native WinUI 3 `MenuFlyout` populated with the items and Fluent Icons (Segoe Fluent Icons: `\xE8A7` for Open, `\xE946` for About, and `\xE711` for Exit).
5. Shows the flyout using `ShowAt` on the helper window's root grid.
6. Cleans up, closes, and disposes the helper window automatically as soon as the flyout triggers its `Closed` event (which is raised when the user either clicks a menu item or clicks anywhere else on the screen to trigger light dismiss).

