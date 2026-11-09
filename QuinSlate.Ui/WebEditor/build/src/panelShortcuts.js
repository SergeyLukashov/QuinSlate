// Panel keyboard shortcuts. Keys typed inside the web editor never reach XAML
// PreviewKeyDown, so the panel-level shortcuts are captured here (highest
// precedence) and forwarded to the host, which performs the action.

import { Prec } from "@codemirror/state";
import { keymap } from "@codemirror/view";
import { selectAll } from "@codemirror/commands";
import { postToHost } from "./hostBridge.js";
import { isSearchOpen } from "./search.js";
import { visibleSlice } from "./searchFold.js";

// A selection longer than this is not what anyone meant to search for, so it does not seed the query.
const MAX_SEED_LENGTH = 256;
const LINE_BREAK = "\n";

function sendKey(command, extra) {
  postToHost("key", Object.assign({ command }, extra));
  return true;
}

// Ctrl+F seeds the query from a non-empty selection within one line (Docs/Specs/24-SEARCH.md §
// Keyboard), minus the markdown markers folding leaves out of the text: the caret line shows its
// markers, so a drag selection picks them up, and a query keeps them as characters to find. The
// length cap applies to the selection as made. The seed is buffer text: it crosses the bridge and
// is never logged.
function openSearch(view) {
  const selection = view.state.selection.main;
  const selected = view.state.doc.sliceString(selection.from, selection.to);
  if (selected.length === 0 || selected.length > MAX_SEED_LENGTH || selected.indexOf(LINE_BREAK) >= 0) {
    return sendKey("find-open");
  }
  const line = view.state.doc.lineAt(selection.from);
  const seed = visibleSlice(line.text, selection.from - line.from, selection.to - line.from);
  return seed.length === 0 ? sendKey("find-open") : sendKey("find-open", { seed });
}

// Esc is bound only while the pill is open; at any other time it stays unbound, as it always was.
function closeSearch() {
  return isSearchOpen() ? sendKey("find-close") : false;
}

export const panelKeymap = Prec.highest(
  keymap.of([
    // Ctrl+Tab / Ctrl+Shift+Tab cycle buffers. Plain Tab belongs to indentation
    // (indent.js) — it never cycles, inserts a tab, or moves DOM focus.
    { key: "Mod-Tab", preventDefault: true, run: () => sendKey("cycle-next") },
    { key: "Mod-Shift-Tab", preventDefault: true, run: () => sendKey("cycle-prev") },
    { key: "Mod-1", preventDefault: true, run: () => sendKey("select-1") },
    { key: "Mod-2", preventDefault: true, run: () => sendKey("select-2") },
    { key: "Mod-3", preventDefault: true, run: () => sendKey("select-3") },
    { key: "Mod-4", preventDefault: true, run: () => sendKey("select-4") },
    { key: "Mod-5", preventDefault: true, run: () => sendKey("select-5") },
    { key: "F2", preventDefault: true, run: () => sendKey("edit-flyout") },
    { key: "Mod-f", preventDefault: true, run: openSearch },
    { key: "F3", preventDefault: true, run: () => sendKey("find-next") },
    { key: "Shift-F3", preventDefault: true, run: () => sendKey("find-prev") },
    { key: "Escape", run: closeSearch },
    // Plain text: bold/italic/underline are no-ops (swallowed so nothing toggles).
    { key: "Mod-b", preventDefault: true, run: () => true },
    { key: "Mod-i", preventDefault: true, run: () => true },
    { key: "Mod-u", preventDefault: true, run: () => true },
    // Standard Windows text-box selection of the whole document.
    { key: "Mod-a", preventDefault: true, run: selectAll },
  ])
);
