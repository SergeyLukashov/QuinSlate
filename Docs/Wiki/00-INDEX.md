# Wiki index

> _Last updated: 2026-07-14_

Durable, cross-cutting knowledge that is not a spec, a decision, or the record of a single
investigation: platform behaviour worth knowing before you touch an area, and techniques that took
real effort to get right.

Use the other folders when they fit better:

- **`Specs/`** — what the product does.
- **`Decisions/`** — a choice made, its alternatives, and why (ADRs).
- **`Investigations/`** — the record of one bug hunt, symptom to fix.
- **`Plans/`** — implementation plans for multi-step work.
- **`Wiki/`** — the distilled residue of the above: "read this before you change X".

---

| # | Page | Read it before |
|---|------|----------------|
| 01 | [TabView drag-reorder: the four SDK traps](01-TABVIEW-DRAG-REORDER.md) | touching the tab strip, its sizing, or the `TabViewItem` template |
| 02 | [Verifying UI behaviour by driving the packaged app](02-SYNTHETIC-INPUT-VERIFICATION.md) | trying to confirm a mid-gesture or transient UI defect |
| 03 | [OS text-services input: keep `autocorrect="on"`](03-EDITOR-OS-TEXT-INPUT.md) | touching the editor's `contentAttributes`, or any text input that isn't typing |
| 04 | [Docs conventions](04-DOCS-CONVENTIONS.md) | creating or renaming any doc under `Docs/` |
| 05 | [Background gradient: the TPDF-dithered four-corner mesh](05-BACKGROUND-GRADIENT-DITHERING.md) | touching the window/editor background, `DitheredGradientBrushFactory`, or the `AppGradient*` resources |
| 06 | [Web editor: the CodeMirror 6 bundle and its build](06-WEB-EDITOR-BUNDLE.md) | touching anything under `QuinSlate.Ui/WebEditor/` or the `EditorHost` bridge |
| 07 | [Win32 gotchas](07-WIN32-GOTCHAS.md) | touching `Interop/`, the tray icon, hotkeys, or the clipboard |
