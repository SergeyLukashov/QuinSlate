# Web editor (CodeMirror 6 bundle) — rules for this directory

Full guide: `Docs/Wiki/06-WEB-EDITOR-BUNDLE.md`. ADR: `Docs/Decisions/04-EDITOR-CODEMIRROR-WEBVIEW2.md`. Spec: `Docs/Specs/17-EDITOR-CODEMIRROR-MIGRATION.md`.

- **Never hand-edit `editor.bundle.js`** — it is a built artifact (committed and shipped so
  `dotnet build` never runs npm). Edit the ES modules under `build/src/` (`main.js` is only
  the entry point that wires them up), then rebuild and commit the regenerated bundle:

  ```bash
  cd QuinSlate.Ui/WebEditor/build
  npm ci        # restore exact pinned versions from package-lock.json
  npm run build # regenerate ../editor.bundle.js
  ```

- Only `editor.html`, `editor.css`, and `editor.bundle.js` are shipped (packaged as `Content`);
  everything under `build/` is dev-only and excluded.
- Do not add npm packages casually — CodeMirror 6 is the second sanctioned third-party
  dependency after Serilog; the "no third-party packages unless absolutely necessary" rule
  applies to the JS side too.
- The page is served over the `https://quinslate.editor/` virtual-host mapping; the host↔page
  contract is the JSON bridge in `Components/EditorHost.cs`. **Never log buffer text across
  the bridge** — only message names, indices, and lengths.
- **`contentAttributes` must keep `autocorrect: "on"`** (`spellcheck` stays `"false"`).
  Edge/WebView2 silently drops Windows emoji-panel input into a contenteditable with
  `autocorrect="off"` + block children; the page sees zero DOM events, so nothing page-side
  can fix it. Do not "restore" `"off"` for editor hygiene. See
  `Docs/Wiki/03-EDITOR-OS-TEXT-INPUT.md`.
