// searchSpacer.js: the room made for the search pill, eased in and out, and the scroll correction
// that keeps it out of sight until the user scrolls up into it.

import { describe, it, beforeEach, afterEach } from "node:test";
import assert from "node:assert/strict";
import {
  easeOut,
  spacerAt,
  compensatedScrollTop,
  setSearchSpacer,
  realignAfterRestore,
  getShownSpacer,
} from "../src/searchSpacer.js";

const PILL = 52;
const DURATION = 200;
const FRAME = 16;

const properties = new Map();
document.documentElement.style.setProperty = (name, value) => properties.set(name, value);

let frames = new Map();
let nextHandle = 1;
let clock = 0;
globalThis.requestAnimationFrame = (callback) => {
  const handle = nextHandle++;
  frames.set(handle, callback);
  return handle;
};
globalThis.cancelAnimationFrame = (handle) => frames.delete(handle);
performance.now = () => clock;

function runFrame() {
  clock += FRAME;
  const pending = frames;
  frames = new Map();
  for (const callback of pending.values()) {
    callback(clock);
  }
}

// Runs frames until the animation settles; returns the padding shown on each frame.
function runAll() {
  const shown = [];
  while (frames.size > 0) {
    runFrame();
    shown.push(getShownSpacer());
  }
  return shown;
}

function fakeView(scrollTop) {
  return {
    scrollDOM: { scrollTop },
    requestMeasure() {},
  };
}

describe("spacerAt", () => {
  it("starts at the old height and ends at the new one", () => {
    assert.equal(spacerAt(0, PILL, 0), 0);
    assert.equal(spacerAt(0, PILL, DURATION), PILL);
    assert.equal(spacerAt(PILL, 0, DURATION * 2), 0);
  });

  it("eases out: past halfway at half the time", () => {
    assert.ok(spacerAt(0, PILL, DURATION / 2) > PILL / 2);
    assert.equal(easeOut(0.5), 0.875);
  });
});

describe("compensatedScrollTop", () => {
  it("puts the spacer above the viewport on open, even at the top", () => {
    assert.equal(compensatedScrollTop(0, 0, PILL), PILL);
    assert.equal(compensatedScrollTop(300, 0, PILL), 300 + PILL);
  });

  it("takes the spacer back out of the scroll position on close", () => {
    assert.equal(compensatedScrollTop(300 + PILL, PILL, 0), 300);
  });

  it("never scrolls above the top", () => {
    assert.equal(compensatedScrollTop(10, PILL, 0), 0);
  });
});

describe("setSearchSpacer", () => {
  beforeEach(() => {
    setSearchSpacer(null, 0);
    frames = new Map();
  });

  afterEach(() => {
    delete window.matchMedia;
  });

  it("opens without moving the text: the spacer ends up above the viewport", () => {
    const view = fakeView(0);
    setSearchSpacer(view, PILL);
    runAll();
    assert.equal(properties.get("--search-spacer"), PILL + "px");
    assert.equal(view.scrollDOM.scrollTop, PILL);
  });

  it("eases the padding over several frames", () => {
    const view = fakeView(0);
    setSearchSpacer(view, PILL);
    const shown = runAll();
    assert.ok(shown.length > 2);
    for (let i = 1; i < shown.length; i++) {
      assert.ok(shown[i] > shown[i - 1]);
    }
  });

  it("round-trips a scrolled view to where it was", () => {
    const view = fakeView(300);
    setSearchSpacer(view, PILL);
    runAll();
    setSearchSpacer(view, 0);
    runAll();
    assert.equal(properties.get("--search-spacer"), "0px");
    assert.equal(view.scrollDOM.scrollTop, 300);
  });

  it("glides revealed text back up on close", () => {
    const view = fakeView(0);
    setSearchSpacer(view, PILL);
    runAll();
    view.scrollDOM.scrollTop = 0; // the user scrolls up into the spacer
    setSearchSpacer(view, 0);
    const shown = runAll();
    assert.ok(shown.length > 2);
    assert.equal(shown[shown.length - 1], 0);
    assert.equal(view.scrollDOM.scrollTop, 0);
  });

  it("follows a scroll to the top mid-open, so a revealed first line ends clear of the pill", () => {
    const view = fakeView(0);
    setSearchSpacer(view, PILL);
    runFrame();
    view.scrollDOM.scrollTop = 0; // the first match on line 1 is revealed
    runAll();
    assert.equal(getShownSpacer(), PILL);
    assert.equal(view.scrollDOM.scrollTop, 0);
  });

  it("respects a scroll mid-change instead of fighting it", () => {
    const view = fakeView(300);
    setSearchSpacer(view, PILL);
    runFrame();
    view.scrollDOM.scrollTop = 500;
    const offset = 500 - getShownSpacer();
    runAll();
    assert.equal(view.scrollDOM.scrollTop, offset + PILL);
  });

  it("reopening mid-close carries on from the height on screen", () => {
    const view = fakeView(300);
    setSearchSpacer(view, PILL);
    runAll();
    setSearchSpacer(view, 0);
    runFrame();
    runFrame();
    const midway = getShownSpacer();
    assert.ok(midway > 0 && midway < PILL);
    setSearchSpacer(view, PILL);
    runAll();
    assert.equal(getShownSpacer(), PILL);
    assert.equal(view.scrollDOM.scrollTop, 300 + PILL);
  });

  it("does nothing when the height has not changed", () => {
    const view = fakeView(300);
    setSearchSpacer(view, PILL);
    runAll();
    setSearchSpacer(view, PILL);
    assert.equal(frames.size, 0);
  });

  it("jumps straight there when the user prefers reduced motion", () => {
    window.matchMedia = () => ({ matches: true });
    const view = fakeView(0);
    setSearchSpacer(view, PILL);
    assert.equal(frames.size, 0);
    assert.equal(getShownSpacer(), PILL);
    assert.equal(view.scrollDOM.scrollTop, PILL);
  });

  it("treats a negative height as none", () => {
    setSearchSpacer(fakeView(0), -5);
    assert.equal(frames.size, 0);
  });

  it("still clears the padding when there is no view", () => {
    setSearchSpacer(null, PILL);
    setSearchSpacer(null, 0);
    assert.equal(properties.get("--search-spacer"), "0px");
  });
});

describe("realignAfterRestore", () => {
  beforeEach(() => {
    setSearchSpacer(null, 0);
    frames = new Map();
  });

  it("hides the spacer above a slate saved before search opened", () => {
    setSearchSpacer(null, PILL);
    const view = fakeView(0);
    realignAfterRestore(view, 0);
    runFrame();
    assert.equal(view.scrollDOM.scrollTop, PILL);
  });

  it("leaves a slate saved with the same spacer alone", () => {
    setSearchSpacer(null, PILL);
    const view = fakeView(120);
    realignAfterRestore(view, PILL);
    runFrame();
    assert.equal(view.scrollDOM.scrollTop, 120);
  });

  it("takes the spacer back out of a slate saved while search was open", () => {
    const view = fakeView(300 + PILL);
    realignAfterRestore(view, PILL);
    runFrame();
    assert.equal(view.scrollDOM.scrollTop, 300);
  });
});
