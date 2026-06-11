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

- One line per buffer (matching the active tabs, currently 5 in total).
- Formatted as `[emoji] [title]` for the label and the first line of buffer content for the preview.
- Show only the first line of the buffer content (up to the first `\n`).
- Visual truncation is handled by the UI TextBlock (`TextTrimming="CharacterEllipsis"`), so no hardcoded text-level character limit is enforced in code.
- If the buffer is empty, show `(empty)` in a muted style if possible,
  plain text otherwise.

## Implementation

The preview itself is never rendered with `NIF_TIP` (the built-in
Shell_NotifyIcon tooltip) — it has a 128-character limit and no
line-break support. It is a borderless, non-interactive window that
appears on the first `WM_MOUSEMOVE` tray callback of a hover and
disappears once a cursor-position poll detects the pointer has left
the icon.

The icon nevertheless keeps a standard `NIF_TIP` tooltip ("QuinSlate",
`NIF_SHOWTIP`) registered while the pointer is away, and withdraws it
via `NIM_MODIFY` at hover begin, re-sending the withdrawal on every
poll tick while the pointer stays on the icon (re-arming ~1.5 s after
the pointer leaves). Statically suppressing the tooltip triggers a
Windows 11 explorer bug — after dismissing a system flyout (Quick
Settings, calendar), hovering a suppressed-tooltip icon shows an empty
tooltip box. The hover-time withdrawal cancels explorer's pending
tooltip in both the normal and the glitched state; the repeat is
required because after some flyout interactions (e.g. dismissing Quick
Settings by clicking the taskbar after a recent hover) a single
withdrawal at hover begin is not honoured.
`NIN_POPUPOPEN`/`NIN_POPUPCLOSE` cannot be used as hover triggers:
explorer does not send `NIN_POPUPOPEN` while `NIF_SHOWTIP` is armed.

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
