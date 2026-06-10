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
- Show a rich About card (`AboutView`) styled with the app's dithered gradient background, hosted in its own borderless, owned top-level window (`AboutWindow`) centred over the main window. Using a dedicated window rather than an in-window `ContentDialog` keeps the card at its natural size and fully visible even when the main window has been resized smaller than the card.
- Displays the app name ("QuinSlate"), version ("v1.0.0"), and description.
- Includes a data location card that shows the dynamic local AppData path with "Open folder" and "Copy path" actions.
- Displays a visual keyboard hotkey mapping for the global toggle shortcut (`Ctrl`+`Shift`+`Q`).
- Illustrates a "Quick math" row to demo the inline evaluation feature.
- Displays link buttons to "Report an issue" (GitHub repository) and "MIT license" in the footer.

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

