// Incoming host messages: the dispatch table of everything
// Components/EditorHost.cs can ask the page to do.

import { undo, redo, selectAll } from "@codemirror/commands";
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

export function handleHostMessage(message) {
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
      break;
  }
}
