// Bounded Damerau–Levenshtein distance for search's "close" tier (Docs/Specs/24-SEARCH.md § The
// three tiers). The optimal-string-alignment variant: insertion, deletion, substitution, and a swap
// of two neighbouring letters each cost one edit.
//
// The query word is compared against the START of a text word, so "recieve" finds "received": the
// distance is the best over every prefix of the text word. The search gives up as soon as no
// alignment can stay within the allowed edits, which keeps a comparison O(word length × k).

// Words under this many letters never fuzz: at that length one edit reaches half the dictionary.
export const MIN_FUZZY_LENGTH = 4;
// From this many letters up, two edits are allowed instead of one.
export const TWO_EDIT_LENGTH = 8;

export function maxEditsFor(length) {
  if (length >= TWO_EDIT_LENGTH) {
    return 2;
  }
  return length >= MIN_FUZZY_LENGTH ? 1 : 0;
}

// Three DP rows, reused across calls: a tier-3 pass compares every word of up to five full slates,
// and a fresh allocation per word is garbage the collector would then have to chase mid-keystroke.
let rowBuffers = [new Int32Array(0), new Int32Array(0), new Int32Array(0)];

function rowsOf(size) {
  if (rowBuffers[0].length < size) {
    rowBuffers = [new Int32Array(size), new Int32Array(size), new Int32Array(size)];
  }
  return rowBuffers;
}

// The smallest edit distance between `query` and any prefix of the word `text[from, to)`, or
// `maxEdits + 1` when every prefix is further than `maxEdits` away. The word is read in place so a
// line's words never have to be sliced out as strings.
export function prefixDistance(query, text, maxEdits, from = 0, to = text.length) {
  const queryLength = query.length;
  const wordLength = to - from;
  const over = maxEdits + 1;
  if (wordLength < queryLength - maxEdits) {
    return over;
  }
  // Prefixes longer than queryLength + maxEdits are at least maxEdits + 1 insertions away.
  const width = Math.min(wordLength, queryLength + maxEdits);

  let [previous, current, beforePrevious] = rowsOf(width + 1);
  for (let j = 0; j <= width; j++) {
    previous[j] = j;
  }

  let previousRowMin = 0;
  for (let i = 1; i <= queryLength; i++) {
    current[0] = i;
    let rowMin = i;
    const queryChar = query.charCodeAt(i - 1);
    for (let j = 1; j <= width; j++) {
      const wordChar = text.charCodeAt(from + j - 1);
      const cost = queryChar === wordChar ? 0 : 1;
      let value = Math.min(previous[j] + 1, current[j - 1] + 1, previous[j - 1] + cost);
      if (
        i > 1 &&
        j > 1 &&
        queryChar === text.charCodeAt(from + j - 2) &&
        query.charCodeAt(i - 2) === wordChar
      ) {
        value = Math.min(value, beforePrevious[j - 2] + 1);
      }
      current[j] = value;
      if (value < rowMin) {
        rowMin = value;
      }
    }
    // Every later row is at least this row's minimum, or one more than the row before it (a swap
    // reaches back two rows), so once both are out of budget nothing can come back into it.
    if (rowMin > maxEdits && previousRowMin >= maxEdits) {
      return over;
    }
    previousRowMin = rowMin;
    const recycled = beforePrevious;
    beforePrevious = previous;
    previous = current;
    current = recycled;
  }

  let best = over;
  for (let j = Math.max(0, queryLength - maxEdits); j <= width; j++) {
    if (previous[j] < best) {
      best = previous[j];
    }
  }
  return best;
}
