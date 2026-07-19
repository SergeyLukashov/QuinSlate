// hostMessages.js: the dispatch guard. A throwing handler or an unknown
// message type is reported to the host's log (names only, never payloads)
// instead of detaching the bridge listener or vanishing silently.
//
// The capturing bridge stub must exist before the modules load (hostBridge.js
// reads window.chrome.webview in its module body), hence the dynamic import.

import { describe, it, beforeEach } from "node:test";
import assert from "node:assert/strict";

const posted = [];

globalThis.window.chrome = {
  webview: {
    postMessage(message) {
      posted.push(message);
    },
    addEventListener() {},
  },
};

const { handleHostMessage } = await import("../src/hostMessages.js");

beforeEach(() => {
  posted.length = 0;
});

describe("host message dispatch guard", () => {
  it("logs a warning naming an unknown message type", () => {
    handleHostMessage({ type: "definitelyNotAMessage" });
    assert.equal(posted.length, 1);
    assert.equal(posted[0].type, "log");
    assert.equal(posted[0].level, "warning");
    assert.equal(posted[0].message, "Unknown host message type 'definitelyNotAMessage'.");
  });

  it("logs a throwing handler as an error naming the message type, and does not throw", () => {
    // No EditorView has been created in this process, so the focus handler throws.
    handleHostMessage({ type: "focus" });
    assert.equal(posted.length, 1);
    assert.equal(posted[0].level, "error");
    assert.match(posted[0].message, /^Handling the host message 'focus' failed: TypeError/);
    assert.equal(typeof posted[0].stack, "string");
  });
});
