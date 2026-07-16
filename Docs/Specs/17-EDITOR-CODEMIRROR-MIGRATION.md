# SPEC: Editor migration to CodeMirror 6 in WebView2

> _Last updated: 2026-07-09_

## What

Replace the five `RichEditBox` buffer editors with **CodeMirror 6 (CM6)**
running in a single **WebView2** control. This is a **like-for-like
migration**: every observable behaviour of the current editor is preserved.
No new features, no raised limits, no changed file formats.

Decision record: [Docs/Decisions/04-EDITOR-CODEMIRROR-WEBVIEW2.md](../Decisions/04-EDITOR-CODEMIRROR-WEBVIEW2.md).
Motivating defect: [Docs/Investigations/03-RICHEDITBOX-TALL-DOCUMENT-RENDER-CEILING.md](../Investigations/03-RICHEDITBOX-TALL-DOCUMENT-RENDER-CEILING.md).

## Non-goals (explicitly out of scope)

- Raising or removing `AppConstants.MaxBufferLength` (stays **50,000** for the
  migration itself). _Since done as the anticipated follow-up: with CM6 shipped
  and the `RichEditBox` render ceiling gone, the cap is now **1,000,000**._
- Task lists, links, or any rich content. Plain text only.
- Changing buffer file format (stays UTF-8 with BOM `.txt`, CRLF).
- Changing the tab strip, tray peek, emoji picker, or any non-editor surface.
- Changing the inline-calc trigger rules, heuristic, or result formatting
  ([11-INLINE-CALC.md](11-INLINE-CALC.md) semantics are unchanged).

**Acceptance bar: a user of the current build must not be able to tell the
editor was replaced, except that previously-broken things now work** (see
"Accepted improvements" at the end).

---

## Architecture

### One WebView2, five documents

- A **single** `WebView2` control hosts one editor page. Five webviews would
  quintuple Chromium's memory cost and break the shared-surface gradient.
- The page holds **five CM6 `EditorState`s** (one per buffer, 1-based indices
  1–5, matching `Buffer.Index`). Tab switching swaps the state into the one
  `EditorView`; per-buffer **undo history, selection, and scroll position
  live in the state** and must survive tab switches exactly as five separate
  `RichEditBox` instances do today.
- The webview element is the shared content of the tab area. `TabViewItem`
  contents become lightweight placeholders; on `SelectionChanged` the host
  tells the page which buffer state to activate.
- The WebView2 environment and the page are created **once at startup**
  (during the existing warm-up phase, while the window is still hidden) and
  never torn down while the app runs. Panel hide/show does not recreate
  anything.

### Assets

- Editor page (`editor.html`), CSS, and a single pinned CM6 bundle live under
  `QuinSlate.Ui/WebEditor/` as package `Content`, served via
  `CoreWebView2.SetVirtualHostNameToFolderMapping` (e.g.
  `https://quinslate.editor/`). No network access — the page must work fully
  offline and carries a CSP restricting all sources to itself.
- The CM6 bundle is **vendored**: pinned versions, lock file, and the built
  bundle are committed; a rebuild script lives beside them. Building
  QuinSlate never requires npm.

### WebView2 environment

- **User data folder** must be explicitly set inside the app-data directory
  (MSIX-packaged apps cannot use the default next-to-exe location).
- `DefaultBackgroundColor` = the flat gradient mid-tone
  (`DitheredGradientBrushFactory.MidColor`) **before first navigation** — the
  Chromium default is white and would flash.
- Settings, all set before the page is shown:
  - `AreDefaultContextMenusEnabled = false` (QuinSlate's own menu, below)
  - `AreBrowserAcceleratorKeysEnabled = false` (no Ctrl+F/Ctrl+P/F5 browser UI)
  - `IsZoomControlEnabled = false`, pinch zoom disabled (editor font size is
    fixed at 15px; zoom would be a new feature)
  - `IsStatusBarEnabled = false`, `IsSwipeNavigationEnabled = false`
  - `AreDevToolsEnabled = false` in Release (buffer text is visible in
    DevTools; Debug builds may enable it)
  - Chromium spellcheck **disabled** (parity with `IsSpellCheckEnabled=false`)
  - autofill/password UI disabled
- `NewWindowRequested` cancelled; `NavigationStarting` allows only the
  virtual-host origin. The page can never navigate away.
- `ProcessFailed`: recreate the webview, reload the page, repopulate all five
  buffers from `BufferService` (host state is authoritative — see
  persistence). Log the failure. The user loses at most in-session undo
  history, never text.
- WebView2 Runtime missing / creation failure: log, show a plain inline
  message in the tab area. Never crash. (Windows 11 ships the Evergreen
  Runtime; this is a Win10-edge-case safety net.)

### Host ↔ page bridge

`window.chrome.webview.postMessage` / `WebMessageReceived`, JSON messages.
Contracts (names indicative):

| Direction | Message | Purpose |
|---|---|---|
| host → page | `init { buffers: [{index, text}] }` | Populate all five states once at startup |
| host → page | `activate { index }` | Tab switch |
| host → page | `setText { index, text }` | Clear tab / external rewrite |
| host → page | `focus` | Focus the active editor |
| host → page | `background { pngBase64, cssWidth, cssHeight }` | Dithered mesh swap (below) |
| host → page | `theme { … }` | Colours on theme change |
| host → page | `calcResult { index, ok, result }` | Calc evaluation reply |
| page → host | `ready` | Page + CM6 initialised |
| page → host | `contentSync { index, text }` | Debounced content push (below) |
| page → host | `calcRequest { index, lineContent }` | Line ending in `=` typed |
| page → host | `key { … }` | Panel-level shortcuts typed inside the editor |
| page → host | `contextMenu { x, y, canUndo, canRedo, hasSelection }` | Right-click |
| page → host | `historyState { index, canUndo, canRedo }` | Menu enablement |

**Logging rule:** messages carrying buffer text are never logged — not the
payload, not a prefix, not on either side. Log message *names*, indices, and
lengths only. This is the existing "never log buffer contents" rule crossing
the bridge.

---

## Regression contract

Everything in this section is current behaviour and must survive unchanged.

### Visual parity

- **Typography:** Cascadia Code, 15px, line spacing 1.4 (CSS
  `line-height: 1.4`), wrap at the viewport (soft wrap, no horizontal
  scrolling), padding 16/10/16/16 with the extra 4px top gap above the editor
  content, left-aligned, LTR.
- **Dithered gradient background.** The single source of truth stays the
  `AppGradient*` resources in `App.xaml` and `DitheredGradientBrushFactory`.
  The factory's per-pixel TPDF pipeline renders the editor-surface bitmap at
  **native pixel size** (CSS size × `devicePixelRatio`); the host passes the
  PNG to the page, which displays it **1:1 with no CSS scaling** (a stretched
  dithered bitmap re-bands). Rebuild on resize (existing 90 ms debounce), on
  theme change, and on rasterization-scale change. Do **not** rely on a
  transparent webview compositing over the XAML brush unless verification
  proves text antialiasing is unaffected; the bitmap-in-page path is the
  reference implementation.
- **Startup choreography preserved:** first composited frame is the flat
  mid-tone on every surface (XAML fallback under the webview +
  `DefaultBackgroundColor` + page body colour all equal `MidColor`); the
  dithered mesh swaps in **all-or-nothing** (window and editor together) once
  the page reports `ready` and has its bitmap. No white flash, no banded
  linear-gradient flash, no window-mesh-before-editor-mesh snap. The webview
  stays visually silent (flat tone) until its first real frame.
- **Opaque surface** — the editor surface never becomes see-through; caret
  and text contrast are unchanged.
- **Caret:** visible, blinking, standard I-beam behaviour. Selection colour
  matches the current accent-derived selection appearance in both themes.
- **Scrollbar:** styled (CSS) to match the WinUI overlay scrollbar the editor
  shows today — thin idle state, expands on hover, right-edge overlay. At a
  glance the editor must not look "web".
- **Tab content entrance animation:** the 180 ms fade + 12 px slide-up on tab
  switch is preserved (replayed on the shared editor surface — XAML container
  animation or equivalent CSS, whichever reproduces the current look
  exactly).
- **Theme change:** editor colours and gradient rebuild on
  `ActualThemeChanged`, as today, without reloading the page or losing state.
- **Mixed DPI:** moving the window between monitors with different scale
  factors re-rasterizes crisply (no persistent blur) and triggers a gradient
  rebuild at the new native size.
- No new chrome of any kind: no line numbers, no active-line highlight, no
  gutter, no minimap, no find bar.

### Text semantics and limits

- Plain text only. **Ctrl+B / Ctrl+I / Ctrl+U do nothing** (today they are
  suppressed so RichEdit cannot toggle formatting; CM6 has no formatting to
  toggle — verify no CM6 default binding is attached to them).
- **50,000-char cap**, enforced in one place: a CM6 transaction filter that
  truncates any insertion (typing, paste, IME commit, drop) so the document
  never exceeds the cap **counted in CRLF form** (each line break = 2 chars),
  matching the disk clamp exactly. This replaces today's three-way
  enforcement (`MaxLength`, paste clamp, `TruncateToMaxLength`) and removes
  the editor-vs-disk drift that `TruncateToMaxLength` existed to repair.
  Editor content and persisted content are always identical.
- **Paste** (Ctrl+V and context menu): text-only (non-text clipboard content
  ignored), inserted at the selection, clamped to remaining capacity; when
  the buffer is at the cap nothing is inserted. Line endings normalise to the
  document's convention. One shared paste path, as today.
- Text **drag & drop** into the editor keeps working (and respects the cap
  via the same transaction filter).
- **Undo/redo:** per-buffer history; Ctrl+Z/Ctrl+Y (and Ctrl+Shift+Z if it
  works today — verify) behave as now; programmatic calc rewrites are a
  single undo step that reverts to the un-evaluated line. History is
  session-only (not persisted), per buffer, and survives tab switches.
- Select All (Ctrl+A and menu) selects the whole document.

### Persistence (contract with `BufferService` unchanged)

- Files stay `%AppData%\QuinSlate\buffer-N.txt`, UTF-8 with BOM, CRLF.
  `BufferService`, its 300 ms write debounce, clamp, and shutdown flush are
  **not modified**.
- The page pushes `contentSync` (full text) per buffer on a **300 ms debounce
  after the last change** — replacing today's `contentExtractTimer` +
  `Document.GetText` pull with a push of identical cadence — **and
  immediately on editor blur and on panel hide**. The host mirrors the latest
  text per buffer and feeds `BufferService.UpdateContent` exactly as
  `ExtractDirtyBuffers` does today (including trailing line-break trim).
- **Shutdown flush:** `FlushPendingContent` cannot synchronously query the
  page. The host-side mirror is the flush source. Because sync also fires on
  blur and panel hide — and exit paths (tray menu quit, session end) happen
  with the panel hidden or losing focus — the mirror is current in practice.
  On exit the host may additionally wait a short bounded time (≤200 ms) for a
  final in-flight `contentSync`; then `BufferService`'s synchronous flush
  runs as today. Residual exposure (process killed mid-keystroke-burst) is no
  worse than the current 300 ms extract debounce window.
- **Clear tab** (menu, with confirm step): editor text set to empty via
  `setText`, `BufferService.UpdateContent(index, "")` called directly, no
  double-extraction from the echo, clear menu item disables when the buffer
  is empty and re-enables on first content — all exactly as today.

### Inline calculator ([11-INLINE-CALC.md](11-INLINE-CALC.md))

- `CalcService` (NCalc, guard, heuristic, formatting) **stays in C# and is
  not modified**; its unit tests keep passing untouched.
- The page detects a typed `=` from the CM6 transaction itself (an inserted
  `=` at the caret from user input) — the composed-character semantics that
  `CharacterReceived` provides today, i.e. layout-independent, Shift/AltGr
  agnostic. It applies the cheap positional pre-checks (caret at/after last
  non-whitespace of the line — the mid-line rule) and sends `calcRequest`
  with the line content.
- Host evaluates via `CalcService.TryEvaluate` and replies. On success the
  page rewrites the line **in place as one undoable transaction**, places the
  caret at the end of the line, and the normal content-sync marks the buffer
  dirty. On failure the `=` stays exactly as typed — silent, like today.
- The bridge round-trip must be imperceptible (same-frame-feeling; the
  current implementation also defers to the `TextChanged` after the
  keystroke). If the user types again before the reply lands, the reply is
  discarded (the line changed) — equivalent to today's armed-flag drop.
- All spec-11 rules unchanged: adjacent-operator guard, digit+operator
  heuristic, spaced/unspaced forms, result formatting, silent failure,
  no variables.

### Calc result highlight ([12-CALC-RESULT-ANIMATION.md](12-CALC-RESULT-ANIMATION.md))

- Implemented as a CM6 **mark decoration** on the result range with a
  background that snaps to the Windows accent colour, then fades over
  **1600 ms** to nothing (a real alpha fade is now possible; the end state —
  indistinguishable from plain text — is what must match).
- Accent colour is supplied by the host (sampled per animation start, as
  today; theme change mid-fade keeps the sampled colour).
- Scope: the result value only — not the `=`, not the separator space.
- One animation at a time: a new evaluation snaps the previous one to its end
  state. Any user edit in that buffer cancels the animation instantly.
  No animation on startup load. All as today.

### Keyboard (parity with `BufferKeyboardController` — [06-KEYBOARD-NAV.md](06-KEYBOARD-NAV.md))

Critical gotcha: keys typed while the web editor has focus **do not route
through XAML `PreviewKeyDown`**. Every editor-focused shortcut below is
captured by a CM6 keymap (highest precedence) and forwarded to the host; the
existing XAML path continues to serve focus-elsewhere cases. Behaviour must
be identical regardless of where focus is:

- **Tab / Shift+Tab** inside the editor: indent / outdent by one level
  (`indent.js`). On a list item that means nesting, under Notion's guardrails —
  see [19-CHECKABLE-TASKS.md § Nesting](19-CHECKABLE-TASKS.md#nesting); on a
  plain line it is ordinary text indentation, with nothing to guard. Details:
  - **One level is two spaces**, the same unit list depth uses, so one Tab is
    one level on any line: indent a plain line once, type `- `, and the bullet
    arrives at depth 1. **Tab never inserts a tab character** — buffers are
    plain `.txt` — **and never moves DOM focus.**
  - **Tab shifts whole lines**, wherever the caret sits in them, so Shift+Tab is
    its exact mirror. It does not insert spaces at the caret.
  - **The caret rides the shift**, like any text editor: Tab on an empty line or
    at column 0 leaves the caret *past* the new indent, not before it. This needs
    an explicit `selection: state.selection.map(changes, 1)` on the transaction —
    CM6's default association maps a position sitting exactly at the insertion
    point to before the inserted text, so the line would move and the caret would
    not. Positions already inside the content shift either way.
  - A multi-line selection shifts every line it touches, as one undo step; blank
    lines are skipped (indenting them would only leave trailing whitespace), but
    a caret alone on a blank line still indents, so the user can indent before
    typing.
  - Depth is capped at 8 levels, plain lines included, so stored depth never
    outruns what the marker widgets can render.
  - Shift+Tab at column 0 does nothing. Tab cycled buffers until indentation
    landed; cycling is now Ctrl+Tab only.
- **Ctrl+Tab / Ctrl+Shift+Tab:** cycle buffers.
- **Ctrl+1..5** (top row and numpad): jump to buffer N and focus its editor.
- **F2:** open the tab-edit flyout for the selected tab.
- **Escape:** panel-level handling (clear-confirm dismissal path) keeps
  working when the editor has focus.
- **Ctrl+B/I/U:** no-ops.
- Standard editing keys (arrows, Home/End, Ctrl+Home/End, PgUp/PgDn,
  Ctrl+arrow word movement, Shift-selection variants, Ctrl+Backspace/Delete
  word deletion) behave as a Windows text box.
- The global hotkey (Ctrl+Shift+Q) is `RegisterHotKey`-based and unaffected;
  verify it still toggles while the webview has focus.

### Focus

- `FocusActiveEditor` semantics: on panel show and tab switch, keyboard focus
  lands in the active editor so the user can type immediately. Deferral until
  ready moves from "editor `Loaded`" to "page `ready`" — a focus request
  before the page is up is queued, and only the latest queued request wins
  (parity with `EditorFocusController`).
- Focus handoff is two-stage: XAML focus to the `WebView2` element, then
  DOM focus to the CM6 view (`focus` message). Both directions of
  click-focus (clicking the editor area focuses it; clicking XAML chrome
  blurs it) must work.
- **Focusing must never scroll the view** — the panel reopening or tabs
  switching must not jump the scroll position (this is what
  `SmoothScrollController`'s focus-protection window and
  `BringIntoViewOnFocusChange=false` defend today; CM6 must be configured to
  not scroll-to-caret on focus).

### Scrolling

- Mouse wheel: smooth, ~3 lines per notch at current perceived speed —
  Chromium's native smooth scrolling replaces `SmoothScrollController`; the
  result must feel equal or better, never choppier.
- Touch panning, scrollbar hover/drag: native and correct.
- Per-buffer scroll position is preserved across tab switches.
- Scroll position survives panel hide/show.

### Context menu

- Right-click shows **the same native `MenuFlyout`** (Undo / Redo / — / Cut /
  Copy / Paste / — / Select All, same icons, same 135px min width, same
  arrow-cursor workaround). Chromium's own menu is disabled. The page reports
  the click position (CSS→DIP) plus `canUndo/canRedo/hasSelection`; the host
  opens the flyout at that position with today's enablement rules (Undo/Redo
  per history state, Cut/Copy require a selection, Paste always enabled) and
  routes actions back over the bridge.
- Touch/pen selection must not surface Chromium's selection handles' menu
  (parity with `SelectionFlyout = null`); suppress or match — verify.

### Tabs and panel integration

- Tab drag-reorder, tab labels, emoji picker, tab-edit flyout, pin/close
  overlay, tray peek, capture, dictation hooks: untouched. Anything that
  reads buffer text reads it from `BufferService` (as today) and needs no
  change; anything that writes buffer text programmatically goes through
  `setText`.
- Flyouts and popups (context menus, tab-edit flyout, emoji picker) must
  render **above** the webview. WinUI 3 popups are windowed and expected to;
  verify explicitly (airspace is the classic WebView2 failure mode).

### Lifecycle

- Panel hide → show: instant, identical content, caret and scroll preserved,
  no re-render flash. (Optional `TrySuspend` on hide is allowed **only** if
  resume is imperceptible; otherwise skip it.)
- App exit from tray while panel hidden: latest text persisted (see
  persistence).
- Second-instance surfacing, Win+D restore, topmost/pin behaviour: unchanged
  (window-level, not editor-level).

### IME and international input

- CJK IME composition works with a correctly positioned candidate window
  (Chromium first-class; explicitly verify because today's RichEdit path
  works too).
- Dead keys, AltGr layouts: unchanged behaviour. The calc trigger fires on a
  composed `=` from any layout.

### Accessibility

- Narrator can read and edit the buffer text via Chromium's UIA bridge.
  Must be no worse than today's RichEdit UIA support.

---

## Component mapping

| Today (retired/changed) | Replacement |
|---|---|
| `EditorViewBuilder` (RichEditBox construction) | WebView2 host setup + `editor.html`/CM6 config |
| `SmoothScrollController` | Chromium native scrolling + CM6 scroll config |
| `EditorPaste.PasteClampedAsync` | CM6 transaction filter (cap) + paste handling |
| `EditorContextMenu` | Same `MenuFlyout`, actions re-targeted over the bridge |
| `CalcResultAnimator` (RichEdit halves) | Page-side `=`-detection + decoration animation; `CalcService` untouched |
| `EditorFocusController` | Page-`ready`-gated focus queue |
| `BufferPanel` extract debounce (`contentExtractTimer`, `ExtractDirtyBuffers`, `TruncateToMaxLength`) | Page-side 300 ms `contentSync` push + host mirror |
| `BufferKeyboardController` editor `KeyDown` half | CM6 keymap → `key` messages (panel half remains) |
| `ApplyEditorDitheredBackground` / `TextControlBackground*` overrides | `background` message + page CSS (window-surface half of `ApplyDitheredBackground` remains) |
| Investigation 03 render ceiling | Moot (doc kept as history) |

`BufferService`, `SettingsService`, `CalcService`, tab strip, tray, peek,
emoji picker: **no changes**.

---

## Accepted improvements (the only permitted visible differences)

- Text past the old render ceiling now paints (the bug this migration kills).
- 🅰️🅱️🅾️-class VS16 emoji render in color in the buffer body.
- Grapheme-cluster caret movement/deletion over complex emoji is correct.
- The calc highlight fade is a true alpha fade (end state identical).

Anything else that looks or behaves differently is a regression.

---

## Verification checklist (gates completion)

The editor's logic has a unit suite — `cd QuinSlate.Ui/WebEditor/build && npm test`
(Node's built-in runner; see [../Wiki/06-WEB-EDITOR-BUNDLE.md](../Wiki/06-WEB-EDITOR-BUNDLE.md)).
It covers item parsing, Tab/Shift+Tab indentation and its guardrails, task and
list Enter/shorthand/toggle, the renumber filter, caret positions, and the CRLF
length maths, and it runs headless, so **it proves none of the rendering below**.
The checklist stays a manual pass.

Run on the dev machine (250% 4K + laptop mixed-DPI) and ideally the old
low-GPU laptop:

1. Side-by-side screenshot comparison per theme (light/dark): editor at rest,
   focused, hovered — gradient, font, padding, caret, scrollbar, selection.
2. Startup frame-burst capture (see the established technique): flat →
   dithered swap with no white/banded/partial frame.
3. Type at speed into a buffer at the cap; no perceptible latency (Serilog
   timing where measurable; no content logging).
4. Cap (`AppConstants.MaxBufferLength`): type at cap, paste over cap, paste into
   selection at cap, drop text at cap → clamped; file never exceeds cap.
5. Calc: all spec-11 trigger/guard/heuristic/format cases; Ctrl+Z reverts the
   evaluation; highlight snap + 1600 ms fade; cancel-on-edit; single
   animation; layout test with AltGr/Shift `=` (e.g. German layout).
6. Keyboard matrix with focus **inside** the editor: Tab/Shift+Tab,
   Ctrl+Tab/Ctrl+Shift+Tab, Ctrl+1..5 (both rows), F2, Escape, Ctrl+B/I/U,
   Ctrl+A/C/X/V/Z/Y, word-wise navigation/deletion; then the same with focus
   on panel chrome.
7. Persistence: type → wait 600 ms → check file; type → immediately quit from
   tray → check file; clear tab → file empty, menu disabled; restart restores
   all buffers byte-identically (UTF-8 BOM, CRLF).
8. Emoji: insert 😀, 👍🏽, 🅰️, family-ZWJ sequence; render in color; caret
   steps over each as one unit; backspace deletes whole cluster; survives
   save/reload byte-identically.
9. IME: Chinese pinyin + Japanese input; candidate window at the caret.
10. Tab switch: entrance animation, per-buffer undo/selection/scroll retained,
    focus lands in editor.
11. Hide/show panel (hotkey, tray, close button), Win+D then restore, monitor
    move across DPI boundaries, resize (gradient rebuild, no re-banding),
    theme switch live.
12. Context menu: position, items, enablement, all six actions, arrow cursor,
    renders above webview; tab-edit flyout and emoji picker also render above.
13. Narrator smoke test: read text, type, hear feedback.
14. Kill `msedgewebview2.exe` mid-session → editor recovers with text intact.
15. `dotnet test` green; `dotnet format` clean.

---

## Documentation updates (part of this work)

- CLAUDE.md stack table: editor row (WebView2 + CodeMirror 6), plus a short
  bridge/no-content-logging note.
- [01-CORE.md](01-CORE.md): editor-control sentence (currently "RichEditBox
  rather than TextBox").
- [11-INLINE-CALC.md](11-INLINE-CALC.md): "Control: RichEditBox" and
  implementation sections re-pointed at this spec's bridge design.
- [12-CALC-RESULT-ANIMATION.md](12-CALC-RESULT-ANIMATION.md): implementation
  section (decoration-based).
- [00-FEATURE-QUEUE.md](00-FEATURE-QUEUE.md): entry 17.
- Investigation 03: status note "superseded by CM6 migration (spec 17)".
