// A headless stand-in for the EditorView, for testing CM6 commands without a DOM.
//
// The editor's commands only ever touch `view.state` and `view.dispatch`, so a plain object with
// those two is enough to drive them exactly as the real keymap does. `dispatch` is variadic because
// CM6 merges multiple specs into one transaction, and the task/list Enter handlers rely on that
// (`view.dispatch(view.state.replaceSelection(...), { userEvent })`) — a single-spec stub would
// silently drop the second half.
//
// Extensions are passed in rather than pulled from editorSetup.js: that module builds a real
// EditorView. Tests declare the filters they actually depend on, so a failure names its own cause.

import { EditorState } from "@codemirror/state";

// Marks the caret in a test document. No test text contains a literal "|".
export const CARET = "|";

function createView(doc, selection, extensions) {
  let state = EditorState.create({ doc, selection, extensions });
  return {
    get state() {
      return state;
    },
    dispatch(...specs) {
      state = state.update(...specs).state;
    },
    text() {
      return state.doc.toString();
    },
    caret() {
      return state.selection.main.head;
    },
    selection() {
      const range = state.selection.main;
      return { from: range.from, to: range.to };
    },
  };
}

// Builds a view from text with the caret marked by "|"; without a marker the caret sits at 0.
export function makeView(text, extensions = []) {
  const caret = text.indexOf(CARET);
  if (caret < 0) {
    return createView(text, undefined, extensions);
  }
  const doc = text.slice(0, caret) + text.slice(caret + CARET.length);
  return createView(doc, { anchor: caret }, extensions);
}

// Builds a view whose selection spans whole lines, from the start of `fromLine` to the end of
// `toLine` (both 1-based) — the shape a user makes by dragging down the gutter.
export function makeLineSelectionView(text, fromLine, toLine, extensions = []) {
  const view = createView(text, undefined, extensions);
  const doc = view.state.doc;
  view.dispatch({
    selection: { anchor: doc.line(fromLine).from, head: doc.line(toLine).to },
  });
  return view;
}

// Appends a character at the end of the document. Used to fire a doc-changing transaction through
// the filters under test (listRenumber only reacts to docChanged transactions).
export function touchDoc(view, text = "!") {
  view.dispatch({ changes: { from: view.state.doc.length, insert: text } });
}
