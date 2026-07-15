# SPEC: Character-limit notice

> _Last updated: 2026-07-14_

## What

When an edit is clamped at the buffer character cap (`AppConstants.MaxBufferLength`, 1,000,000
counted in CRLF form), the panel tells the user — once, calmly — instead of dropping the text in
silence.

Before this, the clamp was invisible: typing simply stopped appearing, and an oversized paste landed
half-in with no explanation. The cap itself is unchanged; only the silence is fixed.

## Behaviour

- **Truncate and notify.** The clamp keeps its existing behaviour — an edit that would cross the cap
  is truncated to whatever budget remains, so a big paste fills the tab to exactly the cap and the
  rest is dropped. The notice says so; the paste is not rejected wholesale.
- **The notice** is a small pill — a caution glyph and a few words — overlaid on the bottom-right
  corner of the editor area. It slides up 8px and fades in, holds, then slides down and fades out
  3 s after the last clamp; the slide-plus-fade matches the tab-edit flyout's `EntranceThemeTransition`
  so the notice enters and leaves like the app's other popups. It is never interactive: it cannot
  take focus or swallow a click, because the user is typing when it appears. Its chrome is shared
  with the rest of the app — the transient-surface background of the panel's `ToolTip` style, the
  same `DividerStrokeColorDefaultBrush` the peek window draws its separators with, a 4px corner
  radius, and the standard control font — so it follows the theme in both light and dark. A
  hand-rolled Composition drop shadow lifts it off the editor.
- It is deliberately **not** an `InfoBar`: InfoBar's chrome (icon column, title/message stack, ~48px
  floor) cannot be brought down to this size or onto the app's palette without gutting its template.
  The shadow is likewise **not** a `ThemeShadow`: at this SDK target ThemeShadow projects onto XAML
  receiver surfaces, and the only thing behind the pill is the WebView2's separate airspace, so it
  would draw nothing — the notice casts its own Composition `DropShadow` instead.
- **One short message** — "Slate is full. Text limit reached." The exact cap is not
  quoted; the number is noise to a user who just wants to know why their text stopped. The cause
  (typing vs paste) still reaches the log, but the notice itself does not vary on it.
- **Throttled.** A buffer sitting at the cap clamps *every* keystroke, so the notice is admitted on
  the first clamp and then suppressed for 5 s (leading-edge — the first blocked keystroke is
  acknowledged immediately, not after a delay). Suppressed clamps still hold the visible notice on
  screen, so it does not expire mid-burst while a key is held down.
- **Reset on tab switch and on panel hide.** The notice is about the tab that was full; it does not
  follow the user to another tab, and it is never the first thing a re-summoned panel shows. Both
  resets also re-arm the throttle, so the next clamp is answered at once.
- **Silent for host-origin clamps.** Buffers loaded from disk (`init`) or rewritten by the host
  (`setText`) are not a user action and never raise the notice.

## Where it lives

Detection is page-side, notification is host-side.

| Piece | Where |
|---|---|
| Detection (the one choke point) | `capFilter` in `QuinSlate.Ui/WebEditor/build/src/main.js` |
| Bridge message `limitReached` (index, cause, dropped count — **no text**) | page → host |
| `LimitReached` event / `EditorLimitEventArgs` | `QuinSlate.Ui/Components/EditorHost.cs` |
| Throttle (leading-edge, clock-injected, unit-tested) | `QuinSlate.Ui/Services/LimitNoticeThrottle.cs` |
| Whether to show it (throttle + logging) | `QuinSlate.Ui/Components/LimitNotice.cs` |
| The pill itself (chrome, fade, hold, auto-dismiss) | `QuinSlate.Ui/Components/LimitNoticeView.xaml`(`.cs`) |

The panel only translates the bridge's cause string into a `LimitNoticeCause` and calls
`LimitNotice.Report(...)`; it owns none of the notice's behaviour.

Every route into the document is a CodeMirror transaction, so `capFilter` sees all of them — typing,
IME commits, dictation, the Windows emoji panel (Win+. — OS text input, so it lands in the page as an
ordinary transaction), browser paste, drag-drop, and the host's `insert` message (the context-menu
paste). There is deliberately no second detection point.

The InfoBar's `IsOpen` is set once and never toggled: InfoBar drives its own open/close transition
from `IsOpen`, which would fight the host Border's opacity fade. Showing and hiding is done by fading
that Border.

## Logging

One Serilog line per admitted notice (buffer index, the cap, the dropped count, the cause).
Suppressed clamps are not logged — at typing speed they would flood the log. No buffer text is
logged, in keeping with the bridge rule in
[../Wiki/06-WEB-EDITOR-BUNDLE.md](../Wiki/06-WEB-EDITOR-BUNDLE.md).

## Out of scope

- Raising, lowering, or making the cap configurable.
- A live character counter or an "approaching the limit" warning.
- The capture hotkey (Spec 10), which appends through `BufferService` rather than the editor and is
  not yet implemented; if built, it needs its own notice path (a tray balloon — the panel is not
  open).
