# Web editor: the CodeMirror 6 bundle and its build

> _Last updated: 2026-07-14_

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
    ├── src/main.js       # the editor source: CM6 setup + the host bridge
    └── node_modules/     # gitignored
```

## Rules when touching this

- **The built `editor.bundle.js` is committed**, so `dotnet build` never runs
  npm. Only the three shipped files (`editor.html`, `editor.css`,
  `editor.bundle.js`) are packaged as `Content`; everything under `build/` is
  excluded.
- **Edit the editor logic in `build/src/main.js`, never in the built
  `editor.bundle.js`.** After changing `main.js` (or bumping a pinned CM6
  version in `package.json`), rebuild the bundle and commit the regenerated
  `editor.bundle.js`:

  ```bash
  cd QuinSlate.Ui/WebEditor/build
  npm ci        # restore exact pinned versions from package-lock.json
  npm run build # regenerate ../editor.bundle.js
  ```

- CodeMirror 6 is the **second sanctioned third-party dependency** after Serilog
  — vendored and pinned. Do not add npm packages casually; the "no third-party
  packages unless absolutely necessary" rule applies to the JS side too.
- The page is served to the WebView2 over a virtual-host mapping
  (`https://quinslate.editor/`) from the app's install/output directory; the
  host↔page contract is the JSON bridge in `Components/EditorHost.cs`.
- **The "never log buffer contents" rule extends across the bridge:** messages
  carrying buffer text (`init`, `setText`, `insert`, `contentSync`,
  `calcRequest`/`calcResult`) are never logged on either side — only message
  names, indices, and lengths.
- **The character cap is enforced in exactly one place:** the `capFilter`
  transaction filter in `main.js`. Every route into the document — typing, IME,
  dictation, paste, drag-drop, and the host's own `insert` message — is a CM6
  transaction, so it passes through the filter and nothing else needs to clamp.
  A clamped user edit reports a `limitReached` message (index, cause, dropped
  count — no text) that the host throttles into the "tab is full" notice; see
  [../Specs/18-CHARACTER-LIMIT-NOTICE.md](../Specs/18-CHARACTER-LIMIT-NOTICE.md).
- **The editor's `contentAttributes` must keep `autocorrect: "on"`.** Edge/WebView2
  silently drops the Windows emoji panel's (Win+. / Win+;) insert into any
  contenteditable that combines `autocorrect="off"` with block-level children —
  which CodeMirror always has (`.cm-line` divs). The drop is browser-side: the page
  sees zero DOM events, so no page-side code can detect or fix it. `main.js`
  overrides CM6's built-in `"off"` for exactly this reason (`spellcheck` stays
  `"false"`). Do not "restore" `autocorrect: "off"` for editor hygiene. See
  [03-EDITOR-OS-TEXT-INPUT.md](03-EDITOR-OS-TEXT-INPUT.md) and
  [05-EMOJI-PANEL-AUTOCORRECT-OFF-DROP.md](../Investigations/05-EMOJI-PANEL-AUTOCORRECT-OFF-DROP.md).
