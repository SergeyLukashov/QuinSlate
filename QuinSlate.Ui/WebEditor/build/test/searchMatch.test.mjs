// searchMatch.js: the three tiers, line-locality, the per-slate cap, and the global tier choice.
// Driven through foldLine so every case reads as the text the user typed.

import { describe, it } from "node:test";
import assert from "node:assert/strict";
import { foldLine, foldQuery, toLineRange } from "../src/searchFold.js";
import {
  TIER_PHRASE,
  TIER_ALL_WORDS,
  TIER_CLOSE,
  NO_TIER,
  MATCH_CAP,
  prepareQuery,
  matchLines,
  searchSlates,
} from "../src/searchMatch.js";

const TICK = String.fromCharCode(96);

function queryOf(text) {
  return prepareQuery(foldQuery(text));
}

// Each slate is an array of lines; the result is the tier and, per slate, every match as the
// original text its highlighted ranges cover.
function search(slates, text) {
  const folded = slates.map((lines) => lines.map(foldLine));
  const indices = slates.map((_, i) => i + 1);
  const found = searchSlates(queryOf(text), indices, (index, tier) =>
    matchLines(folded[index - 1], queryOf(text), tier)
  );
  const hits = indices.map((index) =>
    (found.results.get(index) || []).map((match) =>
      match.ranges
        .map((range) => {
          const mapped = toLineRange(folded[index - 1][match.line], range.from, range.to);
          return slates[index - 1][match.line].slice(mapped.from, mapped.to);
        })
        .join("|")
    )
  );
  return { tier: found.tier, hits };
}

function inOne(lines, text) {
  const found = search([lines], text);
  return { tier: found.tier, hits: found.hits[0] };
}

describe("tier 1: phrase", () => {
  it("folds accents, case and whitespace", () => {
    assert.deepEqual(inOne(["Café au lait"], "cafe"), { tier: TIER_PHRASE, hits: ["Café"] });
    assert.deepEqual(inOne(["Oat  milk"], "oat milk"), { tier: TIER_PHRASE, hits: ["Oat  milk"] });
  });

  it("matches across hidden markers", () => {
    assert.deepEqual(inOne(["Ship **Friday**"], "ship friday"), {
      tier: TIER_PHRASE,
      hits: ["Ship **Friday"],
    });
  });

  it("finds every occurrence on a line", () => {
    assert.deepEqual(inOne(["milk, milk"], "milk").hits, ["milk", "milk"]);
  });

  it("never matches across a line break", () => {
    assert.equal(inOne(["oat", "milk"], "oat milk").tier, NO_TIER);
  });
});

describe("tier 2: all words", () => {
  it("matches words in any order on the same line", () => {
    assert.deepEqual(inOne(["oat milk, 2 l"], "milk oat"), { tier: TIER_ALL_WORDS, hits: ["oat|milk"] });
  });

  it("needs every word on one line", () => {
    assert.equal(inOne(["milk", "oat"], "milk oat").tier, NO_TIER);
  });
});

describe("tier 3: close", () => {
  it("tolerates a swap, an insertion, a deletion and a substitution", () => {
    assert.deepEqual(inOne(["milk"], "mlik"), { tier: TIER_CLOSE, hits: ["milk"] });
    assert.equal(inOne(["milk"], "miilk").tier, TIER_CLOSE);
    assert.equal(inOne(["receive"], "recive").tier, TIER_CLOSE);
    assert.equal(inOne(["milky"], "milx").tier, TIER_CLOSE);
  });

  it("matches against the start of a word", () => {
    assert.deepEqual(inOne(["received the parcel"], "recieve"), { tier: TIER_CLOSE, hits: ["received"] });
  });

  it("allows two edits only from eight letters", () => {
    assert.equal(inOne(["definitely"], "definately").tier, TIER_CLOSE);
    assert.equal(inOne(["definitely"], "dafinataly").tier, NO_TIER);
    assert.equal(inOne(["garden"], "gxrdxn").tier, NO_TIER);
    assert.equal(inOne(["gardening"], "gxrdxning").tier, TIER_CLOSE);
  });

  it("never fuzzes a word under four letters", () => {
    assert.equal(inOne(["cart"], "cat").tier, NO_TIER);
  });

  it("needs every query word on one line", () => {
    assert.deepEqual(inOne(["oat mlik"], "milk oat"), { tier: TIER_CLOSE, hits: ["oat|mlik"] });
  });
});

describe("the tier is chosen globally", () => {
  it("shows exact hits only when any slate has one", () => {
    const found = search([["silk scarf"], ["milk"]], "milk");
    assert.equal(found.tier, TIER_PHRASE);
    assert.deepEqual(found.hits, [[], ["milk"]]);
  });

  it("falls back to close matches everywhere when nothing is exact", () => {
    const found = search([["silk scarf"], ["mild day"]], "milk");
    assert.equal(found.tier, TIER_CLOSE);
    assert.deepEqual(found.hits, [["silk"], ["mild"]]);
  });
});

describe("scope", () => {
  it("never matches inside a task or list prefix", () => {
    assert.deepEqual(inOne(["- [ ] x"], "[ ]").hits, []);
    assert.deepEqual(inOne(["1. one"], "1.").hits, []);
  });

  it("matches inside a code span and a URL", () => {
    assert.deepEqual(inOne(["run " + TICK + "npm test" + TICK], "npm test").hits, ["npm test"]);
    assert.deepEqual(inOne(["see https://example.com/milk"], "example.com").hits, ["example.com"]);
  });

  it("stops counting at the cap", () => {
    const lines = new Array(MATCH_CAP + 5).fill("e").map(foldLine);
    const found = matchLines(lines, queryOf("e"), TIER_PHRASE);
    assert.equal(found.matches.length, MATCH_CAP);
    assert.equal(found.capped, true);
  });
});
