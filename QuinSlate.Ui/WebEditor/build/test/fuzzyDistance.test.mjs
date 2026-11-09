// fuzzyDistance.js: the bounded Damerau–Levenshtein check behind search's "close" tier.

import { describe, it } from "node:test";
import assert from "node:assert/strict";
import { maxEditsFor, prefixDistance } from "../src/fuzzyDistance.js";

describe("maxEditsFor", () => {
  it("never fuzzes under four letters, allows one from four and two from eight", () => {
    assert.equal(maxEditsFor(3), 0);
    assert.equal(maxEditsFor(4), 1);
    assert.equal(maxEditsFor(7), 1);
    assert.equal(maxEditsFor(8), 2);
  });
});

describe("prefixDistance", () => {
  it("counts a swap of neighbours as one edit", () => {
    assert.equal(prefixDistance("mlik", "milk", 1), 1);
  });

  it("counts an insertion, a deletion and a substitution as one edit each", () => {
    assert.equal(prefixDistance("mlk", "milk", 1), 1);
    assert.equal(prefixDistance("miilk", "milk", 1), 1);
    assert.equal(prefixDistance("molk", "milk", 1), 1);
  });

  it("compares against the start of the word", () => {
    assert.equal(prefixDistance("recieve", "received", 1), 1);
    assert.equal(prefixDistance("milk", "milkshake", 1), 0);
  });

  it("allows two edits where asked", () => {
    assert.equal(prefixDistance("definately", "definitely", 2), 1);
    assert.equal(prefixDistance("dafinataly", "definitely", 2), 3);
  });

  it("reports over budget as maxEdits + 1", () => {
    assert.equal(prefixDistance("milk", "scarf", 1), 2);
    assert.equal(prefixDistance("milk", "mi", 1), 2);
  });

  it("reads a word in place inside a longer text", () => {
    const text = "the received parcel";
    assert.equal(prefixDistance("recieve", text, 1, 4, 12), 1);
    assert.equal(prefixDistance("recieve", text, 1, 13, 19), 2);
  });
});
