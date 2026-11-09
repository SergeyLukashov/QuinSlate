// Assembles the single EditorView: the base extension set every buffer state
// shares, state construction, and the DOM listeners on the editor surface.

import { EditorState } from "@codemirror/state";
import { EditorView, keymap, drawSelection } from "@codemirror/view";
import { history, historyKeymap, standardKeymap, undoDepth, redoDepth } from "@codemirror/commands";
import { postToHost, HostOrigin } from "./hostBridge.js";
import { setView, getActiveIndex } from "./editorContext.js";
import { normaliseIncoming } from "./crlfText.js";
import { capFilter } from "./charLimit.js";
import { calcHighlightField } from "./calcHighlight.js";
import { detectCalc } from "./inlineCalc.js";
import { taskPlugin, taskKeymap } from "./tasks.js";
import { listPlugin, listKeymap, listRenumber } from "./lists.js";
import { linkPlugin } from "./links.js";
import { markdownPlugin } from "./markdownFormat.js";
import { verticalMotionKeymap } from "./verticalMotion.js";
import { scrollerFocusPlugin } from "./scrollerFocus.js";
import { indentKeymap } from "./indent.js";
import { panelKeymap } from "./panelShortcuts.js";
import { queueSync, flushSync } from "./contentSync.js";
import { editorTheme } from "./editorTheme.js";
import { registerClickProbe } from "./clickProbe.js";
import { searchHighlightPlugin, searchScrollMargins } from "./searchHighlight.js";
import { onSearchUpdate } from "./search.js";

const SECONDARY_BUTTON = 2;

const baseExtensions = [
  history(),
  panelKeymap,
  // Registered before standardKeymap so the task/list Enter/Space handlers get
  // first refusal; they return false whenever the caret is not on their line
  // kind. Tasks come first: a "- [ ] " line is a task, not a bullet.
  taskKeymap,
  listKeymap,
  // Tab/Shift+Tab for every line — item kinds and plain text alike — so it sits
  // alongside rather than inside either module.
  indentKeymap,
  // Also before standardKeymap, and for the same reason: it rebinds the Arrow and Page motion
  // commands so the caret keeps its column when the markers the destination line is hiding are
  // about to be revealed.
  verticalMotionKeymap,
  keymap.of([...historyKeymap, ...standardKeymap]),
  taskPlugin,
  listPlugin,
  listRenumber,
  // Marks URLs anywhere in the text; no keymap, and it never touches the document.
  linkPlugin,
  // Headings, bold/italic/strikethrough, inline code, and rules — also decoration-only, and
  // registered after linkPlugin because a URL's characters are claimed before emphasis reads them.
  markdownPlugin,
  // Search highlights (Docs/Specs/24-SEARCH.md), decoration-only like the two above. Ahead of
  // calcHighlightField so the calc highlight nests inside a search mark and paints on top of it.
  searchHighlightPlugin,
  searchScrollMargins,
  // Returns focus to the content when a scrollbar press moves it to the scroller.
  scrollerFocusPlugin,
  EditorView.lineWrapping,
  drawSelection(),
  editorTheme,
  // Parity with IsSpellCheckEnabled=false and the plain-text surface.
  // autocorrect MUST stay "on" (overrides CM6's built-in "off"): Edge/WebView2
  // silently drops Windows emoji-panel (Win+./Win+;) insertions into a
  // contenteditable that has autocorrect="off" and block-level children (CM6's
  // .cm-line divs) — the TSF commit never reaches the page. Verified by attribute
  // bisection; see Docs/Investigations/05-EMOJI-PANEL-AUTOCORRECT-OFF-DROP.md.
  EditorView.contentAttributes.of({ spellcheck: "false", autocorrect: "on", autocapitalize: "off" }),
  capFilter,
  calcHighlightField,
  EditorState.allowMultipleSelections.of(false),
  EditorView.updateListener.of((update) => {
    // Ignore state swaps (tab switch via setState carries no transactions); only real edits sync.
    if (update.docChanged && update.transactions.length > 0) {
      const isHostOrigin = update.transactions.some((tr) => tr.annotation(HostOrigin));
      if (!isHostOrigin) {
        queueSync(getActiveIndex(), update.state.doc.toString());
      }
      detectCalc(update);
    }
    onSearchUpdate(update);
  }),
];

export function makeState(text) {
  return EditorState.create({
    doc: normaliseIncoming(text),
    extensions: baseExtensions,
  });
}

export function createEditor() {
  const view = new EditorView({
    state: makeState(""),
    parent: document.getElementById("editor"),
  });
  setView(view);

  // The result highlight is one animation at a time and never plays on load; it
  // is only started from applyCalcResult, so nothing to do here.

  // A right press opens the menu without touching the selection. Chromium's default for a
  // contenteditable is to collapse the caret at the click first, so right-clicking outside a
  // selection threw that selection away before the menu could offer to cut or copy it. CM6 itself
  // only starts a mouse selection for the primary button, so suppressing the default is enough —
  // but that default also carried the focus, which view.focus() puts back.
  view.contentDOM.addEventListener("mousedown", (event) => {
    if (event.button !== SECONDARY_BUTTON) {
      return;
    }

    event.preventDefault();
    view.focus();
  });

  view.contentDOM.addEventListener("contextmenu", (event) => {
    event.preventDefault();
    const selection = view.state.selection.main;
    postToHost("contextMenu", {
      x: event.clientX,
      y: event.clientY,
      canUndo: undoDepth(view.state) > 0,
      canRedo: redoDepth(view.state) > 0,
      hasSelection: !selection.empty,
    });
  });

  view.contentDOM.addEventListener("blur", flushSync);

  // Content-free click-coordinate logging for the caret/click line-offset hunt; paired with the
  // host-side press log in Components/EditorPointerProbe.cs. See
  // Docs/Investigations/07-EDITOR-CLICK-CARET-LINE-OFFSET.md.
  registerClickProbe(view);
}
