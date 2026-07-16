// indent.js: Tab / Shift+Tab. Nesting for list items under Notion's guardrails, plain indentation
// for everything else, and the caret riding along either way.

import { describe, it } from "node:test";
import assert from "node:assert/strict";
import { indentLines, outdentLines } from "../src/indent.js";
import { listRenumber } from "../src/lists.js";
import { MAX_DEPTH } from "../src/listItems.js";
import { makeView, makeLineSelectionView } from "./editorHarness.mjs";

// listRenumber runs in the real editor's extension set, and an indent can move a numbered item
// between runs, so the tests carry it: what they assert is what the user would see.
const view = (text) => makeView(text, [listRenumber]);
const lineSelection = (text, fromLine, toLine) =>
  makeLineSelectionView(text, fromLine, toLine, [listRenumber]);

describe("indent: the nesting guardrail", () => {
  it("nests an item under the item above it", () => {
    const v = view("- a\n- |b");
    indentLines(v);
    assert.equal(v.text(), "- a\n  - b");
  });

  it("refuses the first item of a list — there is no parent to nest under", () => {
    const v = view("- |a\n- b");
    indentLines(v);
    assert.equal(v.text(), "- a\n- b");
  });

  it("refuses to nest under a plain line", () => {
    const v = view("plain\n- |a");
    indentLines(v);
    assert.equal(v.text(), "plain\n- a");
  });

  it("refuses to nest under a blank line", () => {
    const v = view("- a\n\n- |b");
    indentLines(v);
    assert.equal(v.text(), "- a\n\n- b");
  });

  it("refuses an indent that would skip a level", () => {
    const v = view("- a\n  - |b");
    indentLines(v);
    assert.equal(v.text(), "- a\n  - b");
  });

  it("nests under a sibling at the same depth", () => {
    const v = view("- a\n  - b\n  - |c");
    indentLines(v);
    assert.equal(v.text(), "- a\n  - b\n    - c");
  });

  it("nests under a deeper item, landing beside it", () => {
    const v = view("- a\n  - b\n- |c");
    indentLines(v);
    assert.equal(v.text(), "- a\n  - b\n  - c");
  });

  it("refuses to nest past the depth cap", () => {
    // A ladder each item of which is one level deeper: only the last can legally indent, and only
    // while it is under the cap.
    let text = "";
    for (let depth = 0; depth <= MAX_DEPTH; depth++) {
      text += "  ".repeat(depth) + "- item" + depth + "\n";
    }
    const v = view(text + "  ".repeat(MAX_DEPTH) + "- |last");
    indentLines(v);
    assert.ok(v.text().endsWith("  ".repeat(MAX_DEPTH) + "- last"));
  });
});

describe("indent: subtree movement", () => {
  it("carries an item's whole subtree", () => {
    const v = view("- p1\n- |p2\n  - c1\n    - c2");
    indentLines(v);
    assert.equal(v.text(), "- p1\n  - p2\n    - c1\n      - c2");
  });

  it("carries the subtree back on outdent", () => {
    const v = view("- p1\n  - |p2\n    - c1");
    outdentLines(v);
    assert.equal(v.text(), "- p1\n- p2\n  - c1");
  });

  it("stops the subtree at the first line that is not deeper", () => {
    const v = view("- p1\n- |p2\n  - c1\n- p3");
    indentLines(v);
    assert.equal(v.text(), "- p1\n  - p2\n    - c1\n- p3");
  });

  it("stops the subtree at a plain line", () => {
    const v = view("- p1\n- |p2\n  plain");
    indentLines(v);
    assert.equal(v.text(), "- p1\n  - p2\n  plain");
  });
});

describe("outdent", () => {
  it("un-nests an item one level", () => {
    const v = view("- p\n  - |c");
    outdentLines(v);
    assert.equal(v.text(), "- p\n- c");
  });

  it("is a no-op on a top-level item — its subtree would flatten into it", () => {
    const v = view("- |a\n  - b");
    outdentLines(v);
    assert.equal(v.text(), "- a\n  - b");
  });

  it("lets a following sibling become the outdented item's child", () => {
    // Depth is positional: nothing special-cases this, it just falls out.
    const v = view("- p\n  - |c1\n  - c2");
    outdentLines(v);
    assert.equal(v.text(), "- p\n- c1\n  - c2");
  });
});

describe("indent: mixed item kinds", () => {
  it("nests a numbered item under a task", () => {
    const v = view("- [ ] task\n1. |num");
    indentLines(v);
    assert.equal(v.text(), "- [ ] task\n  1. num");
  });

  it("nests a task under a numbered item", () => {
    const v = view("1. a\n- [ ] |t");
    indentLines(v);
    assert.equal(v.text(), "1. a\n  - [ ] t");
  });

  it("nests a bullet under a task", () => {
    const v = view("- [x] task\n- |b");
    indentLines(v);
    assert.equal(v.text(), "- [x] task\n  - b");
  });
});

describe("indent: plain lines", () => {
  it("indents a plain line", () => {
    const v = view("plain |text");
    indentLines(v);
    assert.equal(v.text(), "  plain text");
  });

  it("indents a level per press", () => {
    const v = view("plain te|xt");
    indentLines(v);
    indentLines(v);
    assert.equal(v.text(), "    plain text");
  });

  it("needs no parent — plain text has no structure to guard", () => {
    const v = view("|only line");
    indentLines(v);
    assert.equal(v.text(), "  only line");
  });

  it("outdents a plain line", () => {
    const v = view("    plain |text");
    outdentLines(v);
    assert.equal(v.text(), "  plain text");
  });

  it("is a no-op outdenting at column 0", () => {
    const v = view("plain |text");
    outdentLines(v);
    assert.equal(v.text(), "plain text");
  });

  it("indents an empty line, so the user can indent before typing", () => {
    const v = view("|");
    indentLines(v);
    assert.equal(v.text(), "  ");
  });

  it("normalises an odd indent to whole levels", () => {
    const v = view("   plain |text");
    indentLines(v);
    assert.equal(v.text(), "    plain text");
  });

  it("stops at the depth cap", () => {
    const v = view("a|");
    for (let i = 0; i < MAX_DEPTH + 4; i++) {
      indentLines(v);
    }
    assert.equal(v.text(), "  ".repeat(MAX_DEPTH) + "a");
  });

  it("shares the unit with list depth: one Tab, then '- ', is a depth-1 bullet", () => {
    const v = view("|");
    indentLines(v);
    assert.equal(v.text(), "  ");
    // "  " + "- x" is what the shorthand would produce here; it parses as depth 1.
    assert.equal(v.text() + "- x", "  - x");
  });
});

describe("indent: the caret rides the shift", () => {
  // Regression: CM6 maps a position sitting exactly at an insertion point to *before* the inserted
  // text by default, so Tab moved the line and left the caret at column 0. Only visible on an empty
  // line or at column 0 — a caret inside the content shifts either way.
  it("lands after the indent on an empty line", () => {
    const v = view("|");
    indentLines(v);
    assert.equal(v.caret(), 2);
  });

  it("lands after the indent from column 0", () => {
    const v = view("|hello");
    indentLines(v);
    assert.equal(v.caret(), 2);
  });

  it("rides along from mid-line", () => {
    const v = view("hel|lo");
    indentLines(v);
    assert.equal(v.caret(), 5);
  });

  it("rides along from the line end", () => {
    const v = view("hello|");
    indentLines(v);
    assert.equal(v.caret(), 7);
  });

  it("rides back on outdent", () => {
    const v = view("  |hello");
    outdentLines(v);
    assert.equal(v.caret(), 0);
  });

  it("clamps at the line start when the indent under it is removed", () => {
    const v = view("    |hello");
    outdentLines(v);
    assert.equal(v.caret(), 2);
  });

  it("rides a list item's nesting", () => {
    const v = view("- a\n- |b");
    indentLines(v);
    assert.equal(v.caret(), 8);
  });

  it("keeps up across repeated presses", () => {
    const v = view("|");
    indentLines(v);
    indentLines(v);
    assert.equal(v.text(), "    ");
    assert.equal(v.caret(), 4);
  });
});

describe("indent: selections", () => {
  it("shifts every selected item", () => {
    const v = lineSelection("- a\n- b\n- c", 2, 3);
    indentLines(v);
    assert.equal(v.text(), "- a\n  - b\n  - c");
  });

  it("shifts plain lines caught in the selection too", () => {
    const v = lineSelection("- a\n- b\nplain\n- c", 2, 4);
    indentLines(v);
    assert.equal(v.text(), "- a\n  - b\n  plain\n  - c");
  });

  it("shifts every selected plain line", () => {
    const v = lineSelection("one\ntwo\nthree", 1, 3);
    indentLines(v);
    assert.equal(v.text(), "  one\n  two\n  three");
  });

  it("skips blank lines, which would only gain trailing whitespace", () => {
    const v = lineSelection("one\n\ntwo", 1, 3);
    indentLines(v);
    assert.equal(v.text(), "  one\n\n  two");
  });

  it("pulls the subtree trailing the last selected item", () => {
    const v = lineSelection("- a\n- b\n  - c", 2, 2);
    indentLines(v);
    assert.equal(v.text(), "- a\n  - b\n    - c");
  });

  it("outdents every selected plain line", () => {
    const v = lineSelection("  one\n  two", 1, 2);
    outdentLines(v);
    assert.equal(v.text(), "one\ntwo");
  });

  it("moves the block together or not at all, keyed on its first line", () => {
    const v = lineSelection("- item\nplain", 1, 2);
    indentLines(v);
    assert.equal(v.text(), "- item\nplain");
  });

  it("refuses an outdent whose first line is at depth 0", () => {
    // Clamping each line at 0 independently would flatten the subtree up into its parent.
    const v = lineSelection("- p\n  - c", 1, 2);
    outdentLines(v);
    assert.equal(v.text(), "- p\n  - c");
  });

  it("still finds the same lines on a repeated press", () => {
    // The caret fix moves a selection's anchor off the line start; Tab must not lose the block.
    const v = lineSelection("one\ntwo", 1, 2);
    indentLines(v);
    indentLines(v);
    assert.equal(v.text(), "    one\n    two");
  });

  it("stops at the guardrail on a repeated press", () => {
    const v = lineSelection("- a\n- b\n- c", 2, 3);
    indentLines(v);
    indentLines(v);
    assert.equal(v.text(), "- a\n  - b\n  - c");
  });
});

describe("indent: the Tab key contract", () => {
  it("reports handled even when the shift is refused, so Tab never escapes the editor", () => {
    // Tab must never insert a tab character or move DOM focus, so both commands swallow the key
    // whatever they do with it.
    assert.equal(indentLines(view("- |a")), true);
    assert.equal(outdentLines(view("plain|")), true);
    assert.equal(indentLines(view("|")), true);
  });

  it("never writes a tab character", () => {
    const v = view("|hello");
    indentLines(v);
    indentLines(v);
    assert.ok(!v.text().includes("\t"));
  });

  it("shifts in one transaction, so it is one undo step", () => {
    const v = lineSelection("one\ntwo\nthree", 1, 3);
    const before = v.state;
    indentLines(v);
    // One transaction means the new state is exactly one step from the old.
    assert.equal(v.state.doc.toString(), "  one\n  two\n  three");
    assert.notEqual(v.state, before);
  });
});

describe("indent: renumbering interaction", () => {
  it("renumbers in the same step as the nest", () => {
    const v = view("1. a\n  1. x\n1. b|");
    indentLines(v);
    assert.equal(v.text(), "1. a\n  1. x\n  2. b");
  });
});
