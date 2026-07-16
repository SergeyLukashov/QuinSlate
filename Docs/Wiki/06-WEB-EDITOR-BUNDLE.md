# Web editor: the CodeMirror 6 bundle and its build

> _Last updated: 2026-07-16_

The buffer editor is a CodeMirror 6 web app hosted in a `WebView2` (see ADR
[04-EDITOR-CODEMIRROR-WEBVIEW2.md](../Decisions/04-EDITOR-CODEMIRROR-WEBVIEW2.md) and spec
[17-EDITOR-CODEMIRROR-MIGRATION.md](../Specs/17-EDITOR-CODEMIRROR-MIGRATION.md)). Its sources
and build tooling live under `QuinSlate.Ui/WebEditor/`, **not** in `Assets/`
(which is image/icon assets only):

```
QuinSlate.Ui/WebEditor/
├── editor.html          # page shell (shipped)
├── editor.css           # page-shell styling: background, scrollbar, animations (shipped)
├── editor.bundle.js     # BUILT CM6 + bridge bundle — committed, shipped, do NOT hand-edit
└── build/               # dev-only, never compiled or packaged
    ├── package.json      # pinned @codemirror/{state,view,commands} + esbuild
    ├── package-lock.json # committed lockfile (exact versions)
    ├── build.mjs         # esbuild bundler → ../editor.bundle.js
    ├── test/             # unit tests — node:test, no test framework (see "Testing")
    │   ├── setup.mjs         # browser-global stubs, preloaded via --import
    │   ├── editorHarness.mjs # headless stand-in for the EditorView
    │   └── *.test.mjs        # one file per module under test
    ├── src/              # the editor source, split into focused ES modules
    │   ├── main.js           # entry point: wires the modules up, posts "ready"
    │   ├── hostBridge.js     # postToHost/onHostMessage + the HostOrigin annotation
    │   ├── editorContext.js  # shared session state: the view, active index, accent
    │   ├── crlfText.js       # CRLF length maths + incoming-text normalisation
    │   ├── charLimit.js      # the capFilter transaction filter + limitReached report
    │   ├── contentSync.js    # debounced contentSync push to the host
    │   ├── inlineCalc.js     # "=" detection, calcRequest/calcResult handling
    │   ├── calcHighlight.js  # the fading calc-result mark decoration
    │   ├── listItems.js     # shared: task/bullet/number recognition + nesting depth
    │   ├── indent.js         # Tab/Shift+Tab: list nesting, and plain-line indent
    │   ├── tasks.js          # checkable tasks: widget, decorations, keymap
    │   ├── lists.js          # bullet/numbered items: widgets, keymap, renumber filter
    │   ├── panelShortcuts.js # panel keymap forwarded to the host
    │   ├── editorTheme.js    # the CM6 theme (fonts, caret, selection)
    │   ├── editorSetup.js    # baseExtensions, makeState, view creation + DOM listeners
    │   ├── buffers.js        # the five per-buffer states, activate/setText
    │   ├── entrance.js       # the tab-content entrance replay
    │   ├── startupReveal.js  # caret-blink hold + the startup reveal choreography
    │   ├── appearance.js     # theme colours + background PNG from the host
    │   └── hostMessages.js   # dispatch table for incoming host messages
    └── node_modules/     # gitignored
```

## Rules when touching this

- **The built `editor.bundle.js` is committed**, so `dotnet build` never runs
  npm. Only the three shipped files (`editor.html`, `editor.css`,
  `editor.bundle.js`) are packaged as `Content`; everything under `build/` is
  excluded.
- **Edit the editor logic in the modules under `build/src/`, never in the built
  `editor.bundle.js`.** After changing any source module (or bumping a pinned CM6
  version in `package.json`), rebuild the bundle and commit the regenerated
  `editor.bundle.js`:

  ```bash
  cd QuinSlate.Ui/WebEditor/build
  npm ci        # restore exact pinned versions from package-lock.json
  npm run build # regenerate ../editor.bundle.js
  ```

- CodeMirror 6 is the **second sanctioned third-party dependency** after Serilog
  — vendored and pinned. Do not add npm packages casually; the "no third-party
  packages unless absolutely necessary" rule applies to the JS side too. The
  tests hold that line: they use Node's built-in runner, so the suite added no
  packages at all (see "Testing").
- **Editor logic changes need a test.** `npm test` must pass before the work is
  done, alongside `dotnet test`.
- The page is served to the WebView2 over a virtual-host mapping
  (`https://quinslate.editor/`) from the app's install/output directory; the
  host↔page contract is the JSON bridge in `Components/EditorHost.cs`.
- **The "never log buffer contents" rule extends across the bridge:** messages
  carrying buffer text (`init`, `setText`, `insert`, `contentSync`,
  `calcRequest`/`calcResult`) are never logged on either side — only message
  names, indices, and lengths.
- **The character cap is enforced in exactly one place:** the `capFilter`
  transaction filter in `build/src/charLimit.js`. Every route into the document — typing, IME,
  dictation, paste, drag-drop, and the host's own `insert` message — is a CM6
  transaction, so it passes through the filter and nothing else needs to clamp.
  A clamped user edit reports a `limitReached` message (index, cause, dropped
  count — no text) that the host throttles into the "tab is full" notice; see
  [../Specs/18-CHARACTER-LIMIT-NOTICE.md](../Specs/18-CHARACTER-LIMIT-NOTICE.md).
- **Plain Tab belongs to indentation, not buffer cycling.** It cycled buffers
  until indentation landed; `Ctrl+Tab` / `Ctrl+Shift+Tab` are now the only
  cycling keys (`panelShortcuts.js`), and `indent.js` owns Tab/Shift+Tab for
  tasks, bullets, numbered items and plain lines alike. Do not "restore" the Tab
  binding in `panelShortcuts.js` — it is `Prec.highest` and would silently shadow
  indentation. The C# side (`BufferKeyboardController.HandlePanelPreviewKey`)
  already gates every shortcut but F2 behind Ctrl, so it needs no matching
  change. Tab must still never insert a tab character or move DOM focus: the
  indent commands report handled even where the shift itself is refused. See
  [../Specs/06-KEYBOARD-NAV.md](../Specs/06-KEYBOARD-NAV.md) and
  [../Specs/17-EDITOR-CODEMIRROR-MIGRATION.md](../Specs/17-EDITOR-CODEMIRROR-MIGRATION.md).
- **A transaction that inserts at the caret's own position must map the selection
  itself.** `indent.js` passes `selection: state.selection.map(changes, 1)`
  explicitly: CM6's default association maps a position sitting *exactly* at an
  insertion point to before the inserted text. For Tab that meant the line
  indented and the caret stayed at column 0 — invisible for a caret in the middle
  of a line (it shifts either way), so it only shows on an empty line or at
  column 0. Do not drop that `map` call.
- **Depth is text, one unit is two spaces, and every line has it** — list item or
  not. `listItems.js` is the only place that parses it. Keep the unit shared: the
  `- ` / `1. ` / `[] ` shorthands read depth from the same leading spaces, so a
  plain line indented once converts to a **depth-1** item. Widen the unit for
  plain text alone and one Tab silently becomes two levels.
- **A marker widget replaces indent *and* marker as one atomic range.** That is
  what makes Backspace drop a nested item straight back to plain text at column
  0, and what keeps the caret out of the prefix — do not narrow the range to the
  marker alone. Depth reaches the widget as an inline `--list-depth` custom
  property; the 23px-per-level step lives in `editor.css` with the other marker
  metrics. Note a plain line's indent is *real spaces* (~9px each in Cascadia
  Code 15px), so one plain level is ~18px against a list level's 23px: same
  depth in text, deliberately different on screen.
- **The editor's `contentAttributes` must keep `autocorrect: "on"`.** Edge/WebView2
  silently drops the Windows emoji panel's (Win+. / Win+;) insert into any
  contenteditable that combines `autocorrect="off"` with block-level children —
  which CodeMirror always has (`.cm-line` divs). The drop is browser-side: the page
  sees zero DOM events, so no page-side code can detect or fix it. `build/src/editorSetup.js`
  overrides CM6's built-in `"off"` for exactly this reason (`spellcheck` stays
  `"false"`). Do not "restore" `autocorrect: "off"` for editor hygiene. See
  [03-EDITOR-OS-TEXT-INPUT.md](03-EDITOR-OS-TEXT-INPUT.md) and
  [05-EMOJI-PANEL-AUTOCORRECT-OFF-DROP.md](../Investigations/05-EMOJI-PANEL-AUTOCORRECT-OFF-DROP.md).

## Testing

```bash
cd QuinSlate.Ui/WebEditor/build
npm ci        # first time only
npm test      # node --test over test/*.test.mjs
```

**No test framework, and no new packages.** The suite runs on Node's built-in
runner (`node:test` + `node:assert/strict`), which keeps the "no third-party
packages unless absolutely necessary" rule intact on the JS side — the only
devDependency remains esbuild. Do not add Jest/Vitest/jsdom without a reason the
built-in runner cannot meet.

### How the tests reach the modules

- **`test/setup.mjs`** stubs `window` and `document`, and is preloaded with
  `node --test --import ./test/setup.mjs`. It has to be a preload, not a plain
  import: `hostBridge.js` reads `window.chrome.webview` **as its module body
  runs**, so the globals must exist before any `src/` module is imported.
  With `window.chrome` null, `postToHost` is a no-op and nothing reaches for a
  host that is not there.
- **`test/editorHarness.mjs`** is a headless stand-in for the `EditorView`: CM6
  commands only touch `view.state` and `view.dispatch`, so an object with those
  two drives them exactly as the real keymap does. Its `dispatch` is **variadic**
  on purpose — CM6 merges multiple specs into one transaction and the task/list
  Enter handlers depend on that (`view.dispatch(state.replaceSelection(...), {
  userEvent })`); a single-spec stub silently drops the second half.
- Tests pass the extensions they depend on (e.g. `listRenumber`) explicitly
  rather than importing `editorSetup.js`, which builds a real `EditorView`.

### What the tests do and do not cover

Covered: `listItems.js`, `indent.js`, `tasks.js`, `lists.js`, `crlfText.js` —
parsing, commands, guardrails, the renumber filter, and caret positions.

**Not covered: anything that needs layout or a real DOM.** The widgets' `toDOM`
and every pixel metric in `editor.css` (the 23px indent step, the checkbox glyph
offsets) are unverified by `npm test` and always will be — jsdom does not do
layout, so it would buy false confidence rather than real coverage. Visual
changes need the app on screen; see the `winui3-visual-verify` skill.

Also not yet covered, in rough order of value: `charLimit.js` (the cap's budget
maths — testable, but every case needs a ~1M-character fixture),
`inlineCalc.js` and `contentSync.js` (both need the bridge and timers mocked).
