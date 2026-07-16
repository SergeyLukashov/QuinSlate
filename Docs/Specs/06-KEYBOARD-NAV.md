# SPEC: Buffer keyboard navigation

> _Last updated: 2026-07-16_

## What
Switch between buffers from the keyboard without using the mouse.

## Shortcuts

    Ctrl+1 through Ctrl+5   jump directly to buffer N
    Ctrl+Tab                advance to the next buffer (wraps 5 → 1)
    Ctrl+Shift+Tab          go back to the previous buffer (wraps 1 → 5)

These shortcuts are active whenever the panel is visible, including when
the editor has focus.

**Plain Tab does not cycle buffers.** It originally did, but Tab now indents:
nesting the list item under the caret (Notion-style — see
[19-CHECKABLE-TASKS.md](19-CHECKABLE-TASKS.md) and [20-LISTS.md](20-LISTS.md))
or indenting a plain line by one two-space level, with Shift+Tab as its mirror.
So cycling lives on `Ctrl+Tab` only. Tab still never inserts a tab character and
never moves focus out of the editor. The full contract is in
[17-EDITOR-CODEMIRROR-MIGRATION.md](17-EDITOR-CODEMIRROR-MIGRATION.md).

The tab redesign (see [14-TABS-REDESIGN.md](14-TABS-REDESIGN.md)) added
`Ctrl+Tab` / `Ctrl+Shift+Tab` at the panel level and `F2` to open a tab's
edit popover.

## Implementation

The shortcuts arrive from **two** places and must behave identically; both land
in `Components/BufferKeyboardController.cs`:

- **Editor focused** — keys typed inside the WebView2 never reach XAML
  `PreviewKeyDown`, so the CM6 keymap (`WebEditor/build/src/panelShortcuts.js`,
  highest precedence) captures them and forwards them over the bridge.
- **Panel chrome focused** — `HandlePanelPreviewKey` serves the panel host's
  `PreviewKeyDown`, which fires before the focused control sees the key.

`HandlePanelPreviewKey` takes F2 first, then gates everything else behind Ctrl:

    // WinUI 3 desktop has no CoreWindow — read modifier state via
    // InputKeyboardSource, not the UWP Window.Current.CoreWindow.GetKeyState.
    bool ctrl = IsKeyDown(InputKeyboardSource
        .GetKeyStateForCurrentThread(VirtualKey.Control));
    if (!ctrl)
    {
        return;   // plain Tab is NOT a buffer shortcut — see above
    }

    if (e.Key == VirtualKey.Tab)
    {
        bool back = IsKeyDown(InputKeyboardSource
            .GetKeyStateForCurrentThread(VirtualKey.Shift));
        CycleBuffer(back ? -1 : 1);
        e.Handled = true;
        return;
    }

Selecting a buffer sets `TabView.SelectedIndex`; the panel's `SelectionChanged`
handler then activates and focuses that buffer's editor, so the user can type
immediately.

## Notes

- Also handle `VirtualKey.NumberPad1` through `VirtualKey.NumberPad5` for
  the Ctrl+N shortcuts in case the user has NumLock on.
- The Ctrl gate is what leaves plain Tab alone: standard focus traversal keeps
  working among the panel's UI chrome (pin button, close button), and inside the
  editor Tab is free to indent.
