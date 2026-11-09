// Room for the search pill at the top of the slate (Docs/Specs/24-SEARCH.md § The pill), the way
// VS Code's find widget adds space on top. While the pill is open the content gains a top padding as
// tall as the pill. It is added above the viewport, so opening search moves nothing: the first
// lines stay where they were, and scrolling up past them brings them out from under the pill. The
// content's minimum height grows by the same amount (editorTheme.js), so even a slate too short to
// scroll has the room to hide the spacer and to reveal it.
//
// Every change to the spacer is eased, and each frame moves the scroll position by exactly the
// padding it added or removed. Text therefore only moves where the scroll position cannot follow:
// on close, when the user had scrolled up into the spacer, the text glides back up to the top.

const SPACER_PROPERTY = "--search-spacer";
const PX = "px";
// Matches the pill's own slide-and-fade (PillMotion.DurationMilliseconds).
const DURATION_MS = 200;
const REDUCED_MOTION_QUERY = "(prefers-reduced-motion: reduce)";
// A scroll position further than this from the one last written was moved by someone else.
const SCROLL_TOLERANCE_PX = 1;

let targetHeight = 0; // where the spacer is heading
let shownHeight = 0; // what the padding is right now
let frameHandle = 0;
let startedAt = 0;
let startHeight = 0;
let textOffset = 0; // scrollTop less the padding: where the text sits, which the change preserves
let writtenScrollTop = -1; // the scroll position this module last set, -1 before a change starts

// Cubic ease-out, like the pill's entrance.
export function easeOut(progress) {
  const remaining = 1 - progress;
  return 1 - remaining * remaining * remaining;
}

// The spacer's height `elapsed` ms into a change from `from` to `to`.
export function spacerAt(from, to, elapsed) {
  const progress = Math.min(1, Math.max(0, elapsed / DURATION_MS));
  return from + (to - from) * easeOut(progress);
}

// The scroll position that keeps the text still when the padding goes from `fromHeight` to
// `toHeight`. The browser clamps it again at the bottom, which the grown minimum height rules out.
export function compensatedScrollTop(scrollTop, fromHeight, toHeight) {
  return Math.max(0, scrollTop + toHeight - fromHeight);
}

function prefersReducedMotion() {
  return typeof window.matchMedia === "function" && window.matchMedia(REDUCED_MOTION_QUERY).matches;
}

// Each frame places the scroller from where the text sat when the change began, rather than adding
// up per-frame deltas the browser rounds, so the text cannot drift. Anything that scrolls mid-change
// (the user, or a reveal of the first match) moves that reference instead of being fought. One that
// scrolls all the way up wants the top of the slate, so the text follows the spacer down from there.
function showHeight(view, height) {
  // Read before the padding changes: a shrinking page clamps scrollTop on the next layout.
  const scrollTop = view.scrollDOM.scrollTop;
  if (writtenScrollTop < 0) {
    textOffset = scrollTop - shownHeight;
  } else if (Math.abs(scrollTop - writtenScrollTop) > SCROLL_TOLERANCE_PX) {
    textOffset = scrollTop < SCROLL_TOLERANCE_PX ? -targetHeight : scrollTop - shownHeight;
  }
  document.documentElement.style.setProperty(SPACER_PROPERTY, height + PX);
  view.scrollDOM.scrollTop = Math.max(0, textOffset + height);
  writtenScrollTop = view.scrollDOM.scrollTop;
  shownHeight = height;
  view.requestMeasure();
}

function cancelFrame() {
  if (frameHandle !== 0) {
    cancelAnimationFrame(frameHandle);
    frameHandle = 0;
  }
}

function step(view, now) {
  frameHandle = 0;
  const elapsed = now - startedAt;
  showHeight(view, spacerAt(startHeight, targetHeight, elapsed));
  if (elapsed < DURATION_MS) {
    frameHandle = requestAnimationFrame((next) => step(view, next));
  }
}

// Eases the spacer to `height` CSS px; 0 removes it. `view` may be null before the editor exists.
export function setSearchSpacer(view, height) {
  const next = height > 0 ? height : 0;
  if (next === targetHeight) {
    return;
  }
  targetHeight = next;
  cancelFrame();
  writtenScrollTop = -1;
  if (view == null) {
    shownHeight = next;
    document.documentElement.style.setProperty(SPACER_PROPERTY, next + PX);
    return;
  }
  if (prefersReducedMotion()) {
    showHeight(view, next);
    return;
  }
  startHeight = shownHeight;
  startedAt = performance.now();
  frameHandle = requestAnimationFrame((now) => step(view, now));
}

// A slate's saved scroll position was taken with a spacer of `savedHeight`; once the view has
// restored it, shifts it by the difference, so a slate switched to while the pill is open also has
// the spacer out of sight above it, and one left inside the spacer lands at its top after a close.
// CM's own measure frame was queued by setState, ahead of this one.
export function realignAfterRestore(view, savedHeight) {
  requestAnimationFrame(() => {
    if (shownHeight !== savedHeight) {
      view.scrollDOM.scrollTop = compensatedScrollTop(view.scrollDOM.scrollTop, savedHeight, shownHeight);
    }
  });
}

export function getShownSpacer() {
  return shownHeight;
}
