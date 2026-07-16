# Bullet and numbered lists

> _Last updated: 2026-07-16_

The second formatting feature of the CodeMirror editor, alongside
[checkable tasks](19-CHECKABLE-TASKS.md): a line can be a **bullet item** or a
**numbered item**, created and managed the way Notion does it, with no toolbar
or UI chrome. Everything lives page-side in the web editor bundle
(`QuinSlate.Ui/WebEditor/build/src/lists.js` + `editor.css`); no host/bridge
changes.

Nesting is shared with tasks: `build/src/listItems.js` recognises all three item
kinds and their depth, and `build/src/indent.js` owns the Tab/Shift+Tab
commands, so a bullet nests under a task (or vice versa) with no special cases.
The rules are specified once, in
[19-CHECKABLE-TASKS.md § Nesting](19-CHECKABLE-TASKS.md#nesting); only what is
particular to bullets and numbers is repeated here.

## Text representation

- A bullet item is a line starting with `- ` (a dash and a space). A `- [ ] ` /
  `- [x] ` line is a **task**, not a bullet — tasks own that prefix.
- A numbered item is a line starting with `<n>. ` — one or more digits, a dot,
  and a space (e.g. `1. `, `2. `).
- **Nesting depth is the line's leading spaces — two per level**, in front of
  the marker (`  - child`, `  1. child`).
- The marker text **is** the persisted format: it is what lands in the buffer
  `.txt` file, what copy/paste produces, and what reloads recognise. There is
  no list state — depth included — outside the document text.
- Recognition applies anywhere a marker appears — typed, loaded from disk,
  pasted, or restored by undo.

## Behaviour

| Action | Result |
|---|---|
| Type `- ` at the start of a line | Line converts to a bullet item; any text after the caret becomes the item's content. Typed behind an indent, it converts at that depth |
| Type `1. ` at the start of a line | Line converts to a numbered item; a numbered run always starts at 1 (only `1. ` is the trigger) |
| Enter on a list item (caret in the content) | New line continues the same kind of list **at the same depth** — a fresh bullet, or the next number; splitting mid-content carries the remainder into the new item |
| Enter on an empty item | Marker and indent are both removed — the line becomes plain and empty at column 0 (exits the list), at any depth |
| Enter at the very start of a list line | Default newline: the item is pushed down unchanged |
| Backspace at the start of the item content | Deletes the whole prefix — indent and marker together (they are atomic); the line reverts to plain text at column 0 |
| **Tab** / **Shift+Tab** with the caret on an item | Nests / un-nests it one level — see [19-CHECKABLE-TASKS.md § Nesting](19-CHECKABLE-TASKS.md#nesting) |
| Insert / delete / exit anywhere in a numbered run | Every contiguous numbered run stays sequential from 1, per depth (auto-renumber) |

## Rendering

- The marker characters **and the indent spaces in front of them** are replaced
  by one widget; the caret can never sit inside the prefix.
- A bullet renders as a drawn dot (`.cm-list-bullet`, filled with the theme
  text colour — no font glyph). A numbered item renders the number followed by
  a dot (`.cm-list-number`), right-aligned in a fixed column so single- and
  double-digit items share the same content indent.
- Both reserve the same 23px indent as the task checkbox, so bullets, tasks,
  and numbered items line up — and one depth level is one more such column, so a
  child's marker lands under where its parent's text starts.
- **The marker does not change with depth**: a dot is a dot at every level, and
  numbers stay `1.`, `2.`, `3.` rather than rotating to `a.` / `i.` as Notion
  does. Chosen so the rendered list matches the `.txt` on disk exactly.

## Renumbering

- A `transactionFilter` (`listRenumber`) keeps every contiguous run of numbered
  items sequential from 1. Any digits that disagree with an item's position in
  its run are rewritten and appended to the **same** transaction, so the fix is
  one undo step and the caret maps through it.
- **Counters are stacked by depth**, so nesting never disturbs the run it hangs
  off: a parent run resumes its count after its children, and a re-entered
  nested run restarts at 1.
- A bullet or task breaks the numbered run **at its own depth only**. A plain or
  blank line breaks every run; the count restarts at 1 on the next numbered
  line.
- Host-origin transactions (loads, host inserts) are left untouched: loaded
  text renders with its stored digits and is only renumbered once the user
  edits it.

## Non-goals (for now)

- No `* ` / `+ ` bullet aliases — only `- `.
- No custom numbered-list start value; a run always starts at 1.
- No wrapped-line hanging indent; wrapped list text returns to column 0.
- No depth-varying markers (see Rendering) and no collapsing/folding of a
  nested item's subtree.
