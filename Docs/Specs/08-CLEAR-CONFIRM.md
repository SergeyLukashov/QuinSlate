# SPEC: Clear buffer with confirmation

> _Last updated: 2026-07-05_

## What
Each buffer tab's context menu (right-click menu) has a compact "Clear tab" item. To prevent accidental data loss, clicking this item does not immediately clear; instead, it changes in-place to "Confirm clear" with a Fluent whole-item slide-in micro-animation. Clicking a second time completes the action.

## Behaviour

1. Right-clicking a buffer tab shows a "Clear tab" menu item.
2. The context menu has a highly compact, perfect style (`MinWidth = 150.0`) to give the labels optimal fit.
3. If the tab's content is completely empty, the "Clear tab" item is disabled (greyed out).
4. If the tab has content, clicking "Clear tab" changes the item in-place to `"Confirm clear"` (with a checkmark icon) with a gorgeous Fluent whole-item slide-in micro-animation, and natively cancels the closing of the context menu, keeping it open.
5. Clicking `"Confirm clear"` immediately wipes the active buffer's content, writes a zero-length file to disk, and disables the menu item.
6. If the user clicks anywhere else, the context menu closes normally, and the menu item automatically reverts back to `"Clear tab"`.
7. To prevent accidental double-clicks from triggering an immediate, unintended clear, a safety debounce cooldown of 500ms is enforced: clicking the item a second time within 500ms of the initial transition is ignored and keeps the menu open.

## UI states

Normal context menu item:

    Rename tab []
    Clear tab  []

After clicking "Clear tab" (the whole menu item transitions inline with a slide-in animation):

    Rename tab []
    Confirm clear []

## Implementation

- Implemented natively using WinUI 3's `MenuFlyoutItem` and `MenuFlyoutPresenterStyle`.
- Sets `MinWidth = 150.0` on the `MenuFlyoutPresenter` to give a tight, perfect width.
- Under `Click`, if the text is `"Clear tab"`, updates the text to `"Confirm clear"` and icon to checkmark (``), and plays a hardware-accelerated `Storyboard` animation on the `MenuFlyoutItem`'s `RenderTransform` (sliding the entire item X translation from `-20.0` to `0.0` with `CubicEase` over `250` milliseconds).
- Sets `preventMenuClosing = true` during state transition.
- Subscribes to `menuFlyout.Closing` and, if `preventMenuClosing` is `true`, sets `e.Cancel = true` to cancel the close action and keep the menu open.
- Under `Click`, if the text is `"Confirm clear"`, validates if the time elapsed since the transition is at least `500` milliseconds. If it is less than `500` milliseconds, the click is ignored, `preventMenuClosing` is set to `true` to keep the menu open, and the action is aborted. If the elapsed time exceeds the safety cooldown, executes `OnClearConfirmed`.
- Subscribes to `menuFlyout.Closed` to reset the item's text and icon back to `"Clear tab"` and `""`.

## Notes

- If the buffer is already empty, the "Clear tab" context menu item is disabled (greyed out).
  There is nothing to clear.
