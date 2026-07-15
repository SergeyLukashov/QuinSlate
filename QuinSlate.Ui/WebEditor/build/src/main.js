// QuinSlate editor page: CodeMirror 6 hosting five plain-text buffers in one
// EditorView, driven entirely over the WebView2 host bridge. Like-for-like
// replacement of the retired RichEditBox editors (see
// Docs/Specs/17-EDITOR-CODEMIRROR-MIGRATION.md): plain text, a 50,000-char cap,
// inline-calc detection (evaluation stays in C#), the calc result highlight,
// panel keyboard shortcuts, the shared context menu, and debounced content
// push. The first post-migration formatting feature is checkable tasks
// (Docs/Specs/19-CHECKABLE-TASKS.md).

import { EditorState, Annotation, Prec, StateField, StateEffect } from "@codemirror/state";
import { EditorView, keymap, drawSelection, Decoration, ViewPlugin, WidgetType } from "@codemirror/view";
import {
  history,
  historyKeymap,
  standardKeymap,
  undo,
  redo,
  selectAll,
  undoDepth,
  redoDepth,
} from "@codemirror/commands";

// ---------------------------------------------------------------------------
// Constants (parity with AppConstants.MaxBufferLength and the calc/animation specs)
// ---------------------------------------------------------------------------
const MAX_CRLF_LENGTH = 1000000; // AppConstants.MaxBufferLength, counted with CRLF breaks (break = 2)
// Cause values of the limitReached message; the host words its notice from these
// (EditorHost.CausePaste / CauseType).
const CAUSE_PASTE = "paste";
const CAUSE_TYPE = "type";
const BUFFER_MIN = 1;
const BUFFER_MAX = 5;
const SYNC_DEBOUNCE_MS = 300; // matches the retired contentExtractTimer cadence
const CALC_FADE_MS = 1600; // Docs/Specs/12-CALC-RESULT-ANIMATION.md
// Past the 180 ms reveal entrance plus a settle frame: the entrance ending rebuilds the
// compositor layers, which itself re-anchors child animations, so the caret hand-off waits
// until that has passed.
const CARET_RELEASE_DELAY_MS = 240;

// ---------------------------------------------------------------------------
// Host bridge
// ---------------------------------------------------------------------------
const bridge = window.chrome && window.chrome.webview ? window.chrome.webview : null;

function postToHost(type, payload) {
  if (bridge == null) {
    return;
  }
  const message = payload == null ? { type } : Object.assign({ type }, payload);
  bridge.postMessage(message);
}

// Marks transactions the host originated (init, setText) so the debounced
// content-sync push does not echo them straight back to the host.
const HostOrigin = Annotation.define();

// ---------------------------------------------------------------------------
// CRLF-aware length helpers. The document uses "\n" internally; the buffer file
// (and the cap) count each line break as CRLF = 2 chars. Effective length is
// therefore the character count plus one extra per line break.
// ---------------------------------------------------------------------------
function crlfLengthOfDoc(doc) {
  return doc.length + (doc.lines - 1);
}

function crlfLengthOfString(text) {
  let breaks = 0;
  for (let i = 0; i < text.length; i++) {
    if (text.charCodeAt(i) === 10) {
      breaks++;
    }
  }
  return text.length + breaks;
}

// Returns the longest prefix of text whose CRLF length does not exceed budget.
function truncateToCrlfBudget(text, budget) {
  if (budget <= 0) {
    return "";
  }
  let used = 0;
  for (let i = 0; i < text.length; i++) {
    const cost = text.charCodeAt(i) === 10 ? 2 : 1;
    if (used + cost > budget) {
      return text.slice(0, i);
    }
    used += cost;
  }
  return text;
}

// Every clamp the user caused is reported to the host, which throttles the notice and shows it.
// Host-origin transactions (init, setText) are excluded: clamping them is not a user action.
// Only an index, a cause, and a count cross the bridge — never buffer text.
function reportLimitReached(tr, dropped) {
  if (tr.annotation(HostOrigin) != null || dropped <= 0) {
    return;
  }
  const cause = tr.isUserEvent("input.paste") || tr.isUserEvent("input.drop") ? CAUSE_PASTE : CAUSE_TYPE;
  // A transaction filter must not have side effects while it runs; report once the transaction is applied.
  queueMicrotask(() => postToHost("limitReached", { index: activeIndex, cause, dropped }));
}

// Transaction filter enforcing the CRLF cap in one place. Any
// change (typing, paste, IME commit, drop) is truncated so the resulting
// document never exceeds the cap; editor content and persisted content stay
// identical, which is what removes the old editor-vs-disk drift.
const capFilter = EditorState.transactionFilter.of((tr) => {
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

// ---------------------------------------------------------------------------
// Calc result highlight (mark decoration that fades over 1600 ms via CSS).
// ---------------------------------------------------------------------------
const setCalcHighlight = StateEffect.define();
const clearCalcHighlight = StateEffect.define();

const calcHighlightField = StateField.define({
  create() {
    return Decoration.none;
  },
  update(deco, tr) {
    deco = deco.map(tr.changes);
    for (const effect of tr.effects) {
      if (effect.is(setCalcHighlight)) {
        const { from, to, accent } = effect.value;
        deco = Decoration.set([
          Decoration.mark({
            class: "cm-calc-highlight",
            attributes: { style: `--calc-accent:${accent}` },
          }).range(from, to),
        ]);
        return deco;
      }
      if (effect.is(clearCalcHighlight)) {
        return Decoration.none;
      }
    }
    // Any user edit while a highlight is showing cancels it instantly (the calc
    // rewrite itself carries setCalcHighlight above and is handled before this).
    if (tr.docChanged && deco.size > 0) {
      return Decoration.none;
    }
    return deco;
  },
  provide: (field) => EditorView.decorations.from(field),
});

let calcClearTimer = null;

function startCalcHighlight(view, from, to, accent) {
  if (calcClearTimer != null) {
    clearTimeout(calcClearTimer);
    calcClearTimer = null;
  }
  view.dispatch({ effects: setCalcHighlight.of({ from, to, accent }) });
  calcClearTimer = setTimeout(() => {
    calcClearTimer = null;
    view.dispatch({ effects: clearCalcHighlight.of(null) });
  }, CALC_FADE_MS);
}

// ---------------------------------------------------------------------------
// Checkable tasks (Docs/Specs/19-CHECKABLE-TASKS.md). A line starting with
// "- [ ] " / "- [x] " is a task: the marker text stays in the document (and so
// in the buffer file) and renders as a clickable checkbox wherever it appears —
// typed, loaded, or pasted. Typing "[] " or "[ ] " at the start of a line
// converts it to a task; Enter continues the list; Enter on an empty task
// removes the marker; the checkbox click or Ctrl+Enter toggles done. The
// marker is atomic, so Backspace after it deletes it whole, turning the line
// back into plain text.
// ---------------------------------------------------------------------------
const TASK_MARKER_PATTERN = /^- \[( |x|X)\] /;
const TASK_MARKER_UNCHECKED = "- [ ] ";
const TASK_MARKER_LENGTH = TASK_MARKER_UNCHECKED.length;
const TASK_STATE_CHAR_OFFSET = 3; // index of the " "/"x" state char inside the marker
const TASK_SHORTHAND_PREFIXES = ["[]", "[ ]"];

// Returns { checked } when the line carries the task marker, null otherwise.
function taskStateOfLine(line) {
  const match = TASK_MARKER_PATTERN.exec(line.text);
  if (match == null) {
    return null;
  }
  return { checked: match[1] !== " " };
}

class TaskCheckboxWidget extends WidgetType {
  constructor(checked) {
    super();
    this.checked = checked;
  }

  eq(other) {
    return other.checked === this.checked;
  }

  toDOM() {
    const box = document.createElement("span");
    box.className = this.checked ? "cm-task-checkbox cm-task-checked" : "cm-task-checkbox";
    box.setAttribute("aria-hidden", "true");
    return box;
  }

  ignoreEvent() {
    // Let mousedown reach the plugin's handler below, which performs the toggle.
    return false;
  }
}

function toggleTaskAt(view, pos) {
  const line = view.state.doc.lineAt(pos);
  const state = taskStateOfLine(line);
  if (state == null) {
    return false;
  }
  const statePos = line.from + TASK_STATE_CHAR_OFFSET;
  view.dispatch({
    changes: { from: statePos, to: statePos + 1, insert: state.checked ? " " : "x" },
    userEvent: "input",
  });
  return true;
}

// Two decoration sets per viewport pass: the checkbox widgets (also the atomic
// ranges, so the caret never lands inside a marker and Backspace removes it
// whole) and the dim marks on checked task text. Keeping them separate stops
// the dim marks from becoming atomic themselves.
function buildTaskDecorations(view) {
  const checkboxes = [];
  const dimmed = [];
  for (const range of view.visibleRanges) {
    let pos = range.from;
    while (pos <= range.to) {
      const line = view.state.doc.lineAt(pos);
      const state = taskStateOfLine(line);
      if (state != null) {
        const contentFrom = line.from + TASK_MARKER_LENGTH;
        checkboxes.push(
          Decoration.replace({ widget: new TaskCheckboxWidget(state.checked) }).range(line.from, contentFrom)
        );
        if (state.checked && line.to > contentFrom) {
          dimmed.push(Decoration.mark({ class: "cm-task-done" }).range(contentFrom, line.to));
        }
      }
      pos = line.to + 1;
    }
  }
  return { checkboxes: Decoration.set(checkboxes), dimmed: Decoration.set(dimmed) };
}

const taskPlugin = ViewPlugin.fromClass(
  class {
    constructor(view) {
      const built = buildTaskDecorations(view);
      this.checkboxes = built.checkboxes;
      this.dimmed = built.dimmed;
    }

    update(update) {
      if (update.docChanged || update.viewportChanged) {
        const built = buildTaskDecorations(update.view);
        this.checkboxes = built.checkboxes;
        this.dimmed = built.dimmed;
      }
    }
  },
  {
    decorations: (value) => value.checkboxes,
    provide: (plugin) => [
      EditorView.decorations.of((view) => {
        const value = view.plugin(plugin);
        return value == null ? Decoration.none : value.dimmed;
      }),
      EditorView.atomicRanges.of((view) => {
        const value = view.plugin(plugin);
        return value == null ? Decoration.none : value.checkboxes;
      }),
    ],
    eventHandlers: {
      mousedown(event, view) {
        const target = event.target;
        if (target instanceof Element && target.classList.contains("cm-task-checkbox")) {
          return toggleTaskAt(view, view.posAtDOM(target));
        }
        return false;
      },
    },
  }
);

// Space keymap: converting the Notion shorthand. Runs only when everything
// from the line start to the caret is exactly "[]" or "[ ]"; any text after
// the caret becomes the task's content. Otherwise the space inserts as usual.
function convertTaskShorthand(view) {
  const selection = view.state.selection.main;
  if (!selection.empty) {
    return false;
  }
  const line = view.state.doc.lineAt(selection.head);
  const beforeCaret = view.state.doc.sliceString(line.from, selection.head);
  if (TASK_SHORTHAND_PREFIXES.indexOf(beforeCaret) < 0) {
    return false;
  }
  view.dispatch({
    changes: { from: line.from, to: selection.head, insert: TASK_MARKER_UNCHECKED },
    selection: { anchor: line.from + TASK_MARKER_LENGTH },
    userEvent: "input.type",
    scrollIntoView: true,
  });
  return true;
}

// Enter on a task line: an empty task exits the list (the marker is removed),
// any other position continues it with a fresh unchecked task. Enter at the
// very start of the line (before the checkbox) falls through to the default
// newline, which pushes the task down — matching Notion.
function handleTaskEnter(view) {
  const selection = view.state.selection.main;
  if (!selection.empty) {
    return false;
  }
  const line = view.state.doc.lineAt(selection.head);
  if (taskStateOfLine(line) == null) {
    return false;
  }
  if (line.text.length === TASK_MARKER_LENGTH) {
    view.dispatch({
      changes: { from: line.from, to: line.to },
      userEvent: "delete",
    });
    return true;
  }
  if (selection.head < line.from + TASK_MARKER_LENGTH) {
    return false;
  }
  view.dispatch(view.state.replaceSelection("\n" + TASK_MARKER_UNCHECKED), {
    userEvent: "input.type",
    scrollIntoView: true,
  });
  return true;
}

function toggleTaskAtCaret(view) {
  return toggleTaskAt(view, view.state.selection.main.head);
}

const taskKeymap = keymap.of([
  { key: "Enter", run: handleTaskEnter },
  { key: "Mod-Enter", run: toggleTaskAtCaret },
  { key: "Space", run: convertTaskShorthand },
]);

// ---------------------------------------------------------------------------
// Panel keyboard shortcuts. Keys typed inside the web editor never reach XAML
// PreviewKeyDown, so the panel-level shortcuts are captured here (highest
// precedence) and forwarded to the host, which performs the action.
// ---------------------------------------------------------------------------
function sendKey(command) {
  postToHost("key", { command });
  return true;
}

const panelKeymap = Prec.highest(
  keymap.of([
    // Tab / Shift+Tab cycle buffers; Tab never inserts a tab or moves DOM focus.
    { key: "Tab", preventDefault: true, run: () => sendKey("cycle-next"), shift: () => sendKey("cycle-prev") },
    { key: "Mod-Tab", preventDefault: true, run: () => sendKey("cycle-next") },
    { key: "Mod-Shift-Tab", preventDefault: true, run: () => sendKey("cycle-prev") },
    { key: "Mod-1", preventDefault: true, run: () => sendKey("select-1") },
    { key: "Mod-2", preventDefault: true, run: () => sendKey("select-2") },
    { key: "Mod-3", preventDefault: true, run: () => sendKey("select-3") },
    { key: "Mod-4", preventDefault: true, run: () => sendKey("select-4") },
    { key: "Mod-5", preventDefault: true, run: () => sendKey("select-5") },
    { key: "F2", preventDefault: true, run: () => sendKey("edit-flyout") },
    // Plain text: bold/italic/underline are no-ops (swallowed so nothing toggles).
    { key: "Mod-b", preventDefault: true, run: () => true },
    { key: "Mod-i", preventDefault: true, run: () => true },
    { key: "Mod-u", preventDefault: true, run: () => true },
    // Standard Windows text-box selection of the whole document.
    { key: "Mod-a", preventDefault: true, run: selectAll },
  ])
);

// ---------------------------------------------------------------------------
// Content-sync push (replaces the host-side extract pull). Per-buffer latest
// text is pushed to the host on a 300 ms debounce, and immediately on blur,
// tab switch, and host flush requests.
// ---------------------------------------------------------------------------
const pendingSync = new Map(); // index -> "\n"-joined text
let syncTimer = null;

function queueSync(index, text) {
  pendingSync.set(index, text);
  if (syncTimer != null) {
    clearTimeout(syncTimer);
  }
  syncTimer = setTimeout(flushSync, SYNC_DEBOUNCE_MS);
}

function flushSync() {
  if (syncTimer != null) {
    clearTimeout(syncTimer);
    syncTimer = null;
  }
  if (pendingSync.size === 0) {
    return;
  }
  for (const [index, text] of pendingSync) {
    postToHost("contentSync", { index, text });
  }
  pendingSync.clear();
}

// ---------------------------------------------------------------------------
// Inline-calc detection. The page recognises a user-typed "=" that ends a line
// (layout-independent — it is the composed character) and asks the host to
// evaluate; the host replies and the page rewrites the line here.
// ---------------------------------------------------------------------------
let pendingCalc = null; // { index, from, lineText }

function detectCalc(update) {
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
    pendingCalc = { index: activeIndex, from: line.from, lineText: line.text };
    postToHost("calcRequest", { index: activeIndex, lineContent: line.text });
    return;
  }
}

function applyCalcResult(message) {
  const token = pendingCalc;
  pendingCalc = null;
  if (token == null || !message.ok || token.index !== activeIndex) {
    return;
  }
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
  startCalcHighlight(view, highlightFrom, highlightTo, currentAccent);
}

// ---------------------------------------------------------------------------
// The single EditorView and the five per-buffer states.
// ---------------------------------------------------------------------------
// The host overwrites this from SystemAccentColor on the `theme` message, which arrives immediately
// after `ready` and so before any calc can run. Until then fall back to the CSS system colour for the
// OS selection highlight rather than a hardcoded blue, which would misreport the user's accent.
let currentAccent = "Highlight";
let activeIndex = BUFFER_MIN;
const states = new Map(); // index -> EditorState
// CM6 keeps scroll in the DOM, not the EditorState, so per-buffer scroll must be preserved across
// tab switches. scrollSnapshot() captures the exact position as an effect that restores precisely
// after the state is re-measured (raw scrollTop races the re-measure and lands a few lines off).
const scrollSnapshotByIndex = new Map(); // index -> StateEffect

// Editor surface styling lives in a CodeMirror theme (not plain CSS) so it reliably wins over
// CM6's injected base theme: Cascadia Code 15px, line-height 1.4. The retired RichEditBox padding
// (16/10/16/16 plus the 4px content top gap) is now a WebView2 margin, not content padding, so it
// cannot scroll away. Colours read the CSS variables the host sets via the theme message.
const editorTheme = EditorView.theme({
  "&": {
    height: "100%",
    backgroundColor: "transparent",
    color: "var(--text)",
  },
  "&.cm-focused": { outline: "none" },
  ".cm-scroller": {
    fontFamily: "'Cascadia Code', 'Cascadia Mono', Consolas, monospace",
    fontSize: "15px",
    // The RichEditBox used LineSpacingRule.Multiple 1.4, which multiplies the font's natural line
    // height (~1.2em); CSS line-height multiplies the em directly, so ~1.6 matches that spacing.
    lineHeight: "1.6",
    overflowX: "hidden",
    // Chromium's ::-webkit-scrollbar is a classic scrollbar: it consumes layout width the moment the
    // buffer becomes scrollable, which would shrink the content box (and the selection's right edge)
    // by SCROLLBAR_WIDTH_PX. Reserving the gutter always keeps the text inset identical whether or
    // not a scrollbar is showing. The WebView2's right margin is narrowed by the same amount so the
    // reserved gutter sits inside the edge gap instead of adding to it.
    scrollbarGutter: "stable",
  },
  ".cm-content": {
    // No padding at all: every edge inset is a margin on the WebView2 element, so it stays put
    // during scroll (CM6 content padding lives inside the scrolled region and scrolls away).
    padding: "0",
    caretColor: "var(--caret)",
  },
  ".cm-line": { padding: "0" },
  ".cm-cursor, .cm-dropCursor": {
    borderLeftColor: "var(--caret)",
    // CM6's base theme offsets the caret by margin-left: -0.6px to straddle the character boundary.
    // .cm-cursorLayer is a direct child of .cm-scroller, which clips at overflow-x: hidden, so with
    // no content padding a column-0 caret puts half its 1.2px border outside the clip and all but
    // disappears. Sit it flush inside the boundary instead; the 0.6px shift is imperceptible.
    marginLeft: "0",
  },
  ".cm-selectionBackground": { background: "var(--selection)" },
  // The focused rule must mirror CM6's base-theme selector shape exactly. Its
  // `&light.cm-focused > .cm-scroller > .cm-selectionLayer .cm-selectionBackground` scores (0,5,0);
  // a shorter `&.cm-focused .cm-selectionBackground` scores (0,3,0) and loses, so the base theme's
  // own selection colour would show whenever the editor has focus — i.e. always. Matching the shape
  // ties the specificity, and the base theme is Prec.lowest, so ours is mounted later and wins.
  "&.cm-focused > .cm-scroller > .cm-selectionLayer .cm-selectionBackground": {
    background: "var(--selection)",
  },
});

const baseExtensions = [
  history(),
  panelKeymap,
  // Registered before standardKeymap so the task Enter/Space handlers get
  // first refusal; they return false whenever the caret is not on a task.
  taskKeymap,
  keymap.of([...historyKeymap, ...standardKeymap]),
  taskPlugin,
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
        queueSync(activeIndex, update.state.doc.toString());
      }
      detectCalc(update);
    }
  }),
];

function makeState(text) {
  return EditorState.create({
    doc: normaliseIncoming(text),
    extensions: baseExtensions,
  });
}

// Host text arrives with CRLF (disk convention); the document uses "\n".
function normaliseIncoming(text) {
  if (text == null) {
    return "";
  }
  return text.replace(/\r\n?/g, "\n");
}

// The blink hold is active from the first frame; the host releases it at first visibility.
document.documentElement.classList.add("caret-hold");

const view = new EditorView({
  state: makeState(""),
  parent: document.getElementById("editor"),
});

// The result highlight is one animation at a time and never plays on load; it
// is only started from applyCalcResult, so nothing to do here.

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

// ---------------------------------------------------------------------------
// State activation (tab switch). Per-buffer undo/selection/scroll live in the
// EditorState and survive the swap.
// ---------------------------------------------------------------------------
function activate(index) {
  if (index >= BUFFER_MIN && index <= BUFFER_MAX && index !== activeIndex) {
    // Persist pending edits and capture the exact scroll position of the buffer being switched
    // away from before swapping in the target buffer's state.
    flushSync();
    scrollSnapshotByIndex.set(activeIndex, view.scrollSnapshot());
    states.set(activeIndex, view.state);
    activeIndex = index;
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

// Replays the tab-content entrance (fade + slide-up) on the editor itself, inside Chromium, so the
// WebView2 element's own opacity is never animated from XAML (which flashes black on light theme).
// The fixed gradient layer (#bg) is separate and stays put.
function playEntrance() {
  const el = view.dom;
  el.classList.remove("cm-entering");
  void el.offsetWidth; // force reflow so the animation restarts on every activation
  el.classList.add("cm-entering");
}

function setText(index, text) {
  const doc = normaliseIncoming(text);
  if (index === activeIndex) {
    view.dispatch({
      changes: { from: 0, to: view.state.doc.length, insert: doc },
      annotations: HostOrigin.of(true),
    });
  } else {
    states.set(index, makeState(text));
  }
}

// ---------------------------------------------------------------------------
// Incoming host messages.
// ---------------------------------------------------------------------------
function handleCommand(name) {
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

// Hands the caret from the startup hold (see editor.css: blink suppressed, caret solid) to a
// fresh blink cycle without a single visible mid-phase frame. Two ordered steps: restart the
// blink while the hold still masks it (the selection re-dispatch makes the cursor layer swap
// its animation name — CM6's own restart mechanism), then drop the mask two frames later, once
// the swapped name has committed. The unmasked animation is brand new, so it anchors at the
// release frame in its solid phase. Releasing in the same tick as the restart is not safe: the
// release frame briefly re-exposes the previous animation, which this engine anchors to the
// wall clock, so it can surface mid-blink (captured as a one-frame caret blank). Also the
// whole release path for a page reload after an editor-process failure, where the hold is
// re-applied by the fresh page load.
function resetCaretBlink() {
  view.dispatch({ selection: view.state.selection });
  requestAnimationFrame(() => {
    requestAnimationFrame(() => {
      document.documentElement.classList.remove("caret-hold");
    });
  });
}

// Startup reveal entrance. The host uncloaks the window with its flat startup cover still up
// (Chromium may not have presented fresh frames while the window was cloaked, so the uncloak
// instant cannot be trusted to show current content), then asks for this: after two rAFs — a
// frame demonstrably presented while visible — the editor replays its entrance (fade + slide
// from opacity 0, indistinguishable from the flat cover at its start), the caret-blink hold is
// released so the caret fades in solid with a full phase ahead, and the host is told to drop
// the cover. Every hand-off lands inside the animation, so no timing race can show as a pop.
function beginRevealEntrance() {
  requestAnimationFrame(() => {
    requestAnimationFrame(() => {
      playEntrance();
      postToHost("entranceStarted");
      // The caret fades in with the entrance, held solid; hand it to the live blink cycle
      // only after the entrance has finished.
      setTimeout(resetCaretBlink, CARET_RELEASE_DELAY_MS);
    });
  });
}

function insertClamped(text) {
  const normalised = normaliseIncoming(text);
  view.focus();
  view.dispatch(
    view.state.replaceSelection(normalised),
    { userEvent: "input.paste" }
  );
}

function handleHostMessage(message) {
  switch (message.type) {
    case "init": {
      for (const buffer of message.buffers) {
        states.set(buffer.index, makeState(buffer.text));
      }
      activeIndex = message.buffers.length > 0 ? message.buffers[0].index : BUFFER_MIN;
      const first = states.get(activeIndex);
      if (first != null) {
        view.setState(first);
      }
      break;
    }
    case "activate":
      activate(message.index);
      break;
    case "setText":
      setText(message.index, message.text);
      break;
    case "focus":
      view.focus();
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

function applyTheme(message) {
  const root = document.documentElement;
  if (message.text) {
    root.style.setProperty("--text", message.text);
  }
  if (message.caret) {
    root.style.setProperty("--caret", message.caret);
  }
  if (message.selection) {
    root.style.setProperty("--selection", message.selection);
  }
  if (message.accent) {
    currentAccent = message.accent;
    root.style.setProperty("--accent", message.accent);
  }
}

function applyBackground(message) {
  // #bg already fills the viewport (100% x 100%); the native-resolution PNG is stretched to it,
  // which maps it 1:1 to device pixels (viewport CSS size x devicePixelRatio = the PNG's size).
  const bg = document.getElementById("bg");
  bg.style.backgroundImage = "url(data:image/png;base64," + message.pngBase64 + ")";
  bg.style.backgroundSize = "100% 100%";

  // Tell the host the gradient + text have composited, so it can drop the startup cover that hides
  // the black WebView2 swapchain. Two rAFs guarantee a painted frame before we signal.
  requestAnimationFrame(() => {
    requestAnimationFrame(() => postToHost("painted"));
  });
}

if (bridge != null) {
  bridge.addEventListener("message", (event) => {
    handleHostMessage(event.data);
  });
}

// Signal the host that the page and CM6 are ready to receive init/activate.
postToHost("ready");
