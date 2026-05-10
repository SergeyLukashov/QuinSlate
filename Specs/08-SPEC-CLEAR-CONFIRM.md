# SPEC: Clear buffer with confirmation

## What
Each buffer tab has a clear button. Clicking it requires a single inline
confirmation before wiping the content, preventing accidental data loss.

## Behaviour

1. Each buffer tab shows a small ✕ button in its header area.
2. Clicking ✕ does not clear immediately. Instead, the tab header
   switches to a confirmation state showing: `Clear?  ✓`
3. Clicking ✓ clears the buffer text, writes an empty file, and returns
   the tab header to its normal state.
4. If the user does not click ✓ within 4 seconds, the header reverts to
   normal state automatically. No clear occurs.
5. Clicking anywhere outside the confirm control also cancels it.
6. Pressing Escape while the confirm is showing cancels it.

## UI states

Normal state:

    [ 1 ]  [ ✕ ]

Confirmation state (replaces the header in-place, no modal, no flyout):

    [ Clear?  ✓ ]     (auto-cancels after 4 s)

The confirmation replaces the tab label and ✕ button inline. It must be
visually distinct — e.g. muted red background or warning-coloured text —
so the user understands it is a destructive action.

## Implementation

- Drive with a simple boolean flag per buffer (`_awaitingClearConfirm[N]`)
  and a `DispatcherTimer` with a 4-second interval.
- Starting the timer on the first ✕ click; stop and reset on ✓, Escape,
  or outside click.
- On confirm: set buffer content to empty string, trigger the debounced
  file write immediately with a zero-length file (do not delete the file).

## Notes

- Only one buffer can be in confirmation state at a time. If the user
  clicks ✕ on a second buffer while another is in confirmation state,
  cancel the first and start confirmation on the second.
- If the buffer is already empty, the ✕ button is disabled (greyed out).
  There is nothing to clear.
