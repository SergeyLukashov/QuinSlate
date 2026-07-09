# SPEC: Core buffers, tray icon, hotkey, autosave

> _Last updated: 2026-07-08_

## What
The foundation of the app. Five persistent text buffers displayed in a
floating panel, a system tray icon, a global show/hide hotkey, and
automatic saving to plain text files.

## Behaviour

- On a manual launch the panel is shown. When launched automatically by the
  Windows startup task at login, the app appears only as a tray icon and no
  window is shown. (The launch kind is read via
  `AppInstance.GetActivatedEventArgs` — a `StartupTask` kind means a login launch.)
- A global hotkey (Ctrl+Shift+Q) toggles the panel visible/hidden. If the panel is visible but not the active foreground window, it brings the panel to the foreground instead of hiding it. If the window has been minimized (e.g. by "Show Desktop"/Win+D), the hotkey and tray click restore it rather than toggling — `AppWindow.Show()`/`SetForegroundWindow` alone do not un-minimize a window, so the panel is explicitly restored (`OverlappedPresenter.Restore`).
- The panel contains 5 tabs. (Originally numbered 1–5 with a distinct colour each;
  superseded by the emoji + editable-title tab design — see
  [14-TABS-REDESIGN.md](14-TABS-REDESIGN.md).)
- Each tab holds a plain multiline editor. No formatting controls. (All five buffers are
  edited in one WebView2 hosting CodeMirror 6 — one CM6 state per buffer — which preserves
  per-buffer undo across tab switches and keeps programmatic edits, e.g. the inline calc, as
  clean undo steps. See [11-INLINE-CALC.md](11-INLINE-CALC.md) and the migration spec
  [17-EDITOR-CODEMIRROR-MIGRATION.md](17-EDITOR-CODEMIRROR-MIGRATION.md). Earlier builds used a
  `RichEditBox`.)
- Text is saved automatically. There is no save button.
- On next launch all buffer content is restored to the state at last write.

## Files

Buffers persist as UTF-8 text files (with BOM):

    %AppData%\QuinSlate\buffer-1.txt
    %AppData%\QuinSlate\buffer-2.txt
    ...
    %AppData%\QuinSlate\buffer-5.txt

The directory is created on first launch if it does not exist.
A missing buffer file on startup is treated as an empty buffer — no error.

## Autosave

Write is triggered on a 300 ms debounce after the last keystroke in a
buffer. Do not write on every keystroke. On application exit, flush any
pending write synchronously before the process ends.

## Tray icon

Implemented via Win32 `Shell_NotifyIcon` (P/Invoke). WinUI 3 has no
native tray API.

- Icon must be removed with `NIM_DELETE` before the window is destroyed
  to prevent a ghost icon persisting in the taskbar until Explorer restarts.
- Left-click on the tray icon toggles the panel (same as the hotkey).

## Hotkey

Registered with `RegisterHotKey` (Win32). Hardcoded to Ctrl+Shift+Q
(`MOD_CONTROL | MOD_SHIFT`, `VK_Q`). If registration fails (conflict), log
the error. Do not crash or show a dialog.

## Panel

- Default size: 560 × 680 logical px (device-independent pixels at 96 DPI = 1 DIP).
- Minimum size: 300 × 400 logical px, enforced via `WM_GETMINMAXINFO`.
- Resizable. The user may drag the window edges to any size above the minimum.
- Font: system monospace, 13 pt.

## Out of scope

Hotkey configuration, custom colours, font settings.
