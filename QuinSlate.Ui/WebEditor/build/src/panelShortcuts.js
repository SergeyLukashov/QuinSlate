// Panel keyboard shortcuts. Keys typed inside the web editor never reach XAML
// PreviewKeyDown, so the panel-level shortcuts are captured here (highest
// precedence) and forwarded to the host, which performs the action.

import { Prec } from "@codemirror/state";
import { keymap } from "@codemirror/view";
import { selectAll } from "@codemirror/commands";
import { postToHost } from "./hostBridge.js";

function sendKey(command) {
  postToHost("key", { command });
  return true;
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
    // Plain text: bold/italic/underline are no-ops (swallowed so nothing toggles).
    { key: "Mod-b", preventDefault: true, run: () => true },
    { key: "Mod-i", preventDefault: true, run: () => true },
    { key: "Mod-u", preventDefault: true, run: () => true },
    // Standard Windows text-box selection of the whole document.
    { key: "Mod-a", preventDefault: true, run: selectAll },
  ])
);
