// listItems.js: the single place that answers "what item is this line, and how deep is it".
// tasks.js, lists.js and indent.js all read structure through this, so a mistake here is a mistake
// in every list feature at once.

import { describe, it } from "node:test";
import assert from "node:assert/strict";
import {
  parseItem,
  lineIndent,
  indentText,
  renderDepth,
  splitShorthand,
  MAX_DEPTH,
  ITEM_KIND_TASK,
  ITEM_KIND_BULLET,
  ITEM_KIND_NUMBER,
} from "../src/listItems.js";

describe("parseItem: kinds", () => {
  it("recognises a bullet", () => {
    assert.equal(parseItem("- a").kind, ITEM_KIND_BULLET);
  });

  it("recognises a numbered item", () => {
    assert.equal(parseItem("1. a").kind, ITEM_KIND_NUMBER);
  });

  it("recognises a multi-digit numbered item", () => {
    const item = parseItem("42. a");
    assert.equal(item.kind, ITEM_KIND_NUMBER);
    assert.equal(item.number, "42");
  });

  it("recognises a task, not a bullet — tasks own the '- ' prefix", () => {
    assert.equal(parseItem("- [ ] a").kind, ITEM_KIND_TASK);
    assert.equal(parseItem("- [x] a").kind, ITEM_KIND_TASK);
  });

  it("reads the checked state, accepting an uppercase X", () => {
    assert.equal(parseItem("- [ ] a").checked, false);
    assert.equal(parseItem("- [x] a").checked, true);
    assert.equal(parseItem("- [X] a").checked, true);
  });

  it("returns null for a plain line", () => {
    assert.equal(parseItem("hello"), null);
  });

  it("returns null for a blank line", () => {
    assert.equal(parseItem(""), null);
    assert.equal(parseItem("   "), null);
  });

  it("requires the marker's trailing space", () => {
    assert.equal(parseItem("-a"), null);
    assert.equal(parseItem("1.a"), null);
  });

  it("does not treat a mid-line marker as an item", () => {
    assert.equal(parseItem("text - a"), null);
    assert.equal(parseItem("see 1. below"), null);
  });

  it("treats a bare marker as an empty item", () => {
    assert.equal(parseItem("- ").kind, ITEM_KIND_BULLET);
    assert.equal(parseItem("- [ ] ").kind, ITEM_KIND_TASK);
  });

  it("does not mistake a malformed checkbox for a task", () => {
    assert.equal(parseItem("- [] a").kind, ITEM_KIND_BULLET);
    assert.equal(parseItem("- [y] a").kind, ITEM_KIND_BULLET);
  });
});

describe("parseItem: depth", () => {
  it("reads depth from the leading spaces, two per level", () => {
    assert.equal(parseItem("- a").depth, 0);
    assert.equal(parseItem("  - a").depth, 1);
    assert.equal(parseItem("    - a").depth, 2);
  });

  it("floors an odd indent rather than inventing a half-level", () => {
    assert.equal(parseItem("   - a").depth, 1);
    assert.equal(parseItem(" - a").depth, 0);
  });

  it("keeps the raw indent length, so the atomic range covers the real text", () => {
    assert.equal(parseItem("   - a").indentLength, 3);
    assert.equal(parseItem("   - a").prefixLength, 5);
  });

  it("covers indent and marker in prefixLength for every kind", () => {
    assert.equal(parseItem("  - a").prefixLength, 4);
    assert.equal(parseItem("  1. a").prefixLength, 5);
    assert.equal(parseItem("  - [ ] a").prefixLength, 8);
  });

  it("recognises an item at any depth", () => {
    assert.equal(parseItem("        - [x] deep").kind, ITEM_KIND_TASK);
    assert.equal(parseItem("        - [x] deep").depth, 4);
  });
});

describe("lineIndent", () => {
  it("reads depth off a plain line", () => {
    assert.deepEqual(lineIndent("hello"), { depth: 0, indentLength: 0 });
    assert.deepEqual(lineIndent("  hello"), { depth: 1, indentLength: 2 });
    assert.deepEqual(lineIndent("    hello"), { depth: 2, indentLength: 4 });
  });

  it("reads a whitespace-only line as all indent", () => {
    assert.deepEqual(lineIndent("    "), { depth: 2, indentLength: 4 });
  });

  it("does not count a tab as indent — the editor never writes one", () => {
    assert.equal(lineIndent("\thello").indentLength, 0);
  });
});

describe("indentText", () => {
  it("renders two spaces per level", () => {
    assert.equal(indentText(0), "");
    assert.equal(indentText(1), "  ");
    assert.equal(indentText(3), "      ");
  });

  it("round-trips with the depth parser", () => {
    for (let depth = 0; depth <= MAX_DEPTH; depth++) {
      assert.equal(lineIndent(indentText(depth) + "x").depth, depth);
    }
  });
});

describe("renderDepth", () => {
  it("passes depths through up to the cap", () => {
    assert.equal(renderDepth(0), 0);
    assert.equal(renderDepth(MAX_DEPTH), MAX_DEPTH);
  });

  it("clamps beyond the cap, so pasted text still renders", () => {
    assert.equal(renderDepth(MAX_DEPTH + 5), MAX_DEPTH);
  });
});

describe("splitShorthand", () => {
  it("splits a shorthand off an unindented caret prefix", () => {
    assert.deepEqual(splitShorthand("-"), { depth: 0, shorthand: "-" });
    assert.deepEqual(splitShorthand("[]"), { depth: 0, shorthand: "[]" });
  });

  it("reads the depth a shorthand was typed at", () => {
    assert.deepEqual(splitShorthand("  1."), { depth: 1, shorthand: "1." });
    assert.deepEqual(splitShorthand("    [ ]"), { depth: 2, shorthand: "[ ]" });
  });

  it("reports an indent-only prefix as no shorthand", () => {
    assert.deepEqual(splitShorthand("  "), { depth: 1, shorthand: "" });
  });
});
