// The WebView2 host bridge: the page's single channel to Components/EditorHost.cs.
// The "never log buffer contents" rule extends across this bridge — messages
// carrying buffer text are never logged on either side.

import { Annotation } from "@codemirror/state";

const bridge = window.chrome && window.chrome.webview ? window.chrome.webview : null;

export function postToHost(type, payload) {
  if (bridge == null) {
    return;
  }
  const message = payload == null ? { type } : Object.assign({ type }, payload);
  bridge.postMessage(message);
}

export function onHostMessage(handler) {
  if (bridge == null) {
    return;
  }
  bridge.addEventListener("message", (event) => {
    handler(event.data);
  });
}

// Marks transactions the host originated (init, setText) so the debounced
// content-sync push does not echo them straight back to the host.
export const HostOrigin = Annotation.define();
