// Inline-calc detection. The page recognises a user-typed "=" that ends a line
// (layout-independent — it is the composed character) and asks the host to
// evaluate; the host replies and the page rewrites the line here. Evaluation
// itself stays in C#.

import { postToHost } from "./hostBridge.js";
import { getView, getActiveIndex, getAccent } from "./editorContext.js";
import { startCalcHighlight } from "./calcHighlight.js";

let pendingCalc = null; // { index, from, lineText }

export function detectCalc(update) {
  for (const tr of update.transactions) {
    if (!tr.docChanged) {
      continue;
    }
    if (!tr.isUserEvent("input.type") && !tr.isUserEvent("input")) {
      continue;
    }
    let inserted = "";
    tr.changes.iterChanges((fromA, toA, fromB, toB, ins) => {
      inserted += ins.toString();
    });
    if (inserted !== "=") {
      continue;
    }
    const state = update.state;
    const caret = state.selection.main.head;
    const line = state.doc.lineAt(caret);
    if (caret !== line.to || !line.text.endsWith("=")) {
      continue;
    }
    pendingCalc = { index: getActiveIndex(), from: line.from, lineText: line.text };
    postToHost("calcRequest", { index: getActiveIndex(), lineContent: line.text });
    return;
  }
}

export function applyCalcResult(message) {
  const token = pendingCalc;
  pendingCalc = null;
  if (token == null || !message.ok || token.index !== getActiveIndex()) {
    return;
  }
  const view = getView();
  const line = view.state.doc.lineAt(token.from);
  // Discard if the user changed the line before the reply landed.
  if (line.from !== token.from || line.text !== token.lineText) {
    return;
  }

  const lineText = token.lineText;
  const separator = lineText.endsWith(" =") ? " " : "";
  const newLineText = lineText + separator + message.result;
  const highlightFrom = line.from + lineText.length + separator.length;
  const highlightTo = line.from + newLineText.length;
  const caret = line.from + newLineText.length;

  view.dispatch({
    changes: { from: line.from, to: line.to, insert: newLineText },
    selection: { anchor: caret },
  });
  startCalcHighlight(view, highlightFrom, highlightTo, getAccent());
}
