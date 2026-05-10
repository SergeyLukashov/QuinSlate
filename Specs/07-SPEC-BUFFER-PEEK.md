# SPEC: Buffer peek

## What
Hovering the tray icon shows a preview of the first line of each buffer
so the user can find content without opening the panel.

## Display format

    1 · Buy milk and eggs
    2 · TODO: refactor auth module
    3 · (empty)
    4 · Meeting notes 14 May
    5 · (empty)
    6 · (empty)
    7 · ssh -i ~/.ssh/id_rsa user@ho…

Rules:
- One line per buffer, always all 7, in order.
- Prefix each line with the buffer number and a middle dot ( · ).
- Show only the first line of the buffer content (up to the first `\n`).
- Truncate at 48 characters and append `…` if the first line is longer.
- If the buffer is empty, show `(empty)` in a muted style if possible,
  plain text otherwise.

## Implementation

Do not use `NIF_TIP` (the built-in Shell_NotifyIcon tooltip). It has a
128-character limit and no line-break support.

Instead, create a borderless, non-interactive tooltip window that appears
on `NIN_BALLOONSHOW` / `WM_MOUSEMOVE` over the tray icon area and
disappears on `WM_MOUSELEAVE`.

The window reads the current in-memory buffer state (not from disk) so
the preview is always up to date with unsaved changes.

## Positioning

Display the tooltip window immediately above the tray icon. Query the
icon rect via `Shell_NotifyIconGetRect` and position the window so its
bottom edge sits 8 px above the icon's top edge, horizontally centred
on the icon.

If the tooltip would extend off the top of the screen (taskbar at top),
flip it to appear below the icon instead.

## Updates

Rebuild the tooltip content each time the hover window is shown. No
need to push updates while the tooltip is already visible.
