// Search highlights (Docs/Specs/24-SEARCH.md § Highlighting): mark decorations over the active
// slate's matches, drawn only across the visible ranges. Nothing is written to the document, so
// undo has nothing to unwind and the buffer .txt never hears about a search.
//
// search.js owns the matches and hands them over with setSearchHighlights, then dispatches
// refreshSearchHighlight so the plugin rebuilds. Matches are tied to the document they were found
// in: after an edit and before search.js has re-matched, the plugin maps its existing marks through
// the change instead of redrawing stale positions.

import { StateEffect } from "@codemirror/state";
import { Decoration, EditorView, ViewPlugin } from "@codemirror/view";

const MATCH_CLASS = "cm-search-match";
const CURRENT_CLASS = "cm-search-current";

export const refreshSearchHighlight = StateEffect.define();

const matchMark = Decoration.mark({ class: MATCH_CLASS });
const currentMark = Decoration.mark({ class: CURRENT_CLASS });

let source = { doc: null, matches: [], current: -1 };
let scrollMarginTop = 0;

// `matches` are `{ from, to, ranges }` in `doc`, sorted; `current` is an index into them or -1.
export function setSearchHighlights(doc, matches, current) {
  source = { doc, matches, current };
}

export function clearSearchHighlights() {
  source = { doc: null, matches: [], current: -1 };
}

// The pill floats over the top of the editor; every scroll-to-match lands below it.
export function setSearchScrollMargin(top) {
  scrollMarginTop = top > 0 ? top : 0;
}

export function getSearchScrollMargin() {
  return scrollMarginTop;
}

function firstEndingAfter(matches, pos) {
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
  return low;
}

function buildHighlights(view) {
  if (source.doc !== view.state.doc || source.matches.length === 0) {
    return Decoration.none;
  }
  const marks = [];
  for (const range of view.visibleRanges) {
    for (let i = firstEndingAfter(source.matches, range.from); i < source.matches.length; i++) {
      const match = source.matches[i];
      if (match.from > range.to) {
        break;
      }
      const mark = i === source.current ? currentMark : matchMark;
      for (const part of match.ranges) {
        marks.push(mark.range(part.from, part.to));
      }
    }
  }
  return Decoration.set(marks, true);
}

function isRefresh(update) {
  return update.transactions.some((tr) => tr.effects.some((effect) => effect.is(refreshSearchHighlight)));
}

export const searchHighlightPlugin = ViewPlugin.fromClass(
  class {
    constructor(view) {
      this.decorations = buildHighlights(view);
    }

    update(update) {
      if (source.doc === update.state.doc && (isRefresh(update) || update.viewportChanged || update.docChanged)) {
        this.decorations = buildHighlights(update.view);
      } else if (source.doc == null && isRefresh(update)) {
        this.decorations = Decoration.none;
      } else if (update.docChanged) {
        this.decorations = this.decorations.map(update.changes);
      }
    }
  },
  {
    decorations: (value) => value.decorations,
  }
);

export const searchScrollMargins = EditorView.scrollMargins.of(() =>
  scrollMarginTop > 0 ? { top: scrollMarginTop } : null
);
