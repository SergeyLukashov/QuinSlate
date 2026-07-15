// Replays the tab-content entrance (fade + slide-up) on the editor itself, inside Chromium, so the
// WebView2 element's own opacity is never animated from XAML (which flashes black on light theme).
// The fixed gradient layer (#bg) is separate and stays put.

import { getView } from "./editorContext.js";

export function playEntrance() {
  const el = getView().dom;
  el.classList.remove("cm-entering");
  void el.offsetWidth; // force reflow so the animation restarts on every activation
  el.classList.add("cm-entering");
}
