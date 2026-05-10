# SPEC: Panel anchor and position memory

## What
The panel opens in a sensible default position on first launch and
remembers wherever the user moves it afterward.

## Default position

On first launch (no saved position), place the panel in the bottom-right
corner of the primary work area, inset 16 px from the right and bottom
edges:

    left = WorkArea.Right  - panelWidth  - 16
    top  = WorkArea.Bottom - panelHeight - 16

Use `SystemParameters.WorkArea` for the available area (excludes taskbar).

## Position memory

After every move, save the new `Left` and `Top` values to `settings.json`.
Debounce the write by 500 ms — window move events fire continuously during
a drag and must not trigger a file write per event.

On subsequent launches, read the saved position from `settings.json` and
restore it before the window is shown.

## Off-screen guard

After loading a saved position, check whether the window rect intersects
any current monitor's working area:

    bool onScreen = screens.Any(s =>
        s.WorkingArea.IntersectsWith(new Rect(savedLeft, savedTop, width, height)));

If `onScreen` is false (e.g. a second monitor has been unplugged since
the last run), reset to the default anchor position. Do not show an error.

## Settings schema (relevant fields)

    {
      "windowLeft": 1420,
      "windowTop": 820
    }

## Out of scope

Snapping to screen edges, multi-monitor awareness beyond the off-screen
guard, remembering size (panel is fixed size in v1).
