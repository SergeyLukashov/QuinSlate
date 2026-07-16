// Bullet and numbered lists (Docs/Specs/20-LISTS.md). A line starting with the
// marker "- " is a bullet item; a line starting with "<n>. " is a numbered
// item. The marker text stays in the document (and so in the buffer file) and
// renders as a widget wherever it appears — typed, loaded, or pasted: a "•" dot
// for bullets, the number for ordered items. Typing "- " at the start of a line
// converts it to a bullet; "1. " starts a numbered list; Enter continues the
// list; Enter on an empty item removes the marker; an item nests with Tab
// (indent.js). The marker is atomic together with the indent that carries
// its depth, so Backspace after it deletes the whole prefix, turning the line
// back into plain text at column 0. A transaction filter keeps every contiguous
// numbered run at a given depth sequential from 1.

import { EditorView, keymap, Decoration, ViewPlugin, WidgetType } from "@codemirror/view";
import { EditorState } from "@codemirror/state";
import { HostOrigin } from "./hostBridge.js";
import {
  parseItem,
  indentText,
  renderDepth,
  splitShorthand,
  ITEM_KIND_BULLET,
  ITEM_KIND_NUMBER,
  BULLET_MARKER,
} from "./listItems.js";

const NUMBER_MARKER_SUFFIX = ". ";
const BULLET_SHORTHAND = "-";
const NUMBER_SHORTHAND = "1.";
const FIRST_NUMBER = 1;
const RUN_BROKEN = 0;
const DEPTH_PROPERTY = "--list-depth";

// Returns the item descriptor for a bullet or numbered line, null otherwise (a task line belongs
// to tasks.js, which owns the "- [ ] " prefix).
function listOfLine(line) {
  const item = parseItem(line.text);
  if (item == null || (item.kind !== ITEM_KIND_BULLET && item.kind !== ITEM_KIND_NUMBER)) {
    return null;
  }
  return item;
}

function numberMarker(value) {
  return String(value) + NUMBER_MARKER_SUFFIX;
}

class BulletWidget extends WidgetType {
  constructor(depth) {
    super();
    this.depth = depth;
  }

  eq(other) {
    return other.depth === this.depth;
  }

  toDOM() {
    const dot = document.createElement("span");
    dot.className = "cm-list-bullet";
    dot.style.setProperty(DEPTH_PROPERTY, String(this.depth));
    dot.setAttribute("aria-hidden", "true");
    return dot;
  }

  ignoreEvent() {
    return false;
  }
}

class NumberWidget extends WidgetType {
  constructor(number, depth) {
    super();
    this.number = number;
    this.depth = depth;
  }

  eq(other) {
    return other.number === this.number && other.depth === this.depth;
  }

  toDOM() {
    const label = document.createElement("span");
    label.className = "cm-list-number";
    label.textContent = this.number + ".";
    label.style.setProperty(DEPTH_PROPERTY, String(this.depth));
    label.setAttribute("aria-hidden", "true");
    return label;
  }

  ignoreEvent() {
    return false;
  }
}

// One decoration set per viewport pass: the marker widgets, which are also the
// atomic ranges so the caret never lands inside a marker (or its indent) and
// Backspace removes the prefix whole.
function buildListDecorations(view) {
  const markers = [];
  for (const range of view.visibleRanges) {
    let pos = range.from;
    while (pos <= range.to) {
      const line = view.state.doc.lineAt(pos);
      const item = listOfLine(line);
      if (item != null) {
        const depth = renderDepth(item.depth);
        const widget =
          item.kind === ITEM_KIND_NUMBER ? new NumberWidget(item.number, depth) : new BulletWidget(depth);
        markers.push(Decoration.replace({ widget }).range(line.from, line.from + item.prefixLength));
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
// line start to the caret is exactly "-" (bullet) or "1." (numbered), optionally
// behind an indent — the item then converts at the depth it was typed at. Any
// text after the caret becomes the item's content. Otherwise the space inserts
// as usual. A numbered list always starts at 1, so only "1." is the trigger; the
// renumber filter keeps later items sequential.
export function convertListShorthand(view) {
  const selection = view.state.selection.main;
  if (!selection.empty) {
    return false;
  }
  const line = view.state.doc.lineAt(selection.head);
  const typed = splitShorthand(view.state.doc.sliceString(line.from, selection.head));
  let marker = null;
  if (typed.shorthand === BULLET_SHORTHAND) {
    marker = BULLET_MARKER;
  } else if (typed.shorthand === NUMBER_SHORTHAND) {
    marker = numberMarker(FIRST_NUMBER);
  }
  if (marker == null) {
    return false;
  }
  const prefix = indentText(typed.depth) + marker;
  view.dispatch({
    changes: { from: line.from, to: selection.head, insert: prefix },
    selection: { anchor: line.from + prefix.length },
    userEvent: "input.type",
    scrollIntoView: true,
  });
  return true;
}

// Enter on a list line: an empty item exits the list at any depth (marker and
// indent both go, leaving a plain empty line), any other position continues it
// at the same depth. Enter at the very start of the line (before the marker)
// falls through to the default newline, which pushes the item down. Numbered
// continuations insert the next number; the renumber filter corrects it if the
// caret was mid-list.
export function handleListEnter(view) {
  const selection = view.state.selection.main;
  if (!selection.empty) {
    return false;
  }
  const line = view.state.doc.lineAt(selection.head);
  const item = listOfLine(line);
  if (item == null) {
    return false;
  }
  if (line.text.length === item.prefixLength) {
    view.dispatch({
      changes: { from: line.from, to: line.to },
      userEvent: "delete",
    });
    return true;
  }
  if (selection.head < line.from + item.prefixLength) {
    return false;
  }
  const nextMarker =
    item.kind === ITEM_KIND_NUMBER ? numberMarker(Number(item.number) + 1) : BULLET_MARKER;
  view.dispatch(view.state.replaceSelection("\n" + indentText(item.depth) + nextMarker), {
    userEvent: "input.type",
    scrollIntoView: true,
  });
  return true;
}

export const listKeymap = keymap.of([
  { key: "Enter", run: handleListEnter },
  { key: "Space", run: convertListShorthand },
]);

// Keeps every contiguous run of numbered items sequential from 1, per depth: any
// digits that disagree with the item's position in its run are rewritten,
// appended to the same transaction so it stays one undo step and the caret maps
// through. Counters are stacked by depth, so nested items never disturb the run
// they hang off — a parent run resumes its count after its children, while a
// re-entered nested run restarts at 1. A bullet or task breaks the numbered run
// at its own depth only; a plain or blank line breaks every run. Host-origin
// transactions (loads, inserts) are left untouched — they render as their stored
// digits.
export const listRenumber = EditorState.transactionFilter.of((tr) => {
  if (!tr.docChanged || tr.annotation(HostOrigin) != null) {
    return tr;
  }
  const doc = tr.newDoc;
  const changes = [];
  const counters = [];
  for (let i = 1; i <= doc.lines; i++) {
    const line = doc.line(i);
    const item = parseItem(line.text);
    if (item == null) {
      counters.length = 0;
      continue;
    }
    // Anything deeper than this item is a run that just ended: dropping the counters is what makes
    // the next visit to that depth restart at 1.
    if (counters.length > item.depth + 1) {
      counters.length = item.depth + 1;
    }
    if (item.kind !== ITEM_KIND_NUMBER) {
      counters[item.depth] = RUN_BROKEN;
      continue;
    }
    const expected = (counters[item.depth] || RUN_BROKEN) + 1;
    counters[item.depth] = expected;
    if (item.number !== String(expected)) {
      const from = line.from + item.indentLength;
      changes.push({ from, to: from + item.number.length, insert: String(expected) });
    }
  }
  if (changes.length === 0) {
    return tr;
  }
  return [tr, { changes, sequential: true }];
});
