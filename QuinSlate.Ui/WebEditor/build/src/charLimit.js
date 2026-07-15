// The character cap, enforced in exactly one place: a transaction filter. Any
// change (typing, paste, IME commit, drop, the host's own insert) is a CM6
// transaction, so it passes through here and nothing else needs to clamp.
// See Docs/Specs/18-CHARACTER-LIMIT-NOTICE.md.

import { EditorState } from "@codemirror/state";
import { postToHost, HostOrigin } from "./hostBridge.js";
import { getActiveIndex } from "./editorContext.js";
import { crlfLengthOfDoc, crlfLengthOfString, truncateToCrlfBudget } from "./crlfText.js";

const MAX_CRLF_LENGTH = 1000000; // AppConstants.MaxBufferLength, counted with CRLF breaks (break = 2)
// Cause values of the limitReached message; the host words its notice from these
// (EditorHost.CausePaste / CauseType).
const CAUSE_PASTE = "paste";
const CAUSE_TYPE = "type";

// Every clamp the user caused is reported to the host, which throttles the notice and shows it.
// Host-origin transactions (init, setText) are excluded: clamping them is not a user action.
// Only an index, a cause, and a count cross the bridge — never buffer text.
function reportLimitReached(tr, dropped) {
  if (tr.annotation(HostOrigin) != null || dropped <= 0) {
    return;
  }
  const cause = tr.isUserEvent("input.paste") || tr.isUserEvent("input.drop") ? CAUSE_PASTE : CAUSE_TYPE;
  // A transaction filter must not have side effects while it runs; report once the transaction is applied.
  queueMicrotask(() => postToHost("limitReached", { index: getActiveIndex(), cause, dropped }));
}

// Transaction filter enforcing the CRLF cap in one place. Any
// change (typing, paste, IME commit, drop) is truncated so the resulting
// document never exceeds the cap; editor content and persisted content stay
// identical, which is what removes the old editor-vs-disk drift.
export const capFilter = EditorState.transactionFilter.of((tr) => {
  if (!tr.docChanged) {
    return tr;
  }
  if (crlfLengthOfDoc(tr.newDoc) <= MAX_CRLF_LENGTH) {
    return tr;
  }

  reportLimitReached(tr, crlfLengthOfDoc(tr.newDoc) - MAX_CRLF_LENGTH);

  const startDoc = tr.startState.doc;
  let deletedCrlf = 0;
  const rawChanges = [];
  tr.changes.iterChanges((fromA, toA, fromB, toB, inserted) => {
    deletedCrlf += crlfLengthOfString(startDoc.sliceString(fromA, toA));
    rawChanges.push({ from: fromA, to: toA, insert: inserted.toString() });
  });

  let budget = MAX_CRLF_LENGTH - (crlfLengthOfDoc(startDoc) - deletedCrlf);
  if (budget < 0) {
    budget = 0;
  }

  const clamped = [];
  let lastInsertEnd = null;
  for (const change of rawChanges) {
    const insert = truncateToCrlfBudget(change.insert, budget);
    budget -= crlfLengthOfString(insert);
    clamped.push({ from: change.from, to: change.to, insert });
    lastInsertEnd = change.from + insert.length;
  }

  const spec = { changes: clamped, scrollIntoView: true };
  if (lastInsertEnd != null) {
    spec.selection = { anchor: lastInsertEnd };
  }
  return spec;
});
