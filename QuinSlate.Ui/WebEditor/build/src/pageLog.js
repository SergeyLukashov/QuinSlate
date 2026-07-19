// Page-side logging, routed over the host bridge into the app's Serilog
// pipeline: every "log" message is forwarded by Components/EditorPageLogForwarder.cs
// to the same rolling file sink the C# side writes to, under its own source
// context. Levels mirror Serilog's, so release builds drop "debug" entries
// exactly like C# Debug breadcrumbs.
//
// The "never log buffer contents" rule applies to every call site: log message
// names, indices, counts, and lengths — never document text. Runtime error
// messages and stack traces are code locations, not document text, so the
// global handlers below are safe to forward.

import { postToHost } from "./hostBridge.js";

const LogMessageType = "log";

export const LevelDebug = "debug";
export const LevelInformation = "information";
export const LevelWarning = "warning";
export const LevelError = "error";

// Guards against a runaway loop (e.g. an error thrown on every animation
// frame) flooding the bridge and the log file: each distinct message is
// forwarded at most this many times per page load, the last one annotated so
// the log shows the silencing.
const MaxRepeatsPerMessage = 10;
const SuppressionNotice = " (repeated; further occurrences suppressed)";
const MaxMessageLength = 2000;
const MaxStackLength = 2000;
const UnknownErrorMessage = "Unknown script error";

const repeatCounts = new Map();

function truncate(text, maxLength) {
  if (typeof text !== "string") {
    return "";
  }
  return text.length > maxLength ? text.slice(0, maxLength) + "…" : text;
}

function post(level, message, stack) {
  const count = (repeatCounts.get(message) || 0) + 1;
  repeatCounts.set(message, count);
  if (count > MaxRepeatsPerMessage) {
    return;
  }
  const payload = {
    level,
    message: truncate(message, MaxMessageLength) +
      (count === MaxRepeatsPerMessage ? SuppressionNotice : ""),
  };
  if (stack != null && stack !== "") {
    payload.stack = truncate(stack, MaxStackLength);
  }
  postToHost(LogMessageType, payload);
}

export function logDebug(message) {
  post(LevelDebug, message, null);
}

export function logInformation(message) {
  post(LevelInformation, message, null);
}

export function logWarning(message) {
  post(LevelWarning, message, null);
}

// The optional error carries the stack; the message itself is the caller's.
export function logError(message, error) {
  const stack = error && typeof error.stack === "string" ? error.stack : null;
  post(LevelError, message, stack);
}

// "TypeError: x is not a function" — for embedding a caught error (or a
// promise rejection reason, which need not be an Error) into a log message.
export function describeError(error) {
  if (error instanceof Error) {
    return error.name + ": " + error.message;
  }
  return truncate(String(error), MaxMessageLength);
}

// Uncaught exceptions (including a failure while the modules themselves load,
// as long as this ran first) and unhandled promise rejections end up in the
// host's log instead of vanishing into a DevTools console nobody has open.
export function registerGlobalErrorLogging() {
  if (typeof window.addEventListener !== "function") {
    return;
  }
  window.addEventListener("error", (event) => {
    const where = event.filename
      ? " at " + event.filename + ":" + event.lineno + ":" + event.colno
      : "";
    logError((event.message || UnknownErrorMessage) + where, event.error);
  });
  window.addEventListener("unhandledrejection", (event) => {
    logError("Unhandled promise rejection: " + describeError(event.reason), event.reason);
  });
}
