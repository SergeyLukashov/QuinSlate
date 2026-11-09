// The tiered matcher (Docs/Specs/24-SEARCH.md § What matches). Pure: folded lines and a folded
// query in, matches out, in folded offsets. Which tier is shown is decided across all five slates
// by search.js; this module only answers "what does this tier find in these lines".
//
// Every tier is line-local. A phrase never matches across a line break, and the words of a tier-2
// or tier-3 query must share a line, so a match is always something visible in one place.

import { maxEditsFor, prefixDistance } from "./fuzzyDistance.js";

export const TIER_PHRASE = 1;
export const TIER_ALL_WORDS = 2;
export const TIER_CLOSE = 3;
export const NO_TIER = 0;

// Counting stops here per slate: the pill says "9,999+" rather than hang on "e" in a full slate.
export const MATCH_CAP = 9999;

const SPACE = " ";
const WORD_PATTERN = /[\p{L}\p{N}]+/gu;

function unique(values) {
  return Array.from(new Set(values));
}

// Everything the tiers need from a folded query, computed once per query rather than per line.
// Tier 2 compares whitespace-separated words as substrings; tier 3 compares letter-and-digit words,
// because a typo tolerance has nothing to say about a trailing comma.
export function prepareQuery(foldedQuery) {
  if (foldedQuery.length === 0) {
    return null;
  }
  const words = unique(foldedQuery.split(SPACE).filter((word) => word.length > 0));
  const closeWords = unique(foldedQuery.match(WORD_PATTERN) || []).map((text) => ({
    text,
    maxEdits: maxEditsFor(text.length),
  }));
  return { phrase: foldedQuery, words, closeWords };
}

// The tiers worth trying for a query, in order. Tier 2 needs two words to differ from tier 1, and
// tier 3 needs at least one word long enough to fuzz to differ from tier 2.
export function tiersFor(query) {
  const tiers = [TIER_PHRASE];
  if (query.words.length >= 2) {
    tiers.push(TIER_ALL_WORDS);
  }
  if (query.closeWords.some((word) => word.maxEdits > 0)) {
    tiers.push(TIER_CLOSE);
  }
  return tiers;
}

function occurrences(text, needle, into) {
  let at = text.indexOf(needle);
  while (at >= 0) {
    into.push({ from: at, to: at + needle.length });
    at = text.indexOf(needle, at + needle.length);
  }
}

// Sorted and merged, so overlapping hits of two words draw as one highlight.
function mergeRanges(ranges) {
  ranges.sort((first, second) => first.from - second.from);
  const merged = [];
  for (const range of ranges) {
    const last = merged[merged.length - 1];
    if (last != null && range.from <= last.to) {
      last.to = Math.max(last.to, range.to);
    } else {
      merged.push({ from: range.from, to: range.to });
    }
  }
  return merged;
}

// Word boundaries of a folded line, as [from, to, from, to, ...]. Kept on the cached line record so
// a second tier-3 query over the same text does not tokenise it again; the fold cache calls this as
// it folds, so warming ahead of a search tokenises too.
export function indexWords(line) {
  if (line.words == null) {
    const bounds = [];
    WORD_PATTERN.lastIndex = 0;
    for (let match = WORD_PATTERN.exec(line.text); match != null; match = WORD_PATTERN.exec(line.text)) {
      bounds.push(match.index, match.index + match[0].length);
    }
    line.words = Int32Array.from(bounds);
  }
  return line.words;
}

function allWordsHits(text, words) {
  const hits = [];
  for (const word of words) {
    const before = hits.length;
    occurrences(text, word, hits);
    if (hits.length === before) {
      return null;
    }
  }
  return hits;
}

function closeHits(line, closeWords) {
  const text = line.text;
  const hits = [];
  for (const word of closeWords) {
    const before = hits.length;
    occurrences(text, word.text, hits);
    if (word.maxEdits > 0) {
      const bounds = indexWords(line);
      for (let i = 0; i < bounds.length; i += 2) {
        if (prefixDistance(word.text, text, word.maxEdits, bounds[i], bounds[i + 1]) <= word.maxEdits) {
          hits.push({ from: bounds[i], to: bounds[i + 1] });
        }
      }
    }
    if (hits.length === before) {
      return null;
    }
  }
  return hits;
}

// The matches `tier` finds in one folded line. A phrase match is one occurrence; a tier-2 or
// tier-3 match is the whole line, carrying every query word's hit as its ranges.
export function matchLine(line, query, tier) {
  if (tier === TIER_PHRASE) {
    const hits = [];
    occurrences(line.text, query.phrase, hits);
    return hits.map((hit) => [hit]);
  }
  const hits = tier === TIER_ALL_WORDS ? allWordsHits(line.text, query.words) : closeHits(line, query.closeWords);
  return hits == null ? [] : [mergeRanges(hits)];
}

// Every match `tier` finds across a slate's folded lines, in document order, capped at MATCH_CAP.
// Each match is `{ line, ranges }`: a 0-based line index and folded ranges within it.
export function matchLines(lines, query, tier) {
  const matches = [];
  for (let index = 0; index < lines.length; index++) {
    for (const ranges of matchLine(lines[index], query, tier)) {
      if (matches.length === MATCH_CAP) {
        return { matches, capped: true };
      }
      matches.push({ line: index, ranges });
    }
  }
  return { matches, capped: false };
}

// Runs the tiers in order over every slate and keeps the first that finds anything anywhere: the
// tier is chosen globally, so a fuzzy hit never sits beside an exact one and every badge and
// highlight on screen comes from the same tier. `matchSlate(index, tier)` returns a slate's
// `{ matches, capped }`; the caller decides how those are cached.
export function searchSlates(query, indices, matchSlate) {
  if (query != null) {
    for (const tier of tiersFor(query)) {
      const results = new Map();
      const capped = new Map();
      let total = 0;
      for (const index of indices) {
        const slate = matchSlate(index, tier);
        results.set(index, slate.matches);
        capped.set(index, slate.capped);
        total += slate.matches.length;
      }
      if (total > 0) {
        return { tier, results, capped };
      }
    }
  }
  return { tier: NO_TIER, results: new Map(), capped: new Map() };
}
