// pageLog.js: what reaches the host as a "log" message — levels, stack
// attachment, truncation, flood suppression, and the global error handlers.
//
// hostBridge.js captures window.chrome.webview as its module body runs, so the
// capturing stub must be installed before pageLog is imported — hence the
// dynamic import below (each test file is its own process, so this cannot
// leak into the other suites).

import { describe, it, beforeEach } from "node:test";
import assert from "node:assert/strict";

const posted = [];
const windowListeners = new Map();

globalThis.window.chrome = {
  webview: {
    postMessage(message) {
      posted.push(message);
    },
    addEventListener() {},
  },
};
globalThis.window.addEventListener = (name, handler) => {
  windowListeners.set(name, handler);
};

const {
  logDebug,
  logInformation,
  logWarning,
  logError,
  describeError,
  registerGlobalErrorLogging,
} = await import("../src/pageLog.js");

beforeEach(() => {
  posted.length = 0;
});

describe("log levels", () => {
  it("posts a 'log' message with the level and the message", () => {
    logInformation("editor initialised");
    assert.deepEqual(posted, [
      { type: "log", level: "information", message: "editor initialised" },
    ]);
  });

  it("posts debug, warning, and error at their own levels", () => {
    logDebug("a debug note");
    logWarning("a warning note");
    logError("an error note");
    assert.deepEqual(posted.map((message) => message.level), ["debug", "warning", "error"]);
  });
});

describe("error stacks", () => {
  it("attaches the stack of an Error passed to logError", () => {
    const error = new Error("boom");
    logError("something failed", error);
    assert.equal(posted.length, 1);
    assert.equal(posted[0].stack, error.stack);
  });

  it("omits the stack property when there is no error", () => {
    logError("no error object");
    assert.equal(Object.hasOwn(posted[0], "stack"), false);
  });

  it("omits the stack when the rejection reason is not an Error", () => {
    logError("string reason", "just a string");
    assert.equal(Object.hasOwn(posted[0], "stack"), false);
  });
});

describe("truncation", () => {
  it("truncates an over-long message", () => {
    logInformation("x".repeat(5000));
    assert.equal(posted[0].message.length, 2001);
    assert.ok(posted[0].message.endsWith("…"));
  });

  it("truncates an over-long stack", () => {
    logError("short message", { stack: "y".repeat(5000) });
    assert.equal(posted[0].stack.length, 2001);
  });
});

describe("flood suppression", () => {
  it("forwards a repeating message 10 times, annotating the last, then goes silent", () => {
    for (let i = 0; i < 15; i++) {
      logWarning("the same warning");
    }
    assert.equal(posted.length, 10);
    assert.equal(posted[8].message, "the same warning");
    assert.equal(posted[9].message, "the same warning (repeated; further occurrences suppressed)");
  });

  it("keeps counting per distinct message", () => {
    logWarning("suppress me");
    logWarning("but not me");
    assert.equal(posted.length, 2);
  });
});

describe("describeError", () => {
  it("formats an Error as name: message", () => {
    assert.equal(describeError(new TypeError("x is not a function")), "TypeError: x is not a function");
  });

  it("stringifies a non-Error reason", () => {
    assert.equal(describeError(42), "42");
  });
});

describe("global error handlers", () => {
  registerGlobalErrorLogging();

  it("registers for error and unhandledrejection", () => {
    assert.ok(windowListeners.has("error"));
    assert.ok(windowListeners.has("unhandledrejection"));
  });

  it("logs an uncaught error with its source location and stack", () => {
    const error = new Error("kaput");
    windowListeners.get("error")({
      message: "Uncaught Error: kaput",
      filename: "https://quinslate.editor/editor.bundle.js",
      lineno: 12,
      colno: 34,
      error,
    });
    assert.equal(posted.length, 1);
    assert.equal(posted[0].level, "error");
    assert.equal(
      posted[0].message,
      "Uncaught Error: kaput at https://quinslate.editor/editor.bundle.js:12:34"
    );
    assert.equal(posted[0].stack, error.stack);
  });

  it("falls back to a fixed message when the event carries none", () => {
    windowListeners.get("error")({ message: "", filename: "", lineno: 0, colno: 0 });
    assert.equal(posted[0].message, "Unknown script error");
  });

  it("logs an unhandled rejection, describing a non-Error reason", () => {
    windowListeners.get("unhandledrejection")({ reason: "nope" });
    assert.equal(posted[0].level, "error");
    assert.equal(posted[0].message, "Unhandled promise rejection: nope");
  });
});
