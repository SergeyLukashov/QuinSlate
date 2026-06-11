# SPEC: Buffer keyboard navigation

## What
Switch between buffers from the keyboard without using the mouse.

## Shortcuts

    Ctrl+1 through Ctrl+7   jump directly to buffer N
    Tab                     advance to the next buffer (wraps 7 → 1)
    Shift+Tab               go back to the previous buffer (wraps 1 → 7)

These shortcuts are active whenever the panel is visible, including when
a text box has focus.

## Implementation

Handle `PreviewKeyDown` on the panel host (the outermost container of
`BufferPanel.xaml`). `PreviewKeyDown` fires before the focused control
processes the key, so it intercepts even when a `TextBox` has focus.

    private void Panel_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Tab)
        {
            bool back = Window.Current.CoreWindow.GetKeyState(VirtualKey.Shift)
                        .HasFlag(CoreVirtualKeyStates.Down);
            CycleBuffer(back ? -1 : 1);
            e.Handled = true;
            return;
        }

        bool ctrl = Window.Current.CoreWindow.GetKeyState(VirtualKey.Control)
                    .HasFlag(CoreVirtualKeyStates.Down);
        if (ctrl && e.Key >= VirtualKey.Number1 && e.Key <= VirtualKey.Number7)
        {
            int index = e.Key - VirtualKey.Number1;  // 0-based
            SelectBuffer(index);
            e.Handled = true;
        }
    }

Setting `e.Handled = true` suppresses default Tab focus traversal so the
cursor stays inside the active text box when Tab is used to switch buffers.

After switching, move keyboard focus to the text box of the selected buffer
so the user can type immediately.

## Notes

- Also handle `VirtualKey.NumberPad1` through `VirtualKey.NumberPad7` for
  the Ctrl+N shortcuts in case the user has NumLock on.
- Do not intercept Tab when the panel's non-buffer controls (pin button,
  close button) have focus — standard focus traversal should still work
  among UI chrome elements.
