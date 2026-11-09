// searchWalk.js: stepping through matches in tab-strip order, wrapping, and following edits.

import { describe, it } from "node:test";
import assert from "node:assert/strict";
import {
  normaliseTabOrder,
  currentForCaret,
  step,
  ordinalOf,
  relocate,
} from "../src/searchWalk.js";

const INDICES = [1, 2, 3, 4, 5];

function at(...starts) {
  return starts.map((from) => ({ from, to: from + 4 }));
}

// Slate 1 has two matches, slate 3 one, slate 5 two; 2 and 4 have none.
function results() {
  return new Map([
    [1, at(0, 10)],
    [2, []],
    [3, at(5)],
    [4, []],
    [5, at(1, 20)],
  ]);
}

function walk(order, from, direction, times) {
  const found = results();
  const visited = [];
  let current = from;
  for (let i = 0; i < times; i++) {
    current = step(found, order, current, direction, 1, 0);
    visited.push(current.index + ":" + current.match);
  }
  return visited;
}

describe("normaliseTabOrder", () => {
  it("keeps the strip order and appends any slate it left out", () => {
    assert.deepEqual(normaliseTabOrder([3, 1, 3, 9], INDICES), [3, 1, 2, 4, 5]);
  });
});

describe("step", () => {
  it("walks tab order and wraps 5 to 1", () => {
    assert.deepEqual(walk(INDICES, { index: 1, match: 0 }, 1, 5), ["1:1", "3:0", "5:0", "5:1", "1:0"]);
  });

  it("walks backwards and wraps 1 to 5", () => {
    assert.deepEqual(walk(INDICES, { index: 1, match: 0 }, -1, 3), ["5:1", "5:0", "3:0"]);
  });

  it("follows a reordered strip", () => {
    assert.deepEqual(walk([5, 3, 1, 2, 4], { index: 5, match: 1 }, 1, 2), ["3:0", "1:0"]);
  });

  it("starts from the caret when nothing is current", () => {
    const found = results();
    assert.deepEqual(step(found, INDICES, null, 1, 1, 5), { index: 1, match: 1 });
    assert.deepEqual(step(found, INDICES, null, -1, 1, 5), { index: 1, match: 0 });
    assert.deepEqual(step(found, INDICES, null, 1, 2, 0), { index: 3, match: 0 });
  });

  it("lands on a pending match before moving past it", () => {
    const found = results();
    const pending = { index: 1, match: 1, pending: true };
    assert.deepEqual(step(found, INDICES, pending, 1, 1, 0), { index: 1, match: 1 });
    assert.deepEqual(step(found, INDICES, pending, -1, 1, 0), { index: 1, match: 0 });
  });

  it("returns null when nothing matches anywhere", () => {
    assert.equal(step(new Map(), INDICES, null, 1, 1, 0), null);
  });
});

describe("currentForCaret", () => {
  it("is the first match at or after the caret, wrapping within the slate", () => {
    const found = results();
    assert.deepEqual(currentForCaret(found, 1, 2), { index: 1, match: 0 });
    assert.deepEqual(currentForCaret(found, 1, 5), { index: 1, match: 1 });
    assert.deepEqual(currentForCaret(found, 1, 99), { index: 1, match: 0 });
  });

  it("never leaves the active slate", () => {
    assert.equal(currentForCaret(results(), 2, 0), null);
  });
});

describe("ordinalOf", () => {
  it("counts in walk order across slates", () => {
    const found = results();
    assert.equal(ordinalOf(found, INDICES, { index: 3, match: 0 }), 3);
    assert.equal(ordinalOf(found, [5, 3, 1, 2, 4], { index: 1, match: 1 }), 5);
  });
});

describe("relocate", () => {
  it("keeps the current match when it survived the edit", () => {
    assert.equal(relocate(at(0, 10, 20), 10), 1);
  });

  it("moves to the next match when the edit deleted the current one", () => {
    assert.equal(relocate(at(0, 20), 10), 1);
  });

  it("wraps to the first when nothing follows", () => {
    assert.equal(relocate(at(0, 5), 30), 0);
    assert.equal(relocate([], 3), -1);
  });
});
