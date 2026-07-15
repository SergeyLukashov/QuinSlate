// Host-driven appearance: the theme colours (CSS variables) and the
// host-rendered dithered gradient background PNG.

import { postToHost } from "./hostBridge.js";
import { setAccent } from "./editorContext.js";

export function applyTheme(message) {
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
    setAccent(message.accent);
    root.style.setProperty("--accent", message.accent);
  }
}

export function applyBackground(message) {
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
