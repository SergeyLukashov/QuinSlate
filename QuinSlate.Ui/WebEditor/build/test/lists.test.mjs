// lists.js: the "- " / "1. " shorthands, Enter continuation, and the renumber transaction filter
// that keeps every contiguous numbered run sequential from 1, per depth.

import { describe, it } from "node:test";
import assert from "node:assert/strict";
import { convertListShorthand, handleListEnter, listRenumber } from "../src/lists.js";
import { HostOrigin } from "../src/hostBridge.js";
import { makeView, touchDoc } from "./editorHarness.mjs";

const view = (text) => makeView(text, [listRenumber]);

describe("bullet shorthand", () => {
  it("converts '-' on Space", () => {
    const v = view("-|");
    assert.equal(convertListShorthand(v), true);
    assert.equal(v.text(), "- ");
    assert.equal(v.caret(), 2);
  });

  it("carries text after the caret into the item's content", () => {
    const v = view("-|milk");
    assert.equal(convertListShorthand(v), true);
    assert.equal(v.text(), "- milk");
  });

  it("converts at the depth it was typed at", () => {
    const v = view("  -|");
    assert.equal(convertListShorthand(v), true);
    assert.equal(v.text(), "  - ");
  });

  it("declines mid-line", () => {
    const v = view("text -|");
    assert.equal(convertListShorthand(v), false);
    assert.equal(v.text(), "text -");
  });
});

describe("numbered shorthand", () => {
  it("converts '1.' on Space", () => {
    const v = view("1.|");
    assert.equal(convertListShorthand(v), true);
    assert.equal(v.text(), "1. ");
    assert.equal(v.caret(), 3);
  });

  it("only '1.' starts a run — a run always starts at 1", () => {
    const v = view("3.|");
    assert.equal(convertListShorthand(v), false);
    assert.equal(v.text(), "3.");
  });

  it("converts at the depth it was typed at", () => {
    const v = view("    1.|");
    assert.equal(convertListShorthand(v), true);
    assert.equal(v.text(), "    1. ");
  });
});

describe("list Enter", () => {
  it("continues a bullet list", () => {
    const v = view("- a|");
    assert.equal(handleListEnter(v), true);
    assert.equal(v.text(), "- a\n- ");
  });

  it("continues a numbered list with the next number", () => {
    const v = view("1. a|");
    assert.equal(handleListEnter(v), true);
    assert.equal(v.text(), "1. a\n2. ");
  });

  it("continues at the same depth", () => {
    const v = view("- p\n  - c|");
    assert.equal(handleListEnter(v), true);
    assert.equal(v.text(), "- p\n  - c\n  - ");
  });

  it("continues a nested numbered run at the same depth", () => {
    const v = view("1. p\n  1. c|");
    assert.equal(handleListEnter(v), true);
    assert.equal(v.text(), "1. p\n  1. c\n  2. ");
  });

  it("carries the remainder into the new item when split mid-content", () => {
    const v = view("- buy |milk");
    assert.equal(handleListEnter(v), true);
    assert.equal(v.text(), "- buy \n- milk");
  });

  it("exits the list on an empty item", () => {
    const v = view("- |");
    assert.equal(handleListEnter(v), true);
    assert.equal(v.text(), "");
  });

  it("exits straight to column 0 from an empty nested item, indent and all", () => {
    const v = view("- p\n    - |");
    assert.equal(handleListEnter(v), true);
    assert.equal(v.text(), "- p\n");
  });

  it("declines at the very start of the line, so the item is pushed down", () => {
    const v = view("|- a");
    assert.equal(handleListEnter(v), false);
    assert.equal(v.text(), "- a");
  });

  it("declines on a plain line", () => {
    const v = view("plain|");
    assert.equal(handleListEnter(v), false);
  });

  it("declines on a task — that is tasks.js's Enter", () => {
    const v = view("- [ ] t|");
    assert.equal(handleListEnter(v), false);
  });

  it("renumbers the rest of the run when inserting mid-list", () => {
    const v = view("1. a|\n2. b");
    handleListEnter(v);
    assert.equal(v.text(), "1. a\n2. \n3. b");
  });
});

describe("renumbering", () => {
  it("makes a flat run sequential from 1", () => {
    const v = view("1. a\n1. b\n1. c");
    touchDoc(v);
    assert.equal(v.text(), "1. a\n2. b\n3. c!");
  });

  it("counts each depth separately", () => {
    const v = view("1. a\n  1. x\n  1. y\n1. b");
    touchDoc(v);
    assert.equal(v.text(), "1. a\n  1. x\n  2. y\n2. b!");
  });

  it("restarts a re-entered nested run at 1", () => {
    const v = view("1. a\n  1. x\n1. b\n  1. y");
    touchDoc(v);
    assert.equal(v.text(), "1. a\n  1. x\n2. b\n  1. y!");
  });

  it("resumes the parent run after its children", () => {
    const v = view("1. a\n  1. x\n  2. y\n9. b");
    touchDoc(v);
    assert.equal(v.text(), "1. a\n  1. x\n  2. y\n2. b!");
  });

  it("does not let a nested bullet break the parent run", () => {
    const v = view("1. a\n  - bullet\n1. b");
    touchDoc(v);
    assert.equal(v.text(), "1. a\n  - bullet\n2. b!");
  });

  it("lets a same-depth bullet break the run", () => {
    const v = view("1. a\n- bullet\n5. b");
    touchDoc(v);
    assert.equal(v.text(), "1. a\n- bullet\n1. b!");
  });

  it("lets a same-depth task break the run", () => {
    const v = view("1. a\n- [ ] t\n5. b");
    touchDoc(v);
    assert.equal(v.text(), "1. a\n- [ ] t\n1. b!");
  });

  it("lets a blank line break every run", () => {
    const v = view("1. a\n\n5. b");
    touchDoc(v);
    assert.equal(v.text(), "1. a\n\n1. b!");
  });

  it("lets a plain line break every run", () => {
    const v = view("1. a\nplain\n5. b");
    touchDoc(v);
    assert.equal(v.text(), "1. a\nplain\n1. b!");
  });

  it("rewrites multi-digit numbers", () => {
    const v = view("1. a\n99. b");
    touchDoc(v);
    assert.equal(v.text(), "1. a\n2. b!");
  });

  it("leaves a correct run untouched", () => {
    const v = view("1. a\n2. b");
    touchDoc(v);
    assert.equal(v.text(), "1. a\n2. b!");
  });

  it("leaves host-origin transactions alone — loaded text keeps its stored digits", () => {
    // A load must render as it is on disk, and only renumber once the user edits it.
    const v = view("");
    v.dispatch({
      changes: { from: 0, insert: "5. a\n9. b" },
      annotations: HostOrigin.of("host"),
    });
    assert.equal(v.text(), "5. a\n9. b");
  });

  it("renumbers host-loaded text as soon as the user edits it", () => {
    const v = view("");
    v.dispatch({
      changes: { from: 0, insert: "5. a\n9. b" },
      annotations: HostOrigin.of("host"),
    });
    touchDoc(v);
    assert.equal(v.text(), "1. a\n2. b!");
  });
});
