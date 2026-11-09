// searchFold.js: what the query is compared against, and the offset map back to the document.

import { describe, it } from "node:test";
import assert from "node:assert/strict";
import { Text, EditorState } from "@codemirror/state";
import { foldLine, foldQuery, toLineRange, visibleSlice, FoldCache } from "../src/searchFold.js";

const TICK = String.fromCharCode(96);

// The original text a folded range maps back to.
function original(text, needle) {
  const line = foldLine(text);
  const at = line.text.indexOf(needle);
  assert.ok(at >= 0, JSON.stringify(needle) + " not found in " + JSON.stringify(line.text));
  const range = toLineRange(line, at, at + needle.length);
  return text.slice(range.from, range.to);
}

describe("foldLine", () => {
  it("folds case", () => {
    assert.equal(foldLine("Oat MILK").text, "oat milk");
  });

  it("drops accents and diacritics", () => {
    assert.equal(foldLine("Café Müller mañana").text, "cafe muller manana");
  });

  it("folds curly quotes and dashes", () => {
    assert.equal(foldLine("“hi” ‘x’ a–b—c").text, "\"hi\" 'x' a-b-c");
  });

  it("collapses whitespace runs, tabs and NBSP", () => {
    assert.equal(foldLine("oat   milk\tand eggs").text, "oat milk and eggs");
  });

  it("leaves hidden emphasis and heading markers out", () => {
    assert.equal(foldLine("Ship **Friday**").text, "ship friday");
    assert.equal(foldLine("## Ship ~~it~~").text, "ship it");
    assert.equal(foldLine("run " + TICK + "npm test" + TICK).text, "run npm test");
  });

  it("leaves a list item's atomic prefix out", () => {
    assert.equal(foldLine("- [ ] buy milk").text, "buy milk");
    assert.equal(foldLine("  1. first").text, "first");
    assert.equal(foldLine("- bullet").text, "bullet");
  });

  it("leaves a whole rule line out", () => {
    assert.equal(foldLine("---").text, "");
  });

  it("keeps a URL as text", () => {
    assert.equal(foldLine("see https://Example.com/x").text, "see https://example.com/x");
  });

  it("maps a match back over the hidden markers inside it", () => {
    assert.equal(original("Ship **Friday** now", "ship friday"), "Ship **Friday");
  });

  it("maps back through accents, whitespace runs and quotes", () => {
    assert.equal(original("a Café  au lait", "cafe au"), "Café  au");
    assert.equal(original("say “hi”", "\"hi\""), "“hi”");
  });

  it("covers a decomposed accent's combining mark", () => {
    assert.equal(original("café noir", "cafe"), "café");
  });

  it("maps correctly past a surrogate-pair emoji", () => {
    assert.equal(original("😀 milk", "milk"), "milk");
    assert.equal(original("😀été", "ete"), "été");
  });

  it("keeps a plain ASCII line one-to-one", () => {
    const line = foldLine("Buy milk and eggs");
    assert.equal(line.map, null);
    assert.equal(original("Buy milk and eggs", "milk"), "milk");
  });
});

describe("foldQuery", () => {
  it("folds like the text but keeps markdown characters and trims", () => {
    assert.equal(foldQuery("  Oat   MILK "), "oat milk");
    assert.equal(foldQuery("**Café**"), "**cafe**");
  });
});

describe("visibleSlice", () => {
  // The slice of `text` from the first `start` to the end of the first `end` after it.
  function sliceOf(text, start, end) {
    const from = text.indexOf(start);
    const to = text.indexOf(end, from) + end.length;
    return visibleSlice(text, from, to);
  }

  it("drops the markers inside the selection", () => {
    const text = "Ship **Friday** now";
    assert.equal(visibleSlice(text, 0, text.length), "Ship Friday now");
    assert.equal(sliceOf("run " + TICK + "npm test" + TICK + " ~~now~~", "run", "~~now~~"), "run npm test now");
  });

  it("drops the rest of a marker the selection starts inside", () => {
    const text = "Ship **Friday**";
    assert.equal(visibleSlice(text, text.indexOf("**") + 1, text.length), "Friday");
  });

  it("gives an empty string for a selection of markers only", () => {
    const text = "Ship **Friday**";
    assert.equal(visibleSlice(text, text.length - 2, text.length), "");
    assert.equal(visibleSlice("---", 0, 3), "");
  });

  it("drops a list item's prefix and a heading marker", () => {
    assert.equal(visibleSlice("- [ ] buy milk", 0, 14), "buy milk");
    assert.equal(visibleSlice("## Ship it", 0, 10), "Ship it");
  });

  it("keeps a plain selection as it is", () => {
    assert.equal(visibleSlice("Buy milk and eggs", 4, 8), "milk");
  });

  it("matches its own line as a phrase once folded", () => {
    const text = "Ship **Friday**";
    assert.ok(foldLine(text).text.includes(foldQuery(visibleSlice(text, 0, text.length))));
  });
});

describe("FoldCache", () => {
  it("refolds only what an edit touched and stays aligned with the document", () => {
    const cache = new FoldCache();
    const start = EditorState.create({ doc: "one\nTwo\nthree" });
    const lines = cache.get(1, start.doc);
    const untouched = lines[2];
    const tr = start.update({ changes: { from: 4, to: 7, insert: "Deux\nZwei" } });
    cache.update(1, start.doc, tr.state.doc, tr.changes);
    const after = cache.get(1, tr.state.doc);
    assert.deepEqual(after.map((line) => line.text), ["one", "deux", "zwei", "three"]);
    assert.equal(after[3], untouched);
  });

  it("warms a slate a slice at a time and finishes it on demand", () => {
    const cache = new FoldCache();
    const doc = Text.of(new Array(500).fill("Oat MILK"));
    assert.equal(cache.warm(1, doc, 0), false);
    const lines = cache.get(1, doc);
    assert.equal(lines.length, 500);
    assert.ok(lines.every((line) => line.text === "oat milk"));
    assert.equal(cache.warm(1, doc, 0), true);
  });

  it("refolds a partly warmed entry from the edit on, keeping the lines above it", () => {
    const cache = new FoldCache();
    const start = EditorState.create({ doc: new Array(500).fill("a").join("\n") });
    cache.warm(1, start.doc, 0);
    const kept = cache.entries.get(1).lines[0];
    const tr = start.update({ changes: { from: start.doc.line(30).from, insert: "B" } });
    cache.update(1, start.doc, tr.state.doc, tr.changes);
    const lines = cache.get(1, tr.state.doc);
    assert.equal(lines.length, 500);
    assert.equal(lines[0], kept);
    assert.equal(lines[29].text, "ba");
    assert.ok(lines.every((line, index) => index === 29 || line.text === "a"));
  });

  it("keeps the lines above a paste too long to splice", () => {
    const cache = new FoldCache();
    const start = EditorState.create({ doc: "one\ntwo" });
    const kept = cache.get(1, start.doc)[0];
    const pasted = new Array(5000).fill("X").join("\n");
    const tr = start.update({ changes: { from: start.doc.line(2).from, insert: pasted } });
    cache.update(1, start.doc, tr.state.doc, tr.changes);
    assert.equal(cache.isWarm(1, tr.state.doc), false);
    const lines = cache.get(1, tr.state.doc);
    assert.equal(lines[0], kept);
    assert.equal(lines.length, tr.state.doc.lines);
    assert.equal(lines[lines.length - 1].text, "xtwo");
  });

  it("prepares every line it folds, whether warming, on demand or after an edit", () => {
    const prepared = [];
    const cache = new FoldCache((line) => prepared.push(line.text));
    const start = EditorState.create({ doc: "one\ntwo\nthree" });
    cache.warm(1, start.doc, Infinity);
    const tr = start.update({ changes: { from: 0, insert: "X" } });
    cache.update(1, start.doc, tr.state.doc, tr.changes);
    assert.deepEqual(prepared, ["one", "two", "three", "xone"]);
  });

  it("reports whether a slate is folded whole for a document", () => {
    const cache = new FoldCache();
    const doc = Text.of(new Array(500).fill("a"));
    assert.equal(cache.isWarm(1, doc), false);
    cache.warm(1, doc, 0);
    assert.equal(cache.isWarm(1, doc), false);
    cache.get(1, doc);
    assert.equal(cache.isWarm(1, doc), true);
    assert.equal(cache.isWarm(1, Text.of(["a"])), false);
  });

  it("drops an entry built for another document", () => {
    const cache = new FoldCache();
    const first = Text.of(["a"]);
    cache.get(1, first);
    const other = EditorState.create({ doc: "b" });
    const tr = other.update({ changes: { from: 0, insert: "x" } });
    cache.update(1, other.doc, tr.state.doc, tr.changes);
    assert.deepEqual(cache.get(1, tr.state.doc).map((line) => line.text), ["xb"]);
  });
});
