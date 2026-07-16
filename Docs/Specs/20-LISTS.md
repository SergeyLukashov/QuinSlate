# Bullet and numbered lists

> _Last updated: 2026-07-15_

The second formatting feature of the CodeMirror editor, alongside
[checkable tasks](19-CHECKABLE-TASKS.md): a line can be a **bullet item** or a
**numbered item**, created and managed the way Notion does it, with no toolbar
or UI chrome. Everything lives page-side in the web editor bundle
(`QuinSlate.Ui/WebEditor/build/src/lists.js` + `editor.css`); no host/bridge
changes.

## Text representation

- A bullet item is a line starting with `- ` (a dash and a space). A `- [ ] ` /
  `- [x] ` line is a **task**, not a bullet — tasks own that prefix.
- A numbered item is a line starting with `<n>. ` — one or more digits, a dot,
  and a space (e.g. `1. `, `2. `).
- The marker text **is** the persisted format: it is what lands in the buffer
  `.txt` file, what copy/paste produces, and what reloads recognise. There is
  no list state outside the document text.
- Recognition applies anywhere a marker appears — typed, loaded from disk,
  pasted, or restored by undo.

## Behaviour

| Action | Result |
|---|---|
| Type `- ` at the start of a line | Line converts to a bullet item; any text after the caret becomes the item's content |
| Type `1. ` at the start of a line | Line converts to a numbered item; a numbered list always starts at 1 (only `1. ` is the trigger) |
| Enter on a list item (caret in the content) | New line continues the same kind of list — a fresh bullet, or the next number; splitting mid-content carries the remainder into the new item |
| Enter on an empty item | The marker is removed — the line becomes plain and empty (exits the list) |
| Enter at the very start of a list line | Default newline: the item is pushed down unchanged |
| Backspace at the start of the item content | Deletes the whole marker (it is atomic); the line reverts to plain text |
| Insert / delete / exit anywhere in a numbered run | The whole contiguous numbered run stays sequential from 1 (auto-renumber) |

## Rendering

- The marker characters are replaced by a widget; the caret can never sit
  inside the marker.
- A bullet renders as a drawn dot (`.cm-list-bullet`, filled with the theme
  text colour — no font glyph). A numbered item renders the number followed by
  a dot (`.cm-list-number`), right-aligned in a fixed column so single- and
  double-digit items share the same content indent.
- Both reserve the same 23px indent as the task checkbox, so bullets, tasks,
  and numbered items line up.

## Renumbering

- A `transactionFilter` (`listRenumber`) keeps every contiguous run of numbered
  items sequential from 1. Any digits that disagree with an item's position in
  its run are rewritten and appended to the **same** transaction, so the fix is
  one undo step and the caret maps through it.
- A non-numbered line (a bullet, a task, plain text, or a blank line) breaks a
  run; the count restarts at 1 on the next numbered line.
- Host-origin transactions (loads, host inserts) are left untouched: loaded
  text renders with its stored digits and is only renumbered once the user
  edits it.

## Non-goals (for now)

- No nesting/indentation of lists (matching the checkable-tasks non-goals).
- No `* ` / `+ ` bullet aliases — only `- `.
- No custom numbered-list start value; a run always starts at 1.
- No wrapped-line hanging indent; wrapped list text returns to column 0.
