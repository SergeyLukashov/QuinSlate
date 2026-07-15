# Checkable tasks

> _Last updated: 2026-07-15_

The first formatting feature of the CodeMirror editor: a line can be a
**task** — a clickable checkbox followed by text — created and managed the way
Notion does it, with no toolbar or UI chrome. Everything lives page-side in the
web editor bundle (`QuinSlate.Ui/WebEditor/build/src/tasks.js` + `editor.css`);
no host/bridge changes.

## Text representation

- A task is a line starting with the GitHub-flavored-markdown marker
  `- [ ] ` (unchecked) or `- [x] ` (checked; uppercase `X` is recognised,
  lowercase is written).
- The marker text **is** the persisted format: it is what lands in the buffer
  `.txt` file, what copy/paste produces, and what reloads recognise. There is
  no task state outside the document text.
- Recognition applies anywhere the marker appears — typed, loaded from disk,
  pasted, or restored by undo.

## Behaviour

| Action | Result |
|---|---|
| Type `[] ` or `[ ] ` at the start of a line | Line converts to an unchecked task; any text after the caret becomes the task's content |
| Enter on a task line (caret in the content) | New line continues the list as a fresh **unchecked** task; splitting mid-content carries the remainder into the new task |
| Enter on an empty task | The marker is removed — the line becomes plain and empty (exits the list) |
| Enter at the very start of a task line | Default newline: the task is pushed down unchanged |
| Click the checkbox | Toggles checked/unchecked |
| Ctrl+Enter with the caret on a task line | Toggles checked/unchecked |
| Backspace at the start of the task content | Deletes the whole marker (it is atomic); the line reverts to plain text |

## Rendering

- The six marker characters are replaced by a checkbox widget
  (`.cm-task-checkbox`); the caret can never sit inside the marker.
- Unchecked: an empty rounded square whose border derives from the theme text
  colour. Checked: filled with the system accent (`--accent`, sent by the host
  in the existing `theme` message) with a white check glyph.
- A checked task's text dims to 55 % opacity (`.cm-task-done`). **No
  strikethrough** — dimmed-only by decision.

## Non-goals (for now)

- No nesting/indentation of tasks.
- No wrapped-line hanging indent; wrapped task text returns to column 0 like
  any other line.
- No task counts, filtering, or "move checked to bottom" behaviours.
