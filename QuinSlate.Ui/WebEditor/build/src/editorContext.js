// Shared editor session state. The single EditorView is created once (in
// editorSetup) and every other module reads it through here, which keeps the
// module graph acyclic — extensions that need the view or the active buffer
// index never have to import the module that assembles them.

export const BUFFER_MIN = 1;
export const BUFFER_MAX = 5;

let view = null;
let activeIndex = BUFFER_MIN;
// The host overwrites this from SystemAccentColor on the `theme` message, which arrives immediately
// after `ready` and so before any calc can run. Until then fall back to the CSS system colour for the
// OS selection highlight rather than a hardcoded blue, which would misreport the user's accent.
let accent = "Highlight";

export function setView(editorView) {
  view = editorView;
}

export function getView() {
  return view;
}

export function getActiveIndex() {
  return activeIndex;
}

export function setActiveIndex(index) {
  activeIndex = index;
}

export function getAccent() {
  return accent;
}

export function setAccent(value) {
  accent = value;
}
