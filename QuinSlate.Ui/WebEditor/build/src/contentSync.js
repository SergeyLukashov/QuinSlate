// Content-sync push (replaces the host-side extract pull). Per-buffer latest
// text is pushed to the host on a 300 ms debounce, and immediately on blur,
// tab switch, and host flush requests.

import { postToHost } from "./hostBridge.js";

const SYNC_DEBOUNCE_MS = 300; // matches the retired contentExtractTimer cadence

const pendingSync = new Map(); // index -> "\n"-joined text
let syncTimer = null;

export function queueSync(index, text) {
  pendingSync.set(index, text);
  if (syncTimer != null) {
    clearTimeout(syncTimer);
  }
  syncTimer = setTimeout(flushSync, SYNC_DEBOUNCE_MS);
}

export function flushSync() {
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
