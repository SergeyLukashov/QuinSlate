// The five per-buffer EditorStates and their activation (tab switch).
// Per-buffer undo/selection/scroll live in the EditorState and survive the swap.

import { HostOrigin } from "./hostBridge.js";
import { BUFFER_MIN, BUFFER_MAX, getView, getActiveIndex, setActiveIndex } from "./editorContext.js";
import { normaliseIncoming } from "./crlfText.js";
import { makeState } from "./editorSetup.js";
import { flushSync } from "./contentSync.js";
import { playEntrance } from "./entrance.js";

const states = new Map(); // index -> EditorState
// CM6 keeps scroll in the DOM, not the EditorState, so per-buffer scroll must be preserved across
// tab switches. scrollSnapshot() captures the exact position as an effect that restores precisely
// after the state is re-measured (raw scrollTop races the re-measure and lands a few lines off).
const scrollSnapshotByIndex = new Map(); // index -> StateEffect

export function initBuffers(buffers) {
  for (const buffer of buffers) {
    states.set(buffer.index, makeState(buffer.text));
  }
  setActiveIndex(buffers.length > 0 ? buffers[0].index : BUFFER_MIN);
  const first = states.get(getActiveIndex());
  if (first != null) {
    getView().setState(first);
  }
}

export function activate(index) {
  const view = getView();
  if (index >= BUFFER_MIN && index <= BUFFER_MAX && index !== getActiveIndex()) {
    // Persist pending edits and capture the exact scroll position of the buffer being switched
    // away from before swapping in the target buffer's state.
    flushSync();
    scrollSnapshotByIndex.set(getActiveIndex(), view.scrollSnapshot());
    states.set(getActiveIndex(), view.state);
    setActiveIndex(index);
    const next = states.get(index);
    if (next != null) {
      view.setState(next);
      const snapshot = scrollSnapshotByIndex.get(index);
      if (snapshot != null) {
        view.dispatch({ effects: snapshot });
      }
    }
  }
  playEntrance();
}

export function setText(index, text) {
  const doc = normaliseIncoming(text);
  if (index === getActiveIndex()) {
    const view = getView();
    view.dispatch({
      changes: { from: 0, to: view.state.doc.length, insert: doc },
      annotations: HostOrigin.of(true),
    });
  } else {
    states.set(index, makeState(text));
  }
}
