// Shared recognition of the three list-item kinds — tasks (Docs/Specs/19-CHECKABLE-TASKS.md),
// bullets and numbered items (Docs/Specs/20-LISTS.md) — and of the nesting depth they share.
// tasks.js and lists.js keep their own widgets, shorthands and Enter handling; this module is
// the single place that answers "what item is this line, and how deep is it", so indent.js can
// move all three kinds with one pair of Tab/Shift+Tab commands.
//
// Depth is carried in the text as leading spaces — INDENT_UNIT per level — because the document
// text IS the persisted format. The indent is part of the item's prefix, which renders as one
// atomic widget: the caret never lands inside it and Backspace removes indent and marker together,
// dropping the line back to plain text at column 0.

const INDENT_UNIT = "  ";
const INDENT_PATTERN = /^ */;

// Past this depth a line is still recognised and still renders — hand-edited or pasted text can
// carry any indent — but it renders at the cap, and Tab refuses to nest deeper.
export const MAX_DEPTH = 8;

export const ITEM_KIND_TASK = "task";
export const ITEM_KIND_BULLET = "bullet";
export const ITEM_KIND_NUMBER = "number";

export const TASK_MARKER_UNCHECKED = "- [ ] ";
export const TASK_STATE_CHAR_OFFSET = 3; // index of the " "/"x" state char inside the marker
export const BULLET_MARKER = "- ";

const TASK_MARKER_PATTERN = /^- \[( |x|X)\] /;
// A bullet is "- " NOT followed by a task checkbox marker: those lines belong to tasks.js.
const BULLET_MARKER_PATTERN = /^- (?!\[( |x|X)\] )/;
const NUMBER_MARKER_PATTERN = /^(\d+)\. /;

const TASK_UNCHECKED_STATE_CHAR = " ";

// The leading-space indent of any line, list item or not. Plain lines carry a depth too: Tab
// indents them freely (there is no structure to guard), and it is what the "- " / "1. " / "[] "
// shorthands read to convert an already-indented line at its own depth.
export function lineIndent(text) {
  const indentLength = INDENT_PATTERN.exec(text)[0].length;
  return { depth: Math.floor(indentLength / INDENT_UNIT.length), indentLength };
}

function makeItem(kind, indent, markerLength, checked, number) {
  return {
    kind,
    depth: indent.depth,
    indentLength: indent.indentLength,
    markerLength,
    // The atomic range: indent + marker. Also where the item's content starts.
    prefixLength: indent.indentLength + markerLength,
    checked,
    number,
  };
}

// Returns the item descriptor for a line's text, or null when the line is not a list item.
// `checked` is only meaningful for tasks, `number` (the marker's own digits) only for numbered
// items. `depth` floors the raw indent, so a stray odd space never invents a level.
export function parseItem(text) {
  const indent = lineIndent(text);
  const rest = text.slice(indent.indentLength);
  const task = TASK_MARKER_PATTERN.exec(rest);
  if (task != null) {
    return makeItem(ITEM_KIND_TASK, indent, task[0].length, task[1] !== TASK_UNCHECKED_STATE_CHAR, null);
  }
  const number = NUMBER_MARKER_PATTERN.exec(rest);
  if (number != null) {
    return makeItem(ITEM_KIND_NUMBER, indent, number[0].length, false, number[1]);
  }
  if (BULLET_MARKER_PATTERN.test(rest)) {
    return makeItem(ITEM_KIND_BULLET, indent, BULLET_MARKER.length, false, null);
  }
  return null;
}

// The canonical leading spaces for a depth. Writing this back over an item's existing indent is
// what normalises hand-edited or pasted spacing to whole levels.
export function indentText(depth) {
  return INDENT_UNIT.repeat(depth);
}

// The depth a widget renders at: the pixel step lives in editor.css, which multiplies this by the
// marker column width, so the caret never sits further right than the cap allows.
export function renderDepth(depth) {
  return Math.min(depth, MAX_DEPTH);
}

// Splits a caret's line prefix into its indent and whatever the user typed after it, so the "- ",
// "1. " and "[] " shorthands convert at the depth they were typed at.
export function splitShorthand(beforeCaret) {
  const indent = lineIndent(beforeCaret);
  return { depth: indent.depth, shorthand: beforeCaret.slice(indent.indentLength) };
}
