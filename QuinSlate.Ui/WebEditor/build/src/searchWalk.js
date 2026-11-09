// Walk order for search (Docs/Specs/24-SEARCH.md § Stepping through matches). Pure: matches per
// slate and the tab strip's order in, positions out.
//
// Walk order is tab-strip order, then document order within a slate. A position is
// `{ index, match }`: a slate's buffer index and a 0-based match number within it. Every match is
// `{ from, to }` at least, sorted by `from`, as search.js keeps them.

// The strip order the host sent, with any slate it left out appended, so no slate's matches can
// fall out of the walk.
export function normaliseTabOrder(tabOrder, indices) {
  const order = [];
  for (const index of tabOrder || []) {
    if (indices.indexOf(index) >= 0 && order.indexOf(index) < 0) {
      order.push(index);
    }
  }
  for (const index of indices) {
    if (order.indexOf(index) < 0) {
      order.push(index);
    }
  }
  return order;
}

function countOf(results, index) {
  const matches = results.get(index);
  return matches == null ? 0 : matches.length;
}

// The first match that ends after `pos` — the one at the caret or the next after it — or -1.
export function firstAtOrAfter(matches, pos) {
  let low = 0;
  let high = matches.length;
  while (low < high) {
    const middle = (low + high) >> 1;
    if (matches[middle].to > pos) {
      high = middle;
    } else {
      low = middle + 1;
    }
  }
  return low < matches.length ? low : -1;
}

// The last match that starts before `pos`, or -1.
export function lastBefore(matches, pos) {
  let low = 0;
  let high = matches.length;
  while (low < high) {
    const middle = (low + high) >> 1;
    if (matches[middle].from < pos) {
      low = middle + 1;
    } else {
      high = middle;
    }
  }
  return low - 1;
}

// Where the current match lands while typing: the first at or after the caret in the active slate,
// wrapping to that slate's first. Typing never switches tabs, so this is null when the active slate
// has nothing.
export function currentForCaret(results, index, caret) {
  const matches = results.get(index);
  if (matches == null || matches.length === 0) {
    return null;
  }
  const at = firstAtOrAfter(matches, caret);
  return { index, match: at < 0 ? 0 : at };
}

// The first match in the slate after position `order[tabPos]` in `direction`, wrapping all the way
// round to the starting slate itself, or null when no slate has a match.
function crossTabs(order, results, tabPos, direction) {
  for (let step = 1; step <= order.length; step++) {
    const index = order[(((tabPos + direction * step) % order.length) + order.length) % order.length];
    const count = countOf(results, index);
    if (count > 0) {
      return { index, match: direction > 0 ? 0 : count - 1 };
    }
  }
  return null;
}

// The match one step from `current` in `direction` (+1 next, -1 previous), crossing into the next
// slate that has one and wrapping 5 → 1 and 1 → 5. With no current match the step starts from the
// caret in the active slate. A pending current — placed by a click, not yet landed on — is itself
// the next match, so the first Enter after a click does not skip it.
export function step(results, order, current, direction, activeIndex, caret) {
  if (current != null && current.pending && direction > 0) {
    return { index: current.index, match: current.match };
  }
  const index = current != null ? current.index : activeIndex;
  const tabPos = Math.max(0, order.indexOf(index));
  const matches = results.get(index) || [];

  let within;
  if (current != null) {
    within = current.match + direction;
  } else if (direction > 0) {
    within = firstAtOrAfter(matches, caret);
  } else {
    within = lastBefore(matches, caret);
  }
  if (within >= 0 && within < matches.length) {
    return { index, match: within };
  }
  return crossTabs(order, results, tabPos, direction);
}

// The 1-based position of `current` among every match, in walk order.
export function ordinalOf(results, order, current) {
  let before = 0;
  for (const index of order) {
    if (index === current.index) {
      return before + current.match + 1;
    }
    before += countOf(results, index);
  }
  return before + current.match + 1;
}

// After an edit re-matched the slate: the match at the current one's old start (mapped through the
// edit), or the next one after it when the edit took the current match away.
export function relocate(matches, anchor) {
  if (matches.length === 0) {
    return -1;
  }
  let low = 0;
  let high = matches.length;
  while (low < high) {
    const middle = (low + high) >> 1;
    if (matches[middle].from >= anchor) {
      high = middle;
    } else {
      low = middle + 1;
    }
  }
  return low < matches.length ? low : 0;
}
