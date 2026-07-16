// Bullet and numbered lists (Docs/Specs/20-LISTS.md). A line starting with the
// marker "- " is a bullet item; a line starting with "<n>. " is a numbered
// item. The marker text stays in the document (and so in the buffer file) and
// renders as a widget wherever it appears — typed, loaded, or pasted: a "•" dot
// for bullets, the number for ordered items. Typing "- " at the start of a line
// converts it to a bullet; "1. " starts a numbered list; Enter continues the
// list; Enter on an empty item removes the marker; the marker is atomic, so
// Backspace after it deletes it whole, turning the line back into plain text.
// A transaction filter keeps every contiguous numbered run sequential from 1.

import { EditorView, keymap, Decoration, ViewPlugin, WidgetType } from "@codemirror/view";
import { EditorState } from "@codemirror/state";
import { HostOrigin } from "./hostBridge.js";

// A bullet is "- " NOT followed by a task checkbox marker ("- [ ] "/"- [x] "):
// those lines belong to tasks.js and must not be treated as bullets.
const BULLET_MARKER_PATTERN = /^- (?!\[( |x|X)\] )/;
const BULLET_MARKER = "- ";
const BULLET_MARKER_LENGTH = BULLET_MARKER.length;
const NUMBER_MARKER_PATTERN = /^(\d+)\. /;
const BULLET_SHORTHAND = "-";
const NUMBER_SHORTHAND = "1.";

const LIST_KIND_BULLET = "bullet";
const LIST_KIND_NUMBER = "number";

// Returns { kind, markerLength, number } for a list line, or null. `number` is
// the marker's own digits for a numbered line; it is only meaningful there.
function listStateOfLine(line) {
  const numberMatch = NUMBER_MARKER_PATTERN.exec(line.text);
  if (numberMatch != null) {
    return { kind: LIST_KIND_NUMBER, markerLength: numberMatch[0].length, number: numberMatch[1] };
  }
  if (BULLET_MARKER_PATTERN.test(line.text)) {
    return { kind: LIST_KIND_BULLET, markerLength: BULLET_MARKER_LENGTH, number: null };
  }
  return null;
}

class BulletWidget extends WidgetType {
  eq() {
    return true;
  }

  toDOM() {
    const dot = document.createElement("span");
    dot.className = "cm-list-bullet";
    dot.setAttribute("aria-hidden", "true");
    return dot;
  }

  ignoreEvent() {
    return false;
  }
}

class NumberWidget extends WidgetType {
  constructor(number) {
    super();
    this.number = number;
  }

  eq(other) {
    return other.number === this.number;
  }

  toDOM() {
    const label = document.createElement("span");
    label.className = "cm-list-number";
    label.textContent = this.number + ".";
    label.setAttribute("aria-hidden", "true");
    return label;
  }

  ignoreEvent() {
    return false;
  }
}

// One decoration set per viewport pass: the marker widgets, which are also the
// atomic ranges so the caret never lands inside a marker and Backspace removes
// it whole.
function buildListDecorations(view) {
  const markers = [];
  for (const range of view.visibleRanges) {
    let pos = range.from;
    while (pos <= range.to) {
      const line = view.state.doc.lineAt(pos);
      const state = listStateOfLine(line);
      if (state != null) {
        const widget =
          state.kind === LIST_KIND_NUMBER ? new NumberWidget(state.number) : new BulletWidget();
        markers.push(
          Decoration.replace({ widget }).range(line.from, line.from + state.markerLength)
        );
      }
      pos = line.to + 1;
    }
  }
  return Decoration.set(markers);
}

export const listPlugin = ViewPlugin.fromClass(
  class {
    constructor(view) {
      this.markers = buildListDecorations(view);
    }

    update(update) {
      if (update.docChanged || update.viewportChanged) {
        this.markers = buildListDecorations(update.view);
      }
    }
  },
  {
    decorations: (value) => value.markers,
    provide: (plugin) => [
      EditorView.atomicRanges.of((view) => {
        const value = view.plugin(plugin);
        return value == null ? Decoration.none : value.markers;
      }),
    ],
  }
);

// Space keymap: converting the shorthand. Runs only when everything from the
// line start to the caret is exactly "-" (bullet) or "1." (numbered); any text
// after the caret becomes the item's content. Otherwise the space inserts as
// usual. A numbered list always starts at 1, so only "1." is the trigger; the
// renumber filter keeps later items sequential.
function convertListShorthand(view) {
  const selection = view.state.selection.main;
  if (!selection.empty) {
    return false;
  }
  const line = view.state.doc.lineAt(selection.head);
  const beforeCaret = view.state.doc.sliceString(line.from, selection.head);
  let marker = null;
  if (beforeCaret === BULLET_SHORTHAND) {
    marker = BULLET_MARKER;
  } else if (beforeCaret === NUMBER_SHORTHAND) {
    marker = "1. ";
  }
  if (marker == null) {
    return false;
  }
  view.dispatch({
    changes: { from: line.from, to: selection.head, insert: marker },
    selection: { anchor: line.from + marker.length },
    userEvent: "input.type",
    scrollIntoView: true,
  });
  return true;
}

// Enter on a list line: an empty item exits the list (the marker is removed),
// any other position continues it. Enter at the very start of the line (before
// the marker) falls through to the default newline, which pushes the item down.
// Numbered continuations insert the next number; the renumber filter corrects
// it if the caret was mid-list.
function handleListEnter(view) {
  const selection = view.state.selection.main;
  if (!selection.empty) {
    return false;
  }
  const line = view.state.doc.lineAt(selection.head);
  const state = listStateOfLine(line);
  if (state == null) {
    return false;
  }
  if (line.text.length === state.markerLength) {
    view.dispatch({
      changes: { from: line.from, to: line.to },
      userEvent: "delete",
    });
    return true;
  }
  if (selection.head < line.from + state.markerLength) {
    return false;
  }
  const nextMarker =
    state.kind === LIST_KIND_NUMBER ? String(Number(state.number) + 1) + ". " : BULLET_MARKER;
  view.dispatch(view.state.replaceSelection("\n" + nextMarker), {
    userEvent: "input.type",
    scrollIntoView: true,
  });
  return true;
}

export const listKeymap = keymap.of([
  { key: "Enter", run: handleListEnter },
  { key: "Space", run: convertListShorthand },
]);

// Keeps every contiguous run of numbered items sequential from 1: any digits
// that disagree with the item's position in its run are rewritten, appended to
// the same transaction so it stays one undo step and the caret maps through.
// Host-origin transactions (loads, inserts) are left untouched — they render as
// their stored digits. A non-numbered line resets the run.
export const listRenumber = EditorState.transactionFilter.of((tr) => {
  if (!tr.docChanged || tr.annotation(HostOrigin) != null) {
    return tr;
  }
  const doc = tr.newDoc;
  const changes = [];
  let counter = 0;
  for (let i = 1; i <= doc.lines; i++) {
    const line = doc.line(i);
    const match = NUMBER_MARKER_PATTERN.exec(line.text);
    if (match == null) {
      counter = 0;
      continue;
    }
    counter += 1;
    const expected = String(counter);
    if (match[1] !== expected) {
      changes.push({ from: line.from, to: line.from + match[1].length, insert: expected });
    }
  }
  if (changes.length === 0) {
    return tr;
  }
  return [tr, { changes, sequential: true }];
});
