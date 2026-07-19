// Incoming host messages: the dispatch table of everything
// Components/EditorHost.cs can ask the page to do.

import { undo, redo, selectAll } from "@codemirror/commands";
import { logError, logWarning, describeError } from "./pageLog.js";
import { getView } from "./editorContext.js";
import { normaliseIncoming } from "./crlfText.js";
import { initBuffers, activate, setText } from "./buffers.js";
import { flushSync } from "./contentSync.js";
import { applyCalcResult } from "./inlineCalc.js";
import { resetCaretBlink, beginRevealEntrance } from "./startupReveal.js";
import { applyTheme, applyBackground } from "./appearance.js";

function handleCommand(name) {
  const view = getView();
  switch (name) {
    case "undo":
      undo(view);
      break;
    case "redo":
      redo(view);
      break;
    case "cut":
      view.focus();
      document.execCommand("cut");
      break;
    case "copy":
      view.focus();
      document.execCommand("copy");
      break;
    case "selectAll":
      selectAll(view);
      break;
    default:
      break;
  }
}

function insertClamped(text) {
  const view = getView();
  const normalised = normaliseIncoming(text);
  view.focus();
  view.dispatch(
    view.state.replaceSelection(normalised),
    { userEvent: "input.paste" }
  );
}

// A throwing handler is logged (message *name* only — the payload may carry
// buffer text) and swallowed, so one bad message cannot detach the bridge.
export function handleHostMessage(message) {
  try {
    dispatchHostMessage(message);
  } catch (error) {
    logError(
      "Handling the host message '" + (message && message.type) + "' failed: " + describeError(error),
      error
    );
  }
}

function dispatchHostMessage(message) {
  switch (message.type) {
    case "init":
      initBuffers(message.buffers);
      break;
    case "activate":
      activate(message.index);
      break;
    case "setText":
      setText(message.index, message.text);
      break;
    case "focus":
      getView().focus();
      break;
    case "resetBlink":
      resetCaretBlink();
      break;
    case "entrance":
      beginRevealEntrance();
      break;
    case "flush":
      flushSync();
      break;
    case "insert":
      insertClamped(message.text);
      break;
    case "command":
      handleCommand(message.name);
      break;
    case "calcResult":
      applyCalcResult(message);
      break;
    case "theme":
      applyTheme(message);
      break;
    case "background":
      applyBackground(message);
      break;
    default:
      logWarning("Unknown host message type '" + message.type + "'.");
      break;
  }
}
