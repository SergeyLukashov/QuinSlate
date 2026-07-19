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
import { registerGlobalErrorLogging, logInformation } from "./pageLog.js";
import { applyCaretHold } from "./startupReveal.js";
import { createEditor } from "./editorSetup.js";
import { handleHostMessage } from "./hostMessages.js";

// First, so a failure anywhere in the start-up below reaches the host's log.
registerGlobalErrorLogging();
applyCaretHold();
createEditor();
onHostMessage(handleHostMessage);

// Signal the host that the page and CM6 are ready to receive init/activate.
postToHost("ready");

// A once-per-load lifecycle breadcrumb (no buffer content): confirms the page
// booted CM6 and the log bridge is live, and marks the page's start in the
// shared log next to the host's own startup entries.
logInformation("Editor page ready.");
