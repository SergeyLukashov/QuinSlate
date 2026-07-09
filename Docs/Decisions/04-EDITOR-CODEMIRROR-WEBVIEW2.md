# ADR 0004: Buffer editor replaced with CodeMirror 6 hosted in WebView2

> _Last updated: 2026-07-08_

## Status
Accepted — 2026-07-08

## Context
The buffer editor is a WinUI 3 `RichEditBox`. It has proven to be the weakest
component in the app:

- **Rendering ceiling.** `RichEditBox` renders its entire document into one
  composition surface with no virtualization and stops painting glyphs past a
  fixed rendered height (~260k device pixels observed). Text past the ceiling
  is present and selectable but invisible. This is a documented platform
  limitation ([microsoft-ui-xaml#1842](https://github.com/microsoft/microsoft-ui-xaml/issues/1842)),
  mitigated — not fixed — by capping buffers at 50,000 chars
  (see [Docs/Investigations/03-RICHEDITBOX-TALL-DOCUMENT-RENDER-CEILING.md](../Investigations/03-RICHEDITBOX-TALL-DOCUMENT-RENDER-CEILING.md)).
  The cap was a workaround; the product wants genuinely long buffers (multi-MB).
- **Weak text shaping.** RichEdit's legacy font fallback drops the VS16
  variation selector on text-presentation-default emoji (🅰️ 🅱️ 🅾️ render
  monochrome or as tofu in the body and tab titles, while the emoji picker
  shows them in color).
- **Limited formatting surface.** The calc-result highlight is squeezed
  through `ITextCharacterFormat.BackgroundColor`, which ignores alpha and
  forces RGB-interpolation hacks ([Docs/Specs/12-CALC-RESULT-ANIMATION.md](../Specs/12-CALC-RESULT-ANIMATION.md)).
- **Dead end for the roadmap.** Planned richer content (checkable task lists,
  clickable links) is impractical to build on RichEdit.

Native alternatives were assessed and rejected:

## Alternatives considered
- **Plain `TextBox`** — different XAML text stack without the render ceiling
  at current sizes, zero dependencies. Rejected: no per-run formatting (kills
  the calc highlight), no line-spacing control, still not virtualized (fails
  the multi-MB goal), dead end for rich content.
- **WinUIEdit (Scintilla port)** — architecturally right (native, virtualized,
  mature engine) but the WinUI wrapper is 0.0.4-prerelease and self-declared
  not production ready, with open issues for mixed-DPI blurriness, IME
  candidate-window placement, and a rendering crash. Adopting it means
  maintaining a C++/WinRT fork indefinitely. Rejected.
- **TextControlBox.WinUI** — active, C#, MIT, but no word wrap (README: "I
  have no idea how to implement this properly atm"), no CJK IME, no
  accessibility. Rejected.
- **Monaco in WebView2** — mature, but a *code* editor: no replace-decorations
  (cannot render a text range as an interactive widget), heavy, fixed
  line-grid, code-oriented chrome. Wrong model for notes. Rejected.
- **Lexical / ProseMirror (WYSIWYG)** — strong rich-text model but weak at
  multi-MB documents and requires abandoning plain-text persistence. Rejected.

## Decision
Replace the `RichEditBox` buffer editor with **CodeMirror 6** running in a
single **WebView2** control (the `Microsoft.UI.Xaml.Controls.WebView2` control
that ships inside Windows App SDK — no new NuGet package for the host side).

Rationale:

- CM6 virtualizes rendering and is proven on very large documents (it is the
  editor inside Obsidian, Replit, and Chrome DevTools) — the render ceiling
  and, later, the 50k cap disappear.
- Documents stay **plain text**, so the `.txt` persistence layer, debounced
  writes, exports, and the tray-peek preview pipeline survive unchanged.
- Chromium text shaping fixes the VS16 emoji rendering, grapheme-cluster caret
  movement, and IME as a side effect; accessibility comes from Chromium's UIA
  bridge rather than hand-rolled peers.
- CM6 decorations (CSS-backed, replace-capable) give the calc highlight a
  first-class implementation now and are the enabling mechanism for the
  planned task lists / links later (out of scope here — see the migration
  spec).
- QuinSlate is a resident tray app: the WebView2 environment is created once
  at startup and kept alive, so Chromium's cold-start cost is paid at login,
  not on panel show.

CodeMirror 6 is a third-party **JavaScript** dependency — a second deliberate,
scoped exception to the "no third-party packages" rule (after Serilog,
[01-LOGGING-SERILOG.md](01-LOGGING-SERILOG.md)). It is vendored, not floating:
pinned versions, built into a single bundled artifact that is checked into the
repository together with the lock file and a rebuild script, so building
QuinSlate never requires npm.

The migration itself is strictly **like-for-like**: no new features, no raised
cap, no format changes, and no functional or visual regression. The full
behavioural contract lives in
[Docs/Specs/17-EDITOR-CODEMIRROR-MIGRATION.md](../Specs/17-EDITOR-CODEMIRROR-MIGRATION.md).

## Consequences
- **Memory.** Chromium adds roughly 100–150 MB of resident working set to an
  always-running tray app. Accepted deliberately as the price of the only
  mature editor stack that meets the requirements.
- **Runtime dependency.** The app now requires the WebView2 Evergreen Runtime
  (preinstalled on Windows 11, serviced to Windows 10 via Windows Update).
  Creation failure must degrade gracefully, never crash.
- **New asset pipeline.** A vendored, pinned CM6 bundle plus editor page under
  `QuinSlate.Ui/WebEditor/`, served to the WebView via
  `SetVirtualHostNameToFolderMapping`. Fully offline; no network access.
- **Privacy surface.** Buffer text now crosses a host↔web bridge. Bridge
  traffic is never logged (the "never log buffer contents" rule extends to the
  JS side), DevTools are disabled in Release, and Chromium spellcheck/autofill
  are off.
- **Retired code.** `EditorViewBuilder`, `SmoothScrollController`,
  `EditorPaste`, the RichEdit halves of `CalcResultAnimator` and
  `EditorContextMenu`, and `TruncateToMaxLength` are replaced by web-side
  equivalents; investigation 03's ceiling becomes moot (the doc stays as
  history).
- **Unlocked follow-ups (not in this change):** raising/removing
  `MaxBufferLength`, checkable task lists, clickable links, richer calc
  affordances.
- CLAUDE.md's stack table and the affected specs are updated as part of the
  implementation (see the migration spec's documentation section).
