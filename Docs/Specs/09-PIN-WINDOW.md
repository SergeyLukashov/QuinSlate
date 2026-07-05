# SPEC: Pin window

> _Last updated: 2026-07-05_

## What
A toggle that keeps the panel above all other windows so the user can
read from a buffer while working in another app.

## Behaviour

- The panel has a pin button (📌) in its chrome, alongside the close/hide
  button.
- When pinned: the panel stays above all other windows at all times,
  including when focus moves elsewhere.
- When unpinned: the panel behaves like a normal window.
- The pin state persists across sessions. If the panel was pinned when
  the app was closed, it opens pinned next time.
- The button icon or style reflects the current state (pinned / unpinned).

## Implementation

Toggle between two `SetWindowPos` calls via P/Invoke:

    // Pin
    SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);

    // Unpin
    SetWindowPos(hwnd, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);

Apply the correct state immediately after the window is created on
startup (before it is shown) so there is no visible flash of incorrect
z-order.

## Settings schema (relevant field)

    {
      "IsPinned": true
    }

Property name is serialized in PascalCase (the `System.Text.Json` default). Write to
`settings.json` on every toggle. Read on startup to restore state.

## Notes

- `HWND_TOPMOST` does not prevent other topmost windows (e.g. Task
  Manager, other pinned apps) from appearing above QuinSlate. This is
  expected Windows behaviour — do not attempt to work around it.
- The pin state is independent of panel visibility. Toggling the panel
  hidden and reshowing it must restore the pinned state, not reset it.
