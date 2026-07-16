# Checkable tasks

> _Last updated: 2026-07-16_

The first formatting feature of the CodeMirror editor: a line can be a
**task** — a clickable checkbox followed by text — created and managed the way
Notion does it, with no toolbar or UI chrome. Everything lives page-side in the
web editor bundle (`QuinSlate.Ui/WebEditor/build/src/tasks.js` + `editor.css`);
no host/bridge changes.

Nesting is shared with bullet and numbered lists ([20-LISTS.md](20-LISTS.md)):
`build/src/listItems.js` recognises all three item kinds and their depth, and
`build/src/indent.js` owns the Tab/Shift+Tab commands, so a task nests under a
bullet (or vice versa) with no special cases. Those same commands indent plain
lines — depth is one unit of leading spaces whether or not the line is an item —
so the whole Tab contract is specified in
[17-EDITOR-CODEMIRROR-MIGRATION.md](17-EDITOR-CODEMIRROR-MIGRATION.md); only the
list-specific guardrails are below.

## Text representation

- A task is a line starting with the GitHub-flavored-markdown marker
  `- [ ] ` (unchecked) or `- [x] ` (checked; uppercase `X` is recognised,
  lowercase is written).
- **Nesting depth is the line's leading spaces — two per level**, in front of
  the marker (`  - [ ] child`). Depth floors the raw indent, so hand-edited or
  pasted spacing never invents a half-level; the next Tab rewrites it to whole
  levels.
- The marker text **is** the persisted format: it is what lands in the buffer
  `.txt` file, what copy/paste produces, and what reloads recognise. There is
  no task state — depth included — outside the document text.
- Recognition applies anywhere the marker appears — typed, loaded from disk,
  pasted, or restored by undo.

## Behaviour

| Action | Result |
|---|---|
| Type `[] ` or `[ ] ` at the start of a line | Line converts to an unchecked task; any text after the caret becomes the task's content. Typed behind an indent, it converts at that depth |
| Enter on a task line (caret in the content) | New line continues the list as a fresh **unchecked** task **at the same depth**; splitting mid-content carries the remainder into the new task |
| Enter on an empty task | Marker and indent are both removed — the line becomes plain and empty at column 0 (exits the list), at any depth |
| Enter at the very start of a task line | Default newline: the task is pushed down unchanged |
| Click the checkbox | Toggles checked/unchecked |
| Ctrl+Enter with the caret on a task line | Toggles checked/unchecked |
| Backspace at the start of the task content | Deletes the whole prefix — indent and marker together (they are atomic); the line reverts to plain text at column 0 |
| **Tab** with the caret on a task | Nests it one level under the item above (see Nesting) |
| **Shift+Tab** with the caret on a task | Un-nests it one level; a no-op at depth 0 |

## Nesting

Tab/Shift+Tab shift depth by one level, following Notion's two rules:

- **An item can only nest under an item directly above it, and only one level
  deeper than that item.** Tab is a no-op when the line above is plain text or
  blank (nothing to nest under), when the item is the first line of the buffer,
  or when the shift would skip a level. Depth is capped at 8 (`MAX_DEPTH`).
- **An item's children move with it.** Tab on a parent shifts its whole subtree,
  so a parent never leaves its children behind at a depth that would silently
  re-read as a sibling. Outdenting is the mirror: a following sibling at the old
  depth naturally becomes the outdented item's child, because depth is
  positional.

With a multi-line selection, every line it touches shifts by one level — plain
lines included — plus any subtree trailing the last of them. The guardrail is
checked **once, against the first line of the block**, and the whole block shifts
together or not at all, so relative nesting inside it is preserved. Two
consequences worth knowing:

- A block whose first line is plain indents freely, so a selection that starts on
  a plain line can nest an item below it one level deeper than its surroundings
  would otherwise allow. It renders correctly (hand-edited and pasted text can
  reach any depth anyway); it just is not a depth Tab alone would produce.
- Outdent is refused when the *first* line sits at depth 0, even if later lines
  are deeper. Clamping each line at 0 independently would flatten a top-level
  item's subtree up into it, which is the worse failure.

Each shift is a single transaction, so it is one undo step, and rewriting the
whole indent (rather than adding one unit) normalises stray spacing to whole
levels.

## Rendering

- The marker characters **and the indent spaces in front of them** are replaced
  by one checkbox widget (`.cm-task-checkbox`); the caret can never sit inside
  the prefix.
- Unchecked: an empty rounded square whose border derives from the theme text
  colour. Checked: filled with the system accent (`--accent`, sent by the host
  in the existing `theme` message) with a white check glyph.
- One depth level renders as one extra 23px marker column of widget width, so a
  child's checkbox lands exactly under where its parent's text starts. The
  widget carries its depth in the inline `--list-depth` custom property; the
  pixel step lives in `editor.css` with the rest of the marker metrics. Depth is
  added as width and offset rather than a margin, because the caret and
  empty-line measurements use the widget box and ignore margins.
- A checked task's text dims to 55 % opacity (`.cm-task-done`). **No
  strikethrough** — dimmed-only by decision. Nesting does not cascade the dim:
  checking a parent does not dim or check its children.

## Non-goals (for now)

- No wrapped-line hanging indent; wrapped task text returns to column 0 like
  any other line.
- No task counts, filtering, or "move checked to bottom" behaviours.
- No collapsing/folding of a nested task's subtree.
- Enter on an empty *nested* task exits straight to a plain line rather than
  outdenting a level at a time as Notion does — Shift+Tab is the way to climb
  out. Chosen deliberately, to match the same instant-escape rule Backspace
  follows.
