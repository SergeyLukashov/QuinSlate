# SPEC: Buffer keyboard navigation

> _Last updated: 2026-07-05_

## What
Switch between buffers from the keyboard without using the mouse.

## Shortcuts

    Ctrl+1 through Ctrl+5   jump directly to buffer N
    Tab                     advance to the next buffer (wraps 5 → 1)
    Shift+Tab               go back to the previous buffer (wraps 1 → 5)

These shortcuts are active whenever the panel is visible, including when
a text box has focus.

The tab redesign (see [14-TABS-REDESIGN.md](14-TABS-REDESIGN.md)) also
adds `Ctrl+Tab` / `Ctrl+Shift+Tab` at the panel level and `F2` to open a tab's
edit popover.

## Implementation

Handle `PreviewKeyDown` on the panel host (the outermost container of
`BufferPanel.xaml`). `PreviewKeyDown` fires before the focused control
processes the key, so it intercepts even when a `TextBox` has focus.

    // WinUI 3 desktop has no CoreWindow — read modifier state via
    // InputKeyboardSource, not the UWP Window.Current.CoreWindow.GetKeyState.
    private void Panel_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Tab)
        {
            bool back = IsKeyDown(InputKeyboardSource
                .GetKeyStateForCurrentThread(VirtualKey.Shift));
            CycleBuffer(back ? -1 : 1);
            e.Handled = true;
            return;
        }

        bool ctrl = IsKeyDown(InputKeyboardSource
            .GetKeyStateForCurrentThread(VirtualKey.Control));
        if (ctrl && e.Key >= VirtualKey.Number1 && e.Key <= VirtualKey.Number5)
        {
            int index = e.Key - VirtualKey.Number1;  // 0-based
            SelectBuffer(index);
            e.Handled = true;
        }
    }

    private static bool IsKeyDown(CoreVirtualKeyStates state) =>
        (state & CoreVirtualKeyStates.Down) != 0;

Setting `e.Handled = true` suppresses default Tab focus traversal so the
cursor stays inside the active text box when Tab is used to switch buffers.

After switching, move keyboard focus to the text box of the selected buffer
so the user can type immediately.

## Notes

- Also handle `VirtualKey.NumberPad1` through `VirtualKey.NumberPad5` for
  the Ctrl+N shortcuts in case the user has NumLock on.
- Do not intercept Tab when the panel's non-buffer controls (pin button,
  close button) have focus — standard focus traversal should still work
  among UI chrome elements.
