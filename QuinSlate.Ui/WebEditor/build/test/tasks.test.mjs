// tasks.js: the "[] " shorthand, Enter continuation, and the checkbox toggle. The marker text is
// the persisted format, so every assertion here is also an assertion about what lands in the .txt.

import { describe, it } from "node:test";
import assert from "node:assert/strict";
import { convertTaskShorthand, handleTaskEnter, toggleTaskAtCaret } from "../src/tasks.js";
import { makeView } from "./editorHarness.mjs";

describe("task shorthand", () => {
  it("converts '[]' on Space", () => {
    const v = makeView("[]|");
    assert.equal(convertTaskShorthand(v), true);
    assert.equal(v.text(), "- [ ] ");
    assert.equal(v.caret(), 6);
  });

  it("converts '[ ]' on Space", () => {
    const v = makeView("[ ]|");
    assert.equal(convertTaskShorthand(v), true);
    assert.equal(v.text(), "- [ ] ");
  });

  it("carries text after the caret into the task's content", () => {
    const v = makeView("[]|buy milk");
    assert.equal(convertTaskShorthand(v), true);
    assert.equal(v.text(), "- [ ] buy milk");
    assert.equal(v.caret(), 6);
  });

  it("converts at the depth it was typed at", () => {
    const v = makeView("  []|");
    assert.equal(convertTaskShorthand(v), true);
    assert.equal(v.text(), "  - [ ] ");
  });

  it("declines mid-line, so Space types a space", () => {
    const v = makeView("text []|");
    assert.equal(convertTaskShorthand(v), false);
    assert.equal(v.text(), "text []");
  });

  it("declines when the caret is not right after the shorthand", () => {
    const v = makeView("[]x|");
    assert.equal(convertTaskShorthand(v), false);
    assert.equal(v.text(), "[]x");
  });

  it("declines with a non-empty selection", () => {
    const v = makeView("[]");
    v.dispatch({ selection: { anchor: 0, head: 2 } });
    assert.equal(convertTaskShorthand(v), false);
  });
});

describe("task Enter", () => {
  it("continues the list with a fresh unchecked task", () => {
    const v = makeView("- [ ] buy milk|");
    assert.equal(handleTaskEnter(v), true);
    assert.equal(v.text(), "- [ ] buy milk\n- [ ] ");
  });

  it("continues a checked task as unchecked", () => {
    const v = makeView("- [x] done|");
    assert.equal(handleTaskEnter(v), true);
    assert.equal(v.text(), "- [x] done\n- [ ] ");
  });

  it("continues at the same depth", () => {
    const v = makeView("- [ ] p\n  - [ ] c|");
    assert.equal(handleTaskEnter(v), true);
    assert.equal(v.text(), "- [ ] p\n  - [ ] c\n  - [ ] ");
  });

  it("carries the remainder into the new task when split mid-content", () => {
    const v = makeView("- [ ] buy |milk");
    assert.equal(handleTaskEnter(v), true);
    assert.equal(v.text(), "- [ ] buy \n- [ ] milk");
  });

  it("exits the list on an empty task", () => {
    const v = makeView("- [ ] |");
    assert.equal(handleTaskEnter(v), true);
    assert.equal(v.text(), "");
  });

  it("exits straight to column 0 from an empty nested task, indent and all", () => {
    // Deliberately not Notion's outdent-a-level-per-Enter: Shift+Tab is the way to climb out,
    // matching the instant escape Backspace gives.
    const v = makeView("- [ ] p\n    - [ ] |");
    assert.equal(handleTaskEnter(v), true);
    assert.equal(v.text(), "- [ ] p\n");
  });

  it("declines at the very start of the line, so the task is pushed down", () => {
    const v = makeView("|- [ ] a");
    assert.equal(handleTaskEnter(v), false);
    assert.equal(v.text(), "- [ ] a");
  });

  it("declines on a plain line", () => {
    const v = makeView("plain|");
    assert.equal(handleTaskEnter(v), false);
  });

  it("declines on a bullet — that is lists.js's Enter", () => {
    const v = makeView("- bullet|");
    assert.equal(handleTaskEnter(v), false);
  });
});

describe("task toggle", () => {
  it("checks an unchecked task", () => {
    const v = makeView("- [ ] a|");
    assert.equal(toggleTaskAtCaret(v), true);
    assert.equal(v.text(), "- [x] a");
  });

  it("unchecks a checked task", () => {
    const v = makeView("- [x] a|");
    assert.equal(toggleTaskAtCaret(v), true);
    assert.equal(v.text(), "- [ ] a");
  });

  it("normalises an uppercase X to lowercase on the way back", () => {
    const v = makeView("- [X] a|");
    assert.equal(toggleTaskAtCaret(v), true);
    assert.equal(v.text(), "- [ ] a");
  });

  it("toggles a nested task, finding the state char past the indent", () => {
    const v = makeView("    - [ ] deep|");
    assert.equal(toggleTaskAtCaret(v), true);
    assert.equal(v.text(), "    - [x] deep");
  });

  it("leaves the rest of the line alone", () => {
    const v = makeView("- [ ] a [x] b|");
    toggleTaskAtCaret(v);
    assert.equal(v.text(), "- [x] a [x] b");
  });

  it("declines on a plain line", () => {
    const v = makeView("plain|");
    assert.equal(toggleTaskAtCaret(v), false);
  });

  it("declines on a bullet", () => {
    const v = makeView("- bullet|");
    assert.equal(toggleTaskAtCaret(v), false);
  });
});
