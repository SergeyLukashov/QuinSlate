## Stack

| Layer | Choice |
|---|---|
| UI framework | WinUI 3 (.NET 10) |
| Buffer editor | CodeMirror 6 hosted in a single `WebView2` (vendored bundle under `QuinSlate.Ui/WebEditor/`, served via a virtual-host mapping) |
| Tray icon | Win32 `Shell_NotifyIcon` via P/Invoke |
| Global hotkeys | Win32 `RegisterHotKey` via P/Invoke |
| Persistence | Plain `.txt` files in `%AppData%\QuinSlate\` |
| Always-on-top | Win32 `SetWindowPos` via P/Invoke |
| Single instance | Named Mutex (`Local\QuinSlateSingleInstance`) |
| Clipboard | WinRT `Windows.ApplicationModel.DataTransfer.Clipboard` (read/write via managed API) |
| Window background | Per-pixel TPDF-dithered gradient into a `WriteableBitmap` (no external deps); the editor page shows the same mesh as a host-rendered PNG |

WinUI 3 has no native tray API. All tray behaviour is Win32 via P/Invoke.

The five buffers are edited in one WebView2 hosting CodeMirror 6 (see ADR
`Docs/Decisions/04-EDITOR-CODEMIRROR-WEBVIEW2.md` and spec
`Docs/Specs/17-EDITOR-CODEMIRROR-MIGRATION.md`). CodeMirror 6 is the second
sanctioned third-party dependency after Serilog: a vendored, pinned bundle under
`QuinSlate.Ui/WebEditor/` (rebuild script in `build/`; the built
`editor.bundle.js` is committed, so building QuinSlate never runs npm). Host and
page talk over a `WebView2` JSON bridge (`EditorHost`). **The "never log buffer
contents" rule extends across the bridge:** messages carrying buffer text
(`init`, `setText`, `insert`, `contentSync`, `calcRequest`/`calcResult`) are
never logged on either side — only message names, indices, and lengths.

---

## Project structure

```
QuinSlate/
├── QuinSlate.Ui/             # WinUI 3 desktop application source code and assets
├── QuinSlate.Tests/          # Unit tests for models and services
├── QuinSlate.AssetGenerator/ # Asset generator utility console app
├── QuinSlate.EmojiPickerBench/ # Benchmark harness for the emoji picker
├── Docs/
│   ├── Specs/                # Product specifications and feature queue
│   ├── Investigations/       # Deep-dives into platform bugs, dead-end archaeology
│   ├── Decisions/            # Architecture decision records (ADRs)
│   ├── Plans/                # Implementation plans for multi-step work
│   └── Wiki/                 # "Read this before you change X" — distilled platform knowledge
├── Scratch/                  # Local-only scratchpad for temporary code/scripts
```

---

## Build baseline

Do not enable nullable reference types. Do not use `?` annotations on reference
types or `!` null-forgiving operators. Null checks are explicit `if (x == null)` guards.

Serilog is an accepted dependency for application logging (the one sanctioned
exception to "no third-party NuGet packages") — see `Docs/Decisions/01-LOGGING-SERILOG.md`
and `Docs/Specs/16-LOGGING.md`. Logs roll daily into the `Logs/` subfolder
of the app-data directory. Never log buffer/note contents.

---

## Code style

- C# 13 / .NET 10 idioms. File-scoped namespaces everywhere.
- Namespaces must match the folder structure exactly.
- One class per file. The filename must be identical to the class name.
- Follow SOLID principles. Each class has a single, focused responsibility.
- `private` fields use `camelCase`. Properties and methods use `PascalCase`.
- `var` is fine when the right-hand side makes the type obvious; use concrete types elsewhere.
- No `dynamic`.
- Prefer `async`/`await` over raw `Task.Run` where possible.
- Async/await for file I/O. Synchronous file writes only in the shutdown flush path.
- No third-party NuGet packages unless absolutely necessary.
- No regions (`#region`).
- No magic numbers. Every constant, raw string, or error code is extracted into a clearly named `const` or `enum`.
- XML doc comments (`///`) on all public members of `Interop/` and `Services/`.
- Do not add internal comments beyond what is genuinely necessary. Code must be self-descriptive. Only comment Win32 behaviour that cannot be made obvious through the code itself.

---

## Architecture rules

**P/Invoke** — All native signatures live in `NativeMethods.cs`. Do not scatter `[DllImport]` declarations across other files. Use `partial class` if a file needs to call native methods — import from `NativeMethods`, don't redeclare.

**Persistence** — Buffer files use UTF-8 with BOM (`new UTF8Encoding(true)`). Writes are debounced 300 ms after the last keystroke; never write on every keystroke. On exit, flush any pending debounced write synchronously before the process ends. A missing file on startup is an empty buffer — do not throw.

**Settings** — Non-buffer state lives in a single `settings.json`. Do not use the registry. "Launch on startup" is **not** a registry run-key — QuinSlate is MSIX-packaged, so it uses the `windows.startupTask` manifest extension plus the `Windows.ApplicationModel.StartupTask` API (registry run-key writes get virtualized and never run; see `Docs/Specs/02-STARTUP.md`).

**Single instance** — Enforce via named mutex before hotkey registration. The existing instance must respond to a second launch by surfacing the panel.

**Hotkey IDs** — Keep all hotkey ID constants in one place. Duplicate IDs across instances cause silent failures; this is why single-instance enforcement must
run first.

---

## UI controls

Always use modern WinUI 3 controls. Some examples, documentation, and a comprehensive list of all available controls can be found in the [WinUI Gallery repository](https://github.com/microsoft/WinUI-Gallery).
Always use the WinUI 3 Expert agent when working with UI.

---

## Background gradient (dithering)

The window and editor surfaces are painted with a warm, logo-derived **four-corner bilinear
gradient mesh** (amber `#F2900F` / warm grey `#98948D`). Each corner of the surface has its own
colour and every pixel is the bilinear blend of the four; the top-right corner carries a faint
"whisper of amber" warmth while the rest stay near-neutral, giving dim, organic, non-linear depth
rather than a flat ramp. A plain XAML `LinearGradientBrush` is **8-bit per channel and does not
dither**, so on this dark, low-contrast field it shows visible "false-contour" banding lines.
Acrylic/Mica hide this with their built-in noise layer, but those backdrops were removed.

The fix is `DitheredGradientBrushFactory` (`Components/`). It computes the mesh colour **per pixel
in floating point** (bilinear across the four corners) and adds **triangular-PDF (TPDF) noise of ±1
quantization level before rounding** to 8-bit, then writes the pixels into a `WriteableBitmap`
exposed as an opaque `ImageBrush`.
Dithering *before* the quantization is the crucial part — it makes pixels near a band boundary
round up/down at random in proportion to the sub-level fraction, smearing the boundary away. (Note
what does **not** work: rendering an already-8-bit gradient and adding noise *on top* — the band
edges are already baked in, so the contours stay and you just get grain. Direct2D's own gradient
dithering via a high-precision stop collection was also too weak here. Both were tried.)

Rules when touching this:

- **Single source of truth for the colours: the `AppGradient{Start,End,CornerTR,CornerBL}{Dark,Light}`
  `Color` resources in `App.xaml`.** `Start`/`End` are the diagonal endpoints (top-left /
  bottom-right corners); `CornerTR`/`CornerBL` are the other two mesh corners. Change the gradient
  there and nowhere else. Everything else derives from them: `DitheredGradientBrushFactory` reads all
  four corners by key at runtime (the brush actually shown), the XAML fallback brushes
  (`AppBackgroundGradient` in `App.xaml`, `TextControlBackground*` in `BufferPanelResources.xaml`)
  reference `Start`/`End` via `ThemeResource`, and MainWindow's flash fill reads them via
  `DitheredGradientBrushFactory.MidColor` (the average of the four corners). (XAML has no `x:Static`
  and code cannot reliably read theme-keyed brushes, so the colours live in XAML resources and C#
  reads them by key.)
- The brush is **opaque** — required so the native text caret stays visible and ClearType keeps
  working (a transparent editor surface hides the caret).
- The XAML `AppBackgroundGradient` / `TextControlBackground*` brushes are only a deep **fallback**
  (used if even the code path below cannot run). They are linear gradients and **must never be the
  surface actually presented**: on this dark, low-contrast field they band, and the window vs.
  editor gradients meet in a visible seam. To avoid a banded flash before the dithered mesh is
  ready, `BufferPanel.ApplyFallbackBackground` paints the window and editors with the flat
  `DitheredGradientBrushFactory.MidColor` **synchronously in `Initialise`, before the window is
  first shown**, so the first composited frame is a uniform flat tone. The dithered mesh swaps in
  on load; flat-to-mesh is imperceptible because the mesh barely deviates from its mid-tone.
- The dithered brush is applied in code on `Loaded`, and rebuilt on `ActualThemeChanged` and on
  resize (`BufferPanel` debounces the resize rebuild; `TrayPeekPanel` is fixed-size). It overrides
  `TextControlBackground*` in each editor's own resource scope so the focus/hover visual states
  stay dithered, and re-enters the editor's visual state after applying (the focused state pins the
  background via `ThemeResource` when entered and won't otherwise pick up the swapped brush).
- **The window and editor meshes must swap in together (all-or-nothing), never the window alone.**
  At startup the panel's `Loaded` fires before the TabView has realized the selected tab's content,
  so the active editor has no size and its brush cannot be built yet. Applying the window mesh at
  that point flashes the full-window gradient through the still-unpainted editor area for a few
  frames and then visibly snaps when the editor paints its flat fallback over it (this was the
  "broken gradient on startup" artifact, captured frame-by-frame and fixed). When the editor brush
  cannot be built, `ApplyDitheredBackground` keeps the flat fallback on every surface and schedules
  a one-shot retry for when the editor is laid out (`ScheduleDitheredRetry`). Do not reorder this
  back to "window first, editors when ready".
- **Render at native pixel size, never stretch.** Each surface's bitmap is built at that element's
  DIP size × `XamlRoot.RasterizationScale` and shown 1:1. Dithering is a per-pixel pattern;
  stretching the bitmap blurs it and the 8-bit output re-quantizes, which brings the banding
  straight back — hence per-element sizing and rebuild on resize.
- The mesh is **bilinear** (C0-continuous, no interior creases), so the corner colours can differ
  freely without creating seam lines — the smoothness comes from the interpolation, not from the
  corners being collinear. Keep the per-corner deltas small so the field stays dim and barely
  visible; the dithering removes the banding that the resulting low-contrast ramp would otherwise
  show.

---

## Win32 gotchas

- **Message loop** — `RegisterHotKey` requires a Win32 message loop. Hook into the existing HWND via WindowNative` / `Win32Interop`; do not create a hidden secondary window for this.

- **Tray icon lifetime** — Call `NIM_DELETE` before the referenced window is destroyed, or Windows leaves a ghost icon until the next Explorer restart.

- **Clipboard and STA** — All clipboard operations must run on an STA thread. WinUI 3 is STA by default, but async work that hops threads will throw. Keep clipboard operations synchronous or explicitly marshal back to the UI thread.

---

## Build

To verify compilation during development, run:

```bash
dotnet build QuinSlate.slnx -p:Platform=x64
```

---

## Web editor (CodeMirror 6 bundle)

The buffer editor is a CodeMirror 6 web app hosted in a `WebView2`. Its sources
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
  — vendored and pinned (see ADR `Docs/Decisions/04-EDITOR-CODEMIRROR-WEBVIEW2.md`
  and spec `Docs/Specs/17-EDITOR-CODEMIRROR-MIGRATION.md`). Do not add npm
  packages casually; the "no third-party packages unless absolutely necessary"
  rule applies to the JS side too.
- The page is served to the WebView2 over a virtual-host mapping
  (`https://quinslate.editor/`) from the app's install/output directory; the
  host↔page contract is the JSON bridge in `Components/EditorHost.cs`. **Never
  log buffer text across that bridge** (see the stack note above).
- **The editor's `contentAttributes` must keep `autocorrect: "on"`.** Edge/WebView2
  silently drops the Windows emoji panel's (Win+. / Win+;) insert into any
  contenteditable that combines `autocorrect="off"` with block-level children —
  which CodeMirror always has (`.cm-line` divs). The drop is browser-side: the page
  sees zero DOM events, so no page-side code can detect or fix it. `main.js`
  overrides CM6's built-in `"off"` for exactly this reason (`spellcheck` stays
  `"false"`). Do not "restore" `autocorrect: "off"` for editor hygiene. See
  `Docs/Wiki/03-EDITOR-OS-TEXT-INPUT.md` and
  `Docs/Investigations/05-EMOJI-PANEL-AUTOCORRECT-OFF-DROP.md`.

---

## Testing

After making significant changes, run the tests to verify correctness and catch regressions:

```bash
dotnet test QuinSlate.slnx -p:Platform=x64
```

This must be done before considering any task complete.

---

## Formatting

After **every** task that writes, modifies, or generates any `.cs` file, run:

```bash
dotnet format QuinSlate.slnx
```

This applies to new files and edited files alike. Do not skip it for trivial changes.

---

## Specs

If it is decided during implementation to drift away from the initial requirements, agents should modify the corresponding `Docs/Specs/` `.md` files to reflect the new direction.

---

## Docs conventions

Everything under `Docs/` (`Specs/`, `Investigations/`, `Decisions/`, `Plans/`, `Wiki/`) follows
one naming and stamping convention. Apply it to every new or renamed doc.

- **Filename:** `NN-KEBAB-NAME.md` — a two-digit ordinal prefix, then an UPPERCASE
  kebab-case name, `.md`. No type token in the name (the folder already conveys the type:
  spec / investigation / decision / plan / wiki). The ordinal is per-folder and orders the files;
  `00-` is reserved for a folder's index/queue (e.g. `Specs/00-FEATURE-QUEUE.md`,
  `Wiki/00-INDEX.md`). Pick the next free number in that folder. Investigations and plans are
  numbered chronologically.
- **Date stamp:** every doc opens with its H1 title immediately followed by a
  `> _Last updated: YYYY-MM-DD_` blockquote. Refresh that date (ISO `YYYY-MM-DD`) whenever
  you edit the doc.
- **Cross-links:** reference other docs by their current filename. When you rename a doc,
  update every link to it (other docs, `CLAUDE.md`, and code comments).

---

## Temporary code

All temporary code, experimental scripts, or one-off tests go in `Scratch/` only. This folder is excluded from source control and is never referenced by any project.
