# SPEC: Panel anchor and position memory

> _Last updated: 2026-07-05_

## What
The panel opens in a sensible default position on first launch and
remembers wherever the user moves it afterward.

## Default position

On first launch (no saved position), place the panel in the bottom-right
corner of the primary work area, inset 16 logical px from the right and
bottom edges:

    left = WorkArea.Right  - panelWidth  - 16
    top  = WorkArea.Bottom - panelHeight - 16

Use `SystemParameters.WorkArea` for the available area (excludes taskbar).
All position and size values are in logical pixels (DIP). Convert to physical
pixels via `GetDpiForWindow(hwnd) / 96.0f` when calling Win32 or AppWindow APIs.

## Position memory

After every move, save the new `Left` and `Top` values to `settings.json`.
Debounce the write by 500 ms — window move events fire continuously during
a drag and must not trigger a file write per event.

On subsequent launches, read the saved position from `settings.json` and
restore it before the window is shown.

## Size memory

After every resize, save the new width and height — in logical pixels — to
`settings.json`. Use the same 500 ms debounce as position. Convert the
physical pixel size reported by `AppWindow.Size` back to logical pixels:

    logicalWidth  = (int)Math.Round(appWindow.Size.Width  / scale)
    logicalHeight = (int)Math.Round(appWindow.Size.Height / scale)

On subsequent launches, read the saved size from `settings.json`, convert to
physical pixels for the current monitor's DPI, and call `AppWindow.Resize`
before the window is shown. Fall back to the default size (560 × 680) if the
saved values are missing or zero.

Minimum size is 300 × 400 logical px, enforced via `WM_GETMINMAXINFO`.

## Off-screen guard

After loading a saved position, check whether the window rect intersects
any current monitor's working area:

    bool onScreen = screens.Any(s =>
        s.WorkingArea.IntersectsWith(new Rect(savedLeft, savedTop, width, height)));

If `onScreen` is false (e.g. a second monitor has been unplugged since
the last run), reset to the default anchor position. Do not show an error.

## Settings schema (relevant fields)

    {
      "HasSavedPosition": true,
      "WindowLeft":       1420,
      "WindowTop":        820,
      "WindowWidth":      560,
      "WindowHeight":     680
    }

All values are in logical pixels; property names are serialized in PascalCase (the
`System.Text.Json` default). `HasSavedPosition` false (or absent) means use the default
anchor and size.

## Out of scope

Snapping to screen edges, multi-monitor awareness beyond the off-screen guard,
DPI-change handling while the app is running.
