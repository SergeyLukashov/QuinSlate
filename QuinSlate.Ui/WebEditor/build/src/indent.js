// Tab / Shift+Tab indentation for the editor. Depth is a line's leading spaces — INDENT_UNIT per
// level (listItems.js) — for every line, list item or not, so a shift is just a rewrite of each
// line's indent: one transaction, and so one undo step. Tab always shifts whole lines, wherever the
// caret sits in them, so Shift+Tab is its exact mirror.
//
// A list item (Docs/Specs/19-CHECKABLE-TASKS.md, Docs/Specs/20-LISTS.md) nests under Notion's two
// rules:
//   - an item can only nest under an item directly above it, and only one level deeper than that
//     item, so no indent can skip a level or orphan an item under nothing;
//   - an item's children move with it, so a parent never leaves its subtree behind at a depth that
//     would silently re-read as a sibling.
//
// A plain line has no structure to protect, so it just indents — that is the ordinary text-editor
// Tab. Both share the same unit, so one Tab is one level whichever kind of line it lands on: indent
// a plain line once, type "- ", and the bullet arrives at depth 1.

import { keymap } from "@codemirror/view";
import { parseItem, lineIndent, indentText, MAX_DEPTH } from "./listItems.js";

const INDENT_DELTA = 1;
const OUTDENT_DELTA = -1;
const MIN_DEPTH = 0;
// EditorSelection.map's assoc: associate a mapped position with the text after it, not before.
const ASSOC_AFTER = 1;

function shiftEntry(line) {
  const item = parseItem(line.text);
  const indent = item == null ? lineIndent(line.text) : item;
  return { line, item, depth: indent.depth, indentLength: indent.indentLength };
}

function isBlank(line) {
  return line.text.trim().length === 0;
}

// Every line the selection touches, plus the nested children trailing the last of them when that
// line is a list item. Blank lines are skipped in a multi-line selection, where indenting them
// would only leave trailing whitespace; a caret alone on a blank line still indents, so the user
// can indent before typing.
function linesToShift(state) {
  const selection = state.selection.main;
  const firstLine = state.doc.lineAt(selection.from).number;
  const lastLine = state.doc.lineAt(selection.to).number;
  const multiLine = lastLine > firstLine;
  const shifted = [];
  for (let i = firstLine; i <= lastLine; i++) {
    const line = state.doc.line(i);
    if (multiLine && isBlank(line)) {
      continue;
    }
    shifted.push(shiftEntry(line));
  }
  if (shifted.length === 0) {
    return null;
  }
  const last = shifted[shifted.length - 1];
  if (last.item != null) {
    for (let i = last.line.number + 1; i <= state.doc.lines; i++) {
      const line = state.doc.line(i);
      const item = parseItem(line.text);
      if (item == null || item.depth <= last.item.depth) {
        break;
      }
      shifted.push(shiftEntry(line));
    }
  }
  return shifted;
}

function deepestDepth(shifted) {
  let deepest = MIN_DEPTH;
  for (const entry of shifted) {
    deepest = Math.max(deepest, entry.depth);
  }
  return deepest;
}

// Only a list item has to earn its indent: the item above the block is its one candidate parent, so
// indenting is refused when there is none (the first item of a list), or when the block already
// sits deeper than it (a second Tab would skip a level). A plain first line indents freely. The
// depth cap applies to both, so stored depth never outruns the depth the widgets can render.
function canIndent(state, shifted) {
  const first = shifted[0];
  if (first.item != null) {
    if (first.line.number === 1) {
      return false;
    }
    const above = parseItem(state.doc.line(first.line.number - 1).text);
    if (above == null || first.depth > above.depth) {
      return false;
    }
  }
  return deepestDepth(shifted) < MAX_DEPTH;
}

// Keyed on the first line so a block moves together or not at all: were each line clamped at 0
// independently, outdenting a top-level item would flatten its subtree up into it.
function canOutdent(shifted) {
  return shifted[0].depth > MIN_DEPTH;
}

function shiftLines(view, delta) {
  const shifted = linesToShift(view.state);
  if (shifted == null) {
    return;
  }
  const allowed = delta === INDENT_DELTA ? canIndent(view.state, shifted) : canOutdent(shifted);
  if (!allowed) {
    return;
  }
  // Rewriting the whole indent (rather than adding or trimming one unit) also normalises any
  // hand-edited or pasted spacing to whole levels.
  const changes = view.state.changes(
    shifted.map(({ line, depth, indentLength }) => ({
      from: line.from,
      to: line.from + indentLength,
      insert: indentText(Math.max(MIN_DEPTH, depth + delta)),
    }))
  );
  view.dispatch({
    changes,
    // The caret rides the indent. A caret sitting exactly at the insertion point — column 0, or an
    // empty line — maps to *before* the inserted text under CM6's default association, so the line
    // would move and the caret would stay put. Associating with the text after puts it where a text
    // editor's Tab leaves it: past the new indent. Positions already inside the content are
    // unaffected by the association and shift with their line either way.
    selection: view.state.selection.map(changes, ASSOC_AFTER),
    userEvent: delta === INDENT_DELTA ? "input.indent" : "delete.dedent",
    scrollIntoView: true,
  });
}

// Both report handled unconditionally: Tab must never insert a tab character or move DOM focus, so
// the key stays swallowed even where the shift itself is refused.
export function indentLines(view) {
  shiftLines(view, INDENT_DELTA);
  return true;
}

export function outdentLines(view) {
  shiftLines(view, OUTDENT_DELTA);
  return true;
}

export const indentKeymap = keymap.of([
  { key: "Tab", preventDefault: true, run: indentLines, shift: outdentLines },
]);
