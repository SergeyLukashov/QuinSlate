// QuinSlate editor page entry point: CodeMirror 6 hosting five plain-text
// buffers in one EditorView, driven entirely over the WebView2 host bridge.
// Like-for-like replacement of the retired RichEditBox editors (see
// Docs/Specs/17-EDITOR-CODEMIRROR-MIGRATION.md): plain text, the character cap
// (charLimit.js), inline-calc detection with evaluation staying in C#
// (inlineCalc.js, calcHighlight.js), panel keyboard shortcuts
// (panelShortcuts.js), the shared context menu (editorSetup.js), debounced
// content push (contentSync.js), checkable tasks (tasks.js), and bullet /
// numbered lists (lists.js).

import { postToHost, onHostMessage } from "./hostBridge.js";
import { applyCaretHold } from "./startupReveal.js";
import { createEditor } from "./editorSetup.js";
import { handleHostMessage } from "./hostMessages.js";

applyCaretHold();
createEditor();
onHostMessage(handleHostMessage);

// Signal the host that the page and CM6 are ready to receive init/activate.
postToHost("ready");
