// Checkable tasks (Docs/Specs/19-CHECKABLE-TASKS.md). A line starting with
// "- [ ] " / "- [x] " is a task: the marker text stays in the document (and so
// in the buffer file) and renders as a clickable checkbox wherever it appears —
// typed, loaded, or pasted. Typing "[] " or "[ ] " at the start of a line
// converts it to a task; Enter continues the list; Enter on an empty task
// removes the marker; the checkbox click or Ctrl+Enter toggles done. A task can
// be nested with Tab (indent.js); its depth is the line's leading spaces
// and is part of the marker prefix, so the marker is atomic indent and all:
// Backspace after it deletes the whole prefix, turning the line back into plain
// text at column 0.

import { EditorView, keymap, Decoration, ViewPlugin, WidgetType } from "@codemirror/view";
import {
  parseItem,
  indentText,
  renderDepth,
  splitShorthand,
  ITEM_KIND_TASK,
  TASK_MARKER_UNCHECKED,
  TASK_STATE_CHAR_OFFSET,
} from "./listItems.js";

const TASK_SHORTHAND_PREFIXES = ["[]", "[ ]"];
const TASK_CHECKED_CHAR = "x";
const TASK_UNCHECKED_CHAR = " ";
const DEPTH_PROPERTY = "--list-depth";

// Returns the item descriptor when the line carries the task marker, null otherwise.
function taskOfLine(line) {
  const item = parseItem(line.text);
  return item != null && item.kind === ITEM_KIND_TASK ? item : null;
}

class TaskCheckboxWidget extends WidgetType {
  constructor(checked, depth) {
    super();
    this.checked = checked;
    this.depth = depth;
  }

  eq(other) {
    return other.checked === this.checked && other.depth === this.depth;
  }

  toDOM() {
    const box = document.createElement("span");
    box.className = this.checked ? "cm-task-checkbox cm-task-checked" : "cm-task-checkbox";
    box.style.setProperty(DEPTH_PROPERTY, String(this.depth));
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
  const task = taskOfLine(line);
  if (task == null) {
    return false;
  }
  const statePos = line.from + task.indentLength + TASK_STATE_CHAR_OFFSET;
  view.dispatch({
    changes: {
      from: statePos,
      to: statePos + 1,
      insert: task.checked ? TASK_UNCHECKED_CHAR : TASK_CHECKED_CHAR,
    },
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
      const task = taskOfLine(line);
      if (task != null) {
        const contentFrom = line.from + task.prefixLength;
        const widget = new TaskCheckboxWidget(task.checked, renderDepth(task.depth));
        checkboxes.push(Decoration.replace({ widget }).range(line.from, contentFrom));
        if (task.checked && line.to > contentFrom) {
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

// Space keymap: converting the Notion shorthand. Runs only when everything from
// the line start to the caret is exactly "[]" or "[ ]", optionally behind an
// indent — the task then converts at the depth it was typed at. Any text after
// the caret becomes the task's content. Otherwise the space inserts as usual.
export function convertTaskShorthand(view) {
  const selection = view.state.selection.main;
  if (!selection.empty) {
    return false;
  }
  const line = view.state.doc.lineAt(selection.head);
  const typed = splitShorthand(view.state.doc.sliceString(line.from, selection.head));
  if (TASK_SHORTHAND_PREFIXES.indexOf(typed.shorthand) < 0) {
    return false;
  }
  const prefix = indentText(typed.depth) + TASK_MARKER_UNCHECKED;
  view.dispatch({
    changes: { from: line.from, to: selection.head, insert: prefix },
    selection: { anchor: line.from + prefix.length },
    userEvent: "input.type",
    scrollIntoView: true,
  });
  return true;
}

// Enter on a task line: an empty task exits the list at any depth (marker and
// indent both go, leaving a plain empty line), any other position continues it
// with a fresh unchecked task at the same depth. Enter at the very start of the
// line (before the checkbox) falls through to the default newline, which pushes
// the task down — matching Notion.
export function handleTaskEnter(view) {
  const selection = view.state.selection.main;
  if (!selection.empty) {
    return false;
  }
  const line = view.state.doc.lineAt(selection.head);
  const task = taskOfLine(line);
  if (task == null) {
    return false;
  }
  if (line.text.length === task.prefixLength) {
    view.dispatch({
      changes: { from: line.from, to: line.to },
      userEvent: "delete",
    });
    return true;
  }
  if (selection.head < line.from + task.prefixLength) {
    return false;
  }
  view.dispatch(
    view.state.replaceSelection("\n" + indentText(task.depth) + TASK_MARKER_UNCHECKED),
    {
      userEvent: "input.type",
      scrollIntoView: true,
    }
  );
  return true;
}

export function toggleTaskAtCaret(view) {
  return toggleTaskAt(view, view.state.selection.main.head);
}

export const taskKeymap = keymap.of([
  { key: "Enter", run: handleTaskEnter },
  { key: "Mod-Enter", run: toggleTaskAtCaret },
  { key: "Space", run: convertTaskShorthand },
]);
