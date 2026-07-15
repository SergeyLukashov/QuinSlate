// Checkable tasks (Docs/Specs/19-CHECKABLE-TASKS.md). A line starting with
// "- [ ] " / "- [x] " is a task: the marker text stays in the document (and so
// in the buffer file) and renders as a clickable checkbox wherever it appears —
// typed, loaded, or pasted. Typing "[] " or "[ ] " at the start of a line
// converts it to a task; Enter continues the list; Enter on an empty task
// removes the marker; the checkbox click or Ctrl+Enter toggles done. The
// marker is atomic, so Backspace after it deletes it whole, turning the line
// back into plain text.

import { EditorView, keymap, Decoration, ViewPlugin, WidgetType } from "@codemirror/view";

const TASK_MARKER_PATTERN = /^- \[( |x|X)\] /;
const TASK_MARKER_UNCHECKED = "- [ ] ";
const TASK_MARKER_LENGTH = TASK_MARKER_UNCHECKED.length;
const TASK_STATE_CHAR_OFFSET = 3; // index of the " "/"x" state char inside the marker
const TASK_SHORTHAND_PREFIXES = ["[]", "[ ]"];

// Returns { checked } when the line carries the task marker, null otherwise.
function taskStateOfLine(line) {
  const match = TASK_MARKER_PATTERN.exec(line.text);
  if (match == null) {
    return null;
  }
  return { checked: match[1] !== " " };
}

class TaskCheckboxWidget extends WidgetType {
  constructor(checked) {
    super();
    this.checked = checked;
  }

  eq(other) {
    return other.checked === this.checked;
  }

  toDOM() {
    const box = document.createElement("span");
    box.className = this.checked ? "cm-task-checkbox cm-task-checked" : "cm-task-checkbox";
    box.setAttribute("aria-hidden", "true");
    return box;
  }

  ignoreEvent() {
    // Let mousedown reach the plugin's handler below, which performs the toggle.
    return false;
  }
}

function toggleTaskAt(view, pos) {
  const line = view.state.doc.lineAt(pos);
  const state = taskStateOfLine(line);
  if (state == null) {
    return false;
  }
  const statePos = line.from + TASK_STATE_CHAR_OFFSET;
  view.dispatch({
    changes: { from: statePos, to: statePos + 1, insert: state.checked ? " " : "x" },
    userEvent: "input",
  });
  return true;
}

// Two decoration sets per viewport pass: the checkbox widgets (also the atomic
// ranges, so the caret never lands inside a marker and Backspace removes it
// whole) and the dim marks on checked task text. Keeping them separate stops
// the dim marks from becoming atomic themselves.
function buildTaskDecorations(view) {
  const checkboxes = [];
  const dimmed = [];
  for (const range of view.visibleRanges) {
    let pos = range.from;
    while (pos <= range.to) {
      const line = view.state.doc.lineAt(pos);
      const state = taskStateOfLine(line);
      if (state != null) {
        const contentFrom = line.from + TASK_MARKER_LENGTH;
        checkboxes.push(
          Decoration.replace({ widget: new TaskCheckboxWidget(state.checked) }).range(line.from, contentFrom)
        );
        if (state.checked && line.to > contentFrom) {
          dimmed.push(Decoration.mark({ class: "cm-task-done" }).range(contentFrom, line.to));
        }
      }
      pos = line.to + 1;
    }
  }
  return { checkboxes: Decoration.set(checkboxes), dimmed: Decoration.set(dimmed) };
}

export const taskPlugin = ViewPlugin.fromClass(
  class {
    constructor(view) {
      const built = buildTaskDecorations(view);
      this.checkboxes = built.checkboxes;
      this.dimmed = built.dimmed;
    }

    update(update) {
      if (update.docChanged || update.viewportChanged) {
        const built = buildTaskDecorations(update.view);
        this.checkboxes = built.checkboxes;
        this.dimmed = built.dimmed;
      }
    }
  },
  {
    decorations: (value) => value.checkboxes,
    provide: (plugin) => [
      EditorView.decorations.of((view) => {
        const value = view.plugin(plugin);
        return value == null ? Decoration.none : value.dimmed;
      }),
      EditorView.atomicRanges.of((view) => {
        const value = view.plugin(plugin);
        return value == null ? Decoration.none : value.checkboxes;
      }),
    ],
    eventHandlers: {
      mousedown(event, view) {
        const target = event.target;
        if (target instanceof Element && target.classList.contains("cm-task-checkbox")) {
          return toggleTaskAt(view, view.posAtDOM(target));
        }
        return false;
      },
    },
  }
);

// Space keymap: converting the Notion shorthand. Runs only when everything
// from the line start to the caret is exactly "[]" or "[ ]"; any text after
// the caret becomes the task's content. Otherwise the space inserts as usual.
function convertTaskShorthand(view) {
  const selection = view.state.selection.main;
  if (!selection.empty) {
    return false;
  }
  const line = view.state.doc.lineAt(selection.head);
  const beforeCaret = view.state.doc.sliceString(line.from, selection.head);
  if (TASK_SHORTHAND_PREFIXES.indexOf(beforeCaret) < 0) {
    return false;
  }
  view.dispatch({
    changes: { from: line.from, to: selection.head, insert: TASK_MARKER_UNCHECKED },
    selection: { anchor: line.from + TASK_MARKER_LENGTH },
    userEvent: "input.type",
    scrollIntoView: true,
  });
  return true;
}

// Enter on a task line: an empty task exits the list (the marker is removed),
// any other position continues it with a fresh unchecked task. Enter at the
// very start of the line (before the checkbox) falls through to the default
// newline, which pushes the task down — matching Notion.
function handleTaskEnter(view) {
  const selection = view.state.selection.main;
  if (!selection.empty) {
    return false;
  }
  const line = view.state.doc.lineAt(selection.head);
  if (taskStateOfLine(line) == null) {
    return false;
  }
  if (line.text.length === TASK_MARKER_LENGTH) {
    view.dispatch({
      changes: { from: line.from, to: line.to },
      userEvent: "delete",
    });
    return true;
  }
  if (selection.head < line.from + TASK_MARKER_LENGTH) {
    return false;
  }
  view.dispatch(view.state.replaceSelection("\n" + TASK_MARKER_UNCHECKED), {
    userEvent: "input.type",
    scrollIntoView: true,
  });
  return true;
}

function toggleTaskAtCaret(view) {
  return toggleTaskAt(view, view.state.selection.main.head);
}

export const taskKeymap = keymap.of([
  { key: "Enter", run: handleTaskEnter },
  { key: "Mod-Enter", run: toggleTaskAtCaret },
  { key: "Space", run: convertTaskShorthand },
]);
