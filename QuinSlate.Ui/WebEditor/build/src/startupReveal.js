// The startup reveal: the caret-blink hold and the entrance the host requests
// once the window is uncloaked. Every hand-off lands inside the animation, so
// no timing race can show as a pop.

import { postToHost } from "./hostBridge.js";
import { getView } from "./editorContext.js";
import { playEntrance } from "./entrance.js";

// Past the 180 ms reveal entrance plus a settle frame: the entrance ending rebuilds the
// compositor layers, which itself re-anchors child animations, so the caret hand-off waits
// until that has passed.
const CARET_RELEASE_DELAY_MS = 240;

// The blink hold must be active from the first frame; the host releases it at first visibility.
export function applyCaretHold() {
  document.documentElement.classList.add("caret-hold");
}

// Hands the caret from the startup hold (see editor.css: blink suppressed, caret solid) to a
// fresh blink cycle without a single visible mid-phase frame. Two ordered steps: restart the
// blink while the hold still masks it (the selection re-dispatch makes the cursor layer swap
// its animation name — CM6's own restart mechanism), then drop the mask two frames later, once
// the swapped name has committed. The unmasked animation is brand new, so it anchors at the
// release frame in its solid phase. Releasing in the same tick as the restart is not safe: the
// release frame briefly re-exposes the previous animation, which this engine anchors to the
// wall clock, so it can surface mid-blink (captured as a one-frame caret blank). Also the
// whole release path for a page reload after an editor-process failure, where the hold is
// re-applied by the fresh page load.
export function resetCaretBlink() {
  const view = getView();
  view.dispatch({ selection: view.state.selection });
  requestAnimationFrame(() => {
    requestAnimationFrame(() => {
      document.documentElement.classList.remove("caret-hold");
    });
  });
}

// Startup reveal entrance. The host uncloaks the window with its flat startup cover still up
// (Chromium may not have presented fresh frames while the window was cloaked, so the uncloak
// instant cannot be trusted to show current content), then asks for this: after two rAFs — a
// frame demonstrably presented while visible — the editor replays its entrance (fade + slide
// from opacity 0, indistinguishable from the flat cover at its start), the caret-blink hold is
// released so the caret fades in solid with a full phase ahead, and the host is told to drop
// the cover. Every hand-off lands inside the animation, so no timing race can show as a pop.
export function beginRevealEntrance() {
  requestAnimationFrame(() => {
    requestAnimationFrame(() => {
      playEntrance();
      postToHost("entranceStarted");
      // The caret fades in with the entrance, held solid; hand it to the live blink cycle
      // only after the entrance has finished.
      setTimeout(resetCaretBlink, CARET_RELEASE_DELAY_MS);
    });
  });
}
