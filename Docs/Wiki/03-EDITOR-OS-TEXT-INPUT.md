# OS text-services input into the editor — keep `autocorrect="on"`
> _Last updated: 2026-07-14_

Read this before you touch the editor's `contentAttributes`, anything about how text gets into a
buffer other than by typing, or before re-diagnosing "the emoji panel doesn't insert".

## The rule

**The editor's contenteditable must keep `autocorrect="on"`** (set in
`WebEditor/build/src/editorSetup.js` via `EditorView.contentAttributes`, deliberately overriding
CodeMirror's built-in `"off"`). Edge/WebView2 silently drops the Windows emoji panel's
(Win+. / Win+;) TSF commit into any contenteditable that combines `autocorrect="off"` with
block-level children — and CM6 documents are always block `.cm-line` divs. The drop happens in
the browser process: the page receives **zero** DOM events, so it cannot be observed or worked
around page-side. Full bisection and receipts:
`Docs/Investigations/05-EMOJI-PANEL-AUTOCORRECT-OFF-DROP.md`.

Setting `autocorrect="on"` does not spell-check or squiggle anything (`spellcheck` stays
`"false"`); it means the editor respects the user's Windows autocorrect setting, which is off by
default system-wide.

## What this replaced — do not resurrect it

The panel insert works **natively** in the composition-hosted WebView2. The earlier belief that
composition hosting exposes "no OS text store" was wrong (it was only ever tested against
CodeMirror, which failed for the autocorrect reason). Consequently the whole bridging apparatus
was removed and must not come back:

- `EmojiPanelSink` / `EmojiPanelWatcher` (hidden caret-positioned `TextBox` armed by a WinEvent
  hook on the panel) — deleted.
- The `caret` bridge message (page pushed caret rects to position the sink) — deleted.
- The WinEvent P/Invokes in `NativeMethods` — deleted.

If the panel ever stops inserting again, first check the live attribute on `.cm-content`
(`autocorrect` must read `"on"`) and re-run the bisect harness (`Scratch/cm6probe/` +
`Scratch/probe-cm6-panel.ps1` against `Scratch/wv2spike/`) before building anything.

## Related facts that stay true

- The panel commits via a TSF **composition** (`insertCompositionText`), not keystrokes; Win+V
  pastes through the keyboard path, which is why it always worked.
- Windowed `CoreWebView2Controller` hosting under WinUI on Win11 stays input-dead (upstream
  [microsoft-ui-xaml #10826](https://github.com/microsoft/microsoft-ui-xaml/issues/10826));
  irrelevant now that composition hosting handles the panel, but noted for anyone reconsidering
  hosting modes.
- IMEs and dictation also insert through text services. If they misbehave, verify end-to-end
  against the packaged app, asserting on the persisted buffer file
  (`Docs/Wiki/02-SYNTHETIC-INPUT-VERIFICATION.md`) — this class of bug is invisible to the page.
