# Windows emoji panel did not insert into the CodeMirror editor
> _Last updated: 2026-07-14_

## Symptom

After the CodeMirror/WebView2 migration (ADR `04-EDITOR-CODEMIRROR-WEBVIEW2.md`), pressing
**Win+;** or **Win+.** over a buffer opened the Windows emoji panel, but picking an emoji inserted
nothing. No error, the panel stayed open, typing kept working. The retired `RichEditBox` editors
accepted emoji from the panel; so does Obsidian (Electron + CM6), which the app was compared
against.

## Root cause (the real one)

**An Edge/WebView2 bug: the browser process silently drops the emoji panel's TSF commit when the
focused contenteditable has `autocorrect="off"` *and* contains block-level children.** Both
conditions are required:

- a flat contenteditable with `autocorrect="off"` accepts panel insertions;
- a contenteditable with block children (`<div>…</div>` lines) and no attributes accepts them;
- the combination — `<div contenteditable="true" autocorrect="off"><div>x</div></div>` — has the
  commit dropped browser-side. Zero DOM events reach the page (no `compositionstart`, no
  `beforeinput`, nothing), which is what made this so hard to see from page-side probes.

CodeMirror 6 always trips both conditions: it sets `autocorrect="off"` on `.cm-content`
unconditionally, and every document line is a block `<div class="cm-line">`. Our own
`contentAttributes` in `main.js` set `autocorrect: "off"` as well (RichEditBox parity). Obsidian
is unaffected because Electron ships stock Chromium, which lacks Edge's Windows-autocorrect
integration layer — this failure is Edge/WebView2-specific.

The working panel commit arrives as a composition (`compositionstart` →
`insertCompositionText` → `compositionend`), not as keystrokes. Win+V (clipboard history) always
worked because it pastes through the keyboard path.

**The fix is one attribute:** `autocorrect: "on"` in the editor's `contentAttributes`
(`WebEditor/build/src/editorSetup.js`). `EditorView.contentAttributes` overrides CM6's built-in `"off"`.
`spellcheck` stays `"false"` (no squiggles); with `autocorrect="on"` the editor simply respects
the user's Windows autocorrect setting (off by default system-wide) like any native field.

## How the bisection ran (and what it exonerated)

Minimal repro: a WinForms windowed WebView2 (`Scratch/wv2spike/`) serving a page with several
focusable targets (`Scratch/cm6probe/`), driven by synthetic input
(`Scratch/probe-cm6-panel.ps1`), with every DOM event and doc change posted to the host and
logged to a file. Successive A/B rounds, all in the same page and session:

| Experiment | Panel insert |
|---|---|
| plain contenteditable | ✅ |
| plain + each/all of CM6's attributes (flat) | ✅ |
| nested block line, no attributes | ✅ |
| bare CM6 (zero extensions, view 6.43.6) | ❌ |
| bare CM6 with `DOMObserver` stopped (static DOM, no selectionchange) | ❌ |
| **dead clone of CM6's DOM (no JS attached at all)** | ❌ |
| clone without wrappers / without classes (no injected CSS) | ❌ |
| attributes × block child, split until single attribute | `autocorrect="off"` ❌, everything else ✅ |
| `autocorrect="on"` × block child | ✅ |
| real CM6 + `contentAttributes.of({ autocorrect: "on" })` | ✅ |

Exonerated along the way: CM6's event handlers and selection handling (the dead clone fails with
no JS), the MutationObserver/selectionchange machinery, the wrapper hierarchy, the injected CSS,
`white-space`, `spellcheck`, `writingsuggestions`, `translate`, `autocapitalize`, `role="textbox"`,
`aria-multiline`, and EditContext (Android-gated in view 6.43.6 — never active here).

Two-line minimal repro for an upstream report:

```html
<div contenteditable="true" autocorrect="off"><div>x</div></div>  <!-- panel drops -->
<div contenteditable="true" autocorrect="on"><div>x</div></div>   <!-- panel inserts -->
```

## The graveyard: theories this replaced (all tested, all now moot)

This bug was previously fixed by a **caret-positioned sink** (`EmojiPanelSink` +
`EmojiPanelWatcher`, a hidden XAML `TextBox` armed by a WinEvent cloak/uncloak hook on the
panel's `TextInputHost` windows, forwarding insertions over the bridge) built on the theory that
a composition-hosted WebView2 exposes **no OS text store** at all. That theory was wrong — it was
only ever tested against CodeMirror, which fails for the reason above. With `autocorrect="on"`,
the panel inserts natively into the composition-hosted WebView2; the sink, the watcher, the
caret-push bridge message, and the WinEvent interop were all removed.

Also investigated and now unnecessary:

- `COREWEBVIEW2_FORCED_HOSTING_MODE=COREWEBVIEW2_HOSTING_MODE_WINDOW_TO_VISUAL` — ignored by the
  WinUI `WebView2` control (it requests a composition controller).
- **Windowed hosting** (`CoreWebView2Controller` parented to the main window) — renders but all
  input is dead on Win11 under WinUI (upstream
  [microsoft-ui-xaml #10826](https://github.com/microsoft/microsoft-ui-xaml/issues/10826)). The
  workaround from that issue (clear `WS_EX_TRANSPARENT` on Chromium's `Intermediate D3D Window`)
  was verified to fully restore typing/mouse — but the panel still dropped its commit there, for
  the same `autocorrect` reason, which is what finally pointed away from hosting modes and at the
  field itself.

Useful facts preserved from those detours, should anyone need them again:

- The panel's `TextInputHost` windows are permanent and DWM-cloaked; only
  `EVENT_OBJECT_UNCLOAKED`/`CLOAKED` WinEvents fire at open/close (creation/visibility/cloak
  polling carry no signal).
- The panel re-targets whichever control holds focus at pick time, even one focused after it
  opened; it commits via TSF composition, and falls back to nothing (not keystrokes) when the
  store rejects it.
- `IsTabStop="False"` on a WinUI control also blocks programmatic `Focus()`.
- `event.code` is `""` for all keys in a composition-hosted WebView2 (XAML forwards keystrokes
  without scan codes), so page-side hotkey sniffing is a trap.
- A stuck `TextInputHost` eats all input system-wide; `Stop-Process -Name TextInputHost` resets
  it (it respawns).

## Verification

Driven end-to-end against the packaged app with synthetic input (see
`Docs/Wiki/02-SYNTHETIC-INPUT-VERIFICATION.md`), asserting on the persisted buffer files:

- Win+. and Win+; both insert (surrogate pair lands in the buffer file, exact length delta).
- Two inserts in one panel session land both; light-dismissing with a mouse click then typing
  immediately loses nothing (+2 pairs / +6 length, exact).
- The panel opens anchored over the editor with a working search box.
- 183/183 unit tests pass.
