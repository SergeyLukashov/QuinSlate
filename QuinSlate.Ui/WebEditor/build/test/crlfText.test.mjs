// crlfText.js: the CRLF length maths the character cap counts with, and the normalisation every
// incoming host string passes through. The document uses "\n"; the buffer file on disk uses CRLF,
// so a line break costs 2 against the cap.

import { describe, it } from "node:test";
import assert from "node:assert/strict";
import { Text } from "@codemirror/state";
import {
  crlfLengthOfDoc,
  crlfLengthOfString,
  truncateToCrlfBudget,
  normaliseIncoming,
} from "../src/crlfText.js";

describe("crlfLengthOfDoc", () => {
  it("counts a single line as its character count", () => {
    assert.equal(crlfLengthOfDoc(Text.of(["hello"])), 5);
  });

  it("counts each line break as two", () => {
    // "a\nb" reaches disk as "a\r\nb": 3 characters in the document, 4 against the cap.
    assert.equal(crlfLengthOfDoc(Text.of(["a", "b"])), 4);
    assert.equal(crlfLengthOfDoc(Text.of(["a", "b", "c"])), 7);
  });

  it("counts an empty document as zero", () => {
    assert.equal(crlfLengthOfDoc(Text.of([""])), 0);
  });
});

describe("crlfLengthOfString", () => {
  it("counts a break-free string as its length", () => {
    assert.equal(crlfLengthOfString("hello"), 5);
  });

  it("counts each newline as two", () => {
    assert.equal(crlfLengthOfString("a\nb"), 4);
  });

  it("counts a trailing newline", () => {
    assert.equal(crlfLengthOfString("a\n"), 3);
  });

  it("counts an empty string as zero", () => {
    assert.equal(crlfLengthOfString(""), 0);
  });
});

describe("truncateToCrlfBudget", () => {
  it("returns the whole string when it fits", () => {
    assert.equal(truncateToCrlfBudget("hello", 5), "hello");
    assert.equal(truncateToCrlfBudget("hello", 99), "hello");
  });

  it("truncates to the budget", () => {
    assert.equal(truncateToCrlfBudget("hello", 3), "hel");
  });

  it("returns empty for a non-positive budget", () => {
    assert.equal(truncateToCrlfBudget("hello", 0), "");
    assert.equal(truncateToCrlfBudget("hello", -1), "");
  });

  it("never splits a break's two-character cost", () => {
    // "ab\ncd": the break costs 2, so a budget of 3 stops before it rather than half-spending it.
    assert.equal(truncateToCrlfBudget("ab\ncd", 3), "ab");
    assert.equal(truncateToCrlfBudget("ab\ncd", 4), "ab\n");
  });
});

describe("normaliseIncoming", () => {
  it("converts CRLF to the document's newline", () => {
    assert.equal(normaliseIncoming("a\r\nb"), "a\nb");
  });

  it("converts a lone CR", () => {
    assert.equal(normaliseIncoming("a\rb"), "a\nb");
  });

  it("leaves a bare newline alone", () => {
    assert.equal(normaliseIncoming("a\nb"), "a\nb");
  });

  it("maps null to an empty string, so a missing buffer is not a throw", () => {
    assert.equal(normaliseIncoming(null), "");
    assert.equal(normaliseIncoming(undefined), "");
  });
});
