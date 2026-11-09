// Search folding (Docs/Specs/24-SEARCH.md § Folding): the text search compares against. Case,
// accents, typographic punctuation and whitespace runs fold away, and so do the markdown markers
// and list prefixes the renderer hides, so a query matches what the user sees rather than what the
// buffer file holds.
//
// A folded line keeps a map back to its document offsets, so a match found in folded text can be
// highlighted over the original range — hidden markers and all. Folded lines are cached per slate
// and refolded only where a transaction touched the document; a keystroke in the query refolds
// nothing.

import { parseItem } from "./listItems.js";
import { parseBlock, BLOCK_KIND_RULE } from "./blockFormat.js";
import { scanInline } from "./inlineScan.js";

const SPACE = " ";
const SPACE_CODE = 32;
const TAB_CODE = 9;
const NBSP_CODE = 160;
const ASCII_LIMIT = 128;
const UPPER_A = 65;
const UPPER_Z = 90;
const CASE_OFFSET = 32;
const SURROGATE_HIGH_FROM = 0xd800;
const SURROGATE_HIGH_TO = 0xdbff;
// String.fromCharCode.apply takes its codes as call arguments, so a very long line is built in
// slices that stay well under the engine's argument limit.
const STRING_CHUNK = 8192;

// Everything the fast path has to hand to the slow one: anything outside printable ASCII, and a
// whitespace run, which collapses and so breaks the one-to-one offset map.
const NEEDS_FULL_FOLD = /[^\x20-\x7e]| {2}/;
// Only these characters can start an inline span, so a line without them skips the scanner.
const INLINE_MARKER_CHARS = /[*~`]/;
const HEADING_CHAR = "#";
const RULE_CHAR = "-";
const COMBINING_MARKS = /\p{M}/gu;
const WHITESPACE = /\s/;
// Past this many touched lines (a paste of a whole document, a host setText) a full refold on next
// use is cheaper than splicing, and a spread this long would overflow the call stack.
const MAX_INCREMENTAL_LINES = 4096;
// A warm-up slice reads the clock once per this many lines rather than once per line.
const DEADLINE_CHECK_LINES = 64;

const TYPOGRAPHIC_FOLDS = new Map([
  ["“", "\""],
  ["”", "\""],
  ["„", "\""],
  ["‟", "\""],
  ["‘", "'"],
  ["’", "'"],
  ["‚", "'"],
  ["‛", "'"],
  ["‐", "-"],
  ["‑", "-"],
  ["‒", "-"],
  ["–", "-"],
  ["—", "-"],
  ["―", "-"],
  ["−", "-"],
]);

// The ranges of a line that never reach the folded text: a list item's atomic prefix, a heading's
// marker, a whole rule line, and every inline span's delimiters. Offsets into the line, sorted.
export function hiddenRanges(text) {
  const ranges = [];
  let contentStart = 0;
  const item = parseItem(text);
  if (item != null) {
    contentStart = item.prefixLength;
  } else if (text[0] === HEADING_CHAR || text[0] === RULE_CHAR) {
    const block = parseBlock(text);
    if (block != null && block.kind === BLOCK_KIND_RULE) {
      return [{ from: 0, to: text.length }];
    }
    if (block != null) {
      contentStart = block.markerLength;
    }
  }
  if (contentStart > 0) {
    ranges.push({ from: 0, to: contentStart });
  }
  if (INLINE_MARKER_CHARS.test(text)) {
    for (const span of scanInline(text, contentStart)) {
      if (span.contentFrom > span.from) {
        ranges.push({ from: span.from, to: span.contentFrom });
      }
      if (span.to > span.contentTo) {
        ranges.push({ from: span.contentTo, to: span.to });
      }
    }
    ranges.sort((first, second) => first.from - second.from);
  }
  return ranges;
}

// The characters of line offsets [from, to) that survive folding's marker pass: the slice minus
// its hiddenRanges. A Ctrl+F seed is built from this, so a selection that picked up the markers
// revealed on the caret line still finds its own text as a phrase.
export function visibleSlice(text, from, to) {
  const parts = [];
  let pos = from;
  for (const range of hiddenRanges(text)) {
    if (range.from >= to) {
      break;
    }
    if (range.from > pos) {
      parts.push(text.slice(pos, range.from));
    }
    pos = Math.max(pos, range.to);
  }
  if (pos < to) {
    parts.push(text.slice(pos, to));
  }
  return parts.join("");
}

// `char` is only consulted outside ASCII, where the Unicode spaces live.
function isWhitespace(code, char) {
  if (code === SPACE_CODE || code === TAB_CODE || code === NBSP_CODE) {
    return true;
  }
  return code >= ASCII_LIMIT && WHITESPACE.test(char);
}

// The folded form of one non-ASCII character: lower case, decomposed, combining marks dropped.
// May be empty (a lone combining mark) or longer than one unit.
export function foldChar(char) {
  const typographic = TYPOGRAPHIC_FOLDS.get(char);
  if (typographic != null) {
    return typographic;
  }
  return char.toLowerCase().normalize("NFD").replace(COMBINING_MARKS, "");
}

// Folded characters and their source ranges, in typed arrays: a line is folded one character at a
// time, and plain JS arrays of boxed strings made that loop the most expensive part of a search.
// One builder is reused for every line (folding is synchronous), because allocating three small
// typed arrays per line cost more than the fold itself.
class FoldBuilder {
  constructor(capacity) {
    this.codes = new Uint16Array(capacity);
    this.starts = new Int32Array(capacity);
    this.ends = new Int32Array(capacity);
    this.length = 0;
  }

  reset() {
    this.length = 0;
    return this;
  }

  push(code, start, end) {
    if (this.length === this.codes.length) {
      this.grow();
    }
    this.codes[this.length] = code;
    this.starts[this.length] = start;
    this.ends[this.length] = end;
    this.length++;
  }

  grow() {
    const size = this.codes.length * 2 + 1;
    const codes = new Uint16Array(size);
    const starts = new Int32Array(size);
    const ends = new Int32Array(size);
    codes.set(this.codes);
    starts.set(this.starts);
    ends.set(this.ends);
    this.codes = codes;
    this.starts = starts;
    this.ends = ends;
  }

  lastCode() {
    return this.length > 0 ? this.codes[this.length - 1] : -1;
  }

  lastEnd() {
    return this.length > 0 ? this.ends[this.length - 1] : -1;
  }

  extendLast(end) {
    this.ends[this.length - 1] = end;
  }

  build() {
    const parts = [];
    for (let from = 0; from < this.length; from += STRING_CHUNK) {
      parts.push(String.fromCharCode.apply(null, this.codes.subarray(from, Math.min(this.length, from + STRING_CHUNK))));
    }
    // Starts then ends, in one array: one allocation per line rather than two.
    const map = new Int32Array(this.length * 2);
    map.set(this.starts.subarray(0, this.length));
    map.set(this.ends.subarray(0, this.length), this.length);
    return { text: parts.join(""), map };
  }
}

const INITIAL_BUILDER_CAPACITY = 256;
const builder = new FoldBuilder(INITIAL_BUILDER_CAPACITY);

// Folds one line. For folded character i, `map[i]` and `map[text.length + i]` bound the original
// characters it came from; `map` is null when the fold is one-to-one, which is every plain ASCII
// line without a whitespace run or a marker — the common case, and the one that stays cheap.
export function foldLine(text) {
  const hidden = hiddenRanges(text);
  if (hidden.length === 0 && !NEEDS_FULL_FOLD.test(text)) {
    return { text: text.toLowerCase(), map: null };
  }

  const out = builder.reset();
  let hiddenIndex = 0;
  let pos = 0;
  while (pos < text.length) {
    while (hiddenIndex < hidden.length && hidden[hiddenIndex].to <= pos) {
      hiddenIndex++;
    }
    const range = hidden[hiddenIndex];
    if (range != null && range.from <= pos) {
      pos = range.to;
      continue;
    }

    const code = text.charCodeAt(pos);
    if (code < ASCII_LIMIT && code !== SPACE_CODE && code !== TAB_CODE) {
      out.push(code >= UPPER_A && code <= UPPER_Z ? code + CASE_OFFSET : code, pos, pos + 1);
      pos++;
      continue;
    }

    const width = code >= SURROGATE_HIGH_FROM && code <= SURROGATE_HIGH_TO && pos + 1 < text.length ? 2 : 1;
    const char = code < ASCII_LIMIT ? null : text.slice(pos, pos + width);
    if (isWhitespace(code, char)) {
      if (out.lastCode() === SPACE_CODE && out.lastEnd() === pos) {
        out.extendLast(pos + width);
      } else {
        out.push(SPACE_CODE, pos, pos + width);
      }
    } else {
      const result = foldChar(char);
      if (result.length === 0 && out.lastEnd() === pos) {
        // A combining mark after its base letter: the letter's highlight grows to cover it.
        out.extendLast(pos + width);
      }
      for (let i = 0; i < result.length; i++) {
        out.push(result.charCodeAt(i), pos, pos + width);
      }
    }
    pos += width;
  }
  return out.build();
}

// The query folds like the text, minus the markdown: a query is typed, not rendered, so a "*" in
// it is a character to find. Leading and trailing space is dropped so a half-typed "oat " still
// finds "oat".
export function foldQuery(query) {
  const folded = [];
  let lastWasSpace = false;
  for (const char of query) {
    const code = char.charCodeAt(0);
    if (isWhitespace(code, char)) {
      if (!lastWasSpace) {
        folded.push(SPACE);
      }
      lastWasSpace = true;
      continue;
    }
    lastWasSpace = false;
    if (code < ASCII_LIMIT) {
      folded.push(String.fromCharCode(code >= UPPER_A && code <= UPPER_Z ? code + CASE_OFFSET : code));
    } else {
      folded.push(foldChar(char));
    }
  }
  return folded.join("").trim();
}

// Maps a folded range [from, to) back to line offsets.
export function toLineRange(line, from, to) {
  if (line.map == null) {
    return { from, to };
  }
  return { from: line.map[from], to: line.map[line.text.length + to - 1] };
}

function foldUntil(entry, deadline, prepareLine) {
  const total = entry.lines.length;
  if (entry.folded === total) {
    return true;
  }
  const iter = entry.doc.iterLines(entry.folded + 1);
  for (let next = iter.next(); !next.done; next = iter.next()) {
    const line = foldLine(next.value);
    prepareLine(line);
    entry.lines[entry.folded++] = line;
    if (entry.folded % DEADLINE_CHECK_LINES === 0 && performance.now() >= deadline) {
      return entry.folded === total;
    }
  }
  return true;
}

function noPreparation() {}

// Per-slate cache of folded lines, index-aligned with the document's line numbers (line n is entry
// n - 1). An entry is valid for exactly one document (CM6 documents are immutable, so identity is
// the version). `update` carries an entry across a transaction by refolding only the lines the
// changes touched; `warm` folds a new one a slice at a time, so it can be done ahead of any search.
// `prepareLine` runs on every freshly folded line, so work a search would otherwise do on first
// use (tokenising, say) happens while warming instead.
export class FoldCache {
  constructor(prepareLine = noPreparation) {
    this.prepareLine = prepareLine;
    this.entries = new Map(); // slate index -> { doc, lines, folded }
  }

  get(index, doc) {
    const entry = this.entryFor(index, doc);
    foldUntil(entry, Infinity, this.prepareLine);
    return entry.lines;
  }

  // Folds more of slate `index` until `deadline` (a performance.now() time). True once it is whole.
  warm(index, doc, deadline) {
    return foldUntil(this.entryFor(index, doc), deadline, this.prepareLine);
  }

  // Whether slate `index` is folded whole for `doc`, so a search over it would fold nothing.
  isWarm(index, doc) {
    const entry = this.entries.get(index);
    return entry != null && entry.doc === doc && entry.folded === entry.lines.length;
  }

  entryFor(index, doc) {
    let entry = this.entries.get(index);
    if (entry == null || entry.doc !== doc) {
      entry = { doc, lines: new Array(doc.lines), folded: 0 };
      this.entries.set(index, entry);
    }
    return entry;
  }

  // Carries slate `index`'s entry from `startDoc` to `doc` across `changes`. An entry for any other
  // document is dropped: it refolds the next time it is asked for.
  update(index, startDoc, doc, changes) {
    const entry = this.entries.get(index);
    if (entry == null) {
      return;
    }
    if (entry.doc !== startDoc) {
      this.entries.delete(index);
      return;
    }
    let fromA = Infinity;
    let toA = -1;
    let fromB = Infinity;
    let toB = -1;
    changes.iterChangedRanges((changeFromA, changeToA, changeFromB, changeToB) => {
      fromA = Math.min(fromA, changeFromA);
      toA = Math.max(toA, changeToA);
      fromB = Math.min(fromB, changeFromB);
      toB = Math.max(toB, changeToB);
    });
    if (toA < 0) {
      entry.doc = doc;
      return;
    }

    const firstOld = startDoc.lineAt(fromA).number;
    const lastOld = startDoc.lineAt(toA).number;
    const firstNew = doc.lineAt(fromB).number;
    const lastNew = doc.lineAt(toB).number;
    if (entry.folded !== entry.lines.length) {
      // Partly warmed: the lines above the edit are still right, so the warm-up keeps them and
      // resumes from the edit rather than starting the slate over.
      this.truncate(entry, doc, firstNew - 1);
      return;
    }
    if (lastNew - firstNew > MAX_INCREMENTAL_LINES) {
      this.truncate(entry, doc, firstNew - 1);
      return;
    }
    const refolded = [];
    for (let number = firstNew; number <= lastNew; number++) {
      const line = foldLine(doc.line(number).text);
      this.prepareLine(line);
      refolded.push(line);
    }
    entry.lines.splice(firstOld - 1, lastOld - firstOld + 1, ...refolded);
    entry.doc = doc;
    entry.folded = entry.lines.length;
    if (entry.lines.length !== doc.lines) {
      this.entries.delete(index);
    }
  }

  // Keeps the first `keep` folded lines of `entry` (they are unchanged in `doc`) and leaves the rest
  // to be folded again.
  truncate(entry, doc, keep) {
    entry.folded = Math.min(entry.folded, keep);
    entry.lines.length = entry.folded;
    entry.lines.length = doc.lines;
    entry.doc = doc;
  }

  clear() {
    this.entries.clear();
  }
}
