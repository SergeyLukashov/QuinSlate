// Search across all five slates (Docs/Specs/24-SEARCH.md). The host owns the pill, the tab badges
// and tab switching; this module owns the matching and the walk, because all five EditorStates
// already live here.
//
// The query is treated as buffer text: it crosses the bridge but is never logged, and neither is
// anything matched. Results go back to the host as counts, the tier and the current match's
// position — never text.

import { EditorView } from "@codemirror/view";
import { postToHost } from "./hostBridge.js";
import { logDebug } from "./pageLog.js";
import { BUFFER_MIN, BUFFER_MAX, getView, getActiveIndex, getBufferState } from "./editorContext.js";
import { FoldCache, foldQuery, toLineRange } from "./searchFold.js";
import { NO_TIER, prepareQuery, searchSlates, matchLines, indexWords } from "./searchMatch.js";
import { normaliseTabOrder, currentForCaret, step, ordinalOf, relocate } from "./searchWalk.js";
import {
  setSearchHighlights,
  clearSearchHighlights,
  setSearchScrollMargin,
  getSearchScrollMargin,
  refreshSearchHighlight,
} from "./searchHighlight.js";
import { setSearchSpacer } from "./searchSpacer.js";

// A search over budget is worth a line in the log; one within it is not (Docs/Specs/24-SEARCH.md
// § Performance).
const FRAME_BUDGET_MS = 16;
// Folding runs ahead of the first query in slices no longer than this: in idle time from the moment
// the buffers load, and back to back once the pill is open. Five full slates take a few hundred
// milliseconds to fold, which a first query must never pay in one frame.
const WARM_SLICE_MS = 8;

// Why a search is being re-run, which decides where the current match goes afterwards.
const RUN_QUERY = "query";
const RUN_EDIT = "edit";
const RUN_ORDER = "order";

const INDICES = [];
for (let index = BUFFER_MIN; index <= BUFFER_MAX; index++) {
  INDICES.push(index);
}

// Tokenised as it folds, so the first close-tier query finds every line's words already indexed.
const foldCache = new FoldCache(indexWords);

let open = false;
let foldedQuery = "";
let query = null;
let order = INDICES.slice();
let tier = NO_TIER;
let results = new Map(); // index -> [{ from, to, ranges }] in that slate's document
let capped = new Map(); // index -> bool
// Per slate and tier, the matches for one document, so an edit re-matches only its own slate.
let slateCache = new Map(); // "tier:index" -> { doc, matches, capped }
let current = null; // { index, match, pending? }
// The current match's start in the active document, carried through edits so an edit that removes
// the current match hands over to the next one after it.
let anchor = null;
let navigatingTo = null; // { index, match } while the host switches to the slate it is in
let scheduledRun = null; // the reason of the run waiting for the next frame
// The pill's margin grew after the current match was revealed (its first layout lands after the
// first query), so the next run reveals it again, clear of the pill.
let revealOnNextRun = false;
let frameHandle = 0;
let warmHandle = 0; // back-to-back warm-up while the pill is open
let idleWarmHandle = 0; // idle-time warm-up at any other time
let revealHandle = 0;

export function isSearchOpen() {
  return open;
}

function activeMatches() {
  return results.get(getActiveIndex()) || [];
}

function caretOf(view) {
  return view.state.selection.main.from;
}

function matchSlate(index, tierToRun) {
  const state = getBufferState(index);
  if (state == null) {
    return { matches: [], capped: false };
  }
  const doc = state.doc;
  const key = tierToRun + ":" + index;
  const cached = slateCache.get(key);
  if (cached != null && cached.doc === doc) {
    return cached;
  }
  const lines = foldCache.get(index, doc);
  const found = matchLines(lines, query, tierToRun);
  const matches = found.matches.map((match) => {
    const lineFrom = doc.line(match.line + 1).from;
    const ranges = match.ranges.map((range) => {
      const mapped = toLineRange(lines[match.line], range.from, range.to);
      return { from: lineFrom + mapped.from, to: lineFrom + mapped.to };
    });
    return { from: ranges[0].from, to: ranges[ranges.length - 1].to, ranges };
  });
  const entry = { doc, matches, capped: found.capped };
  slateCache.set(key, entry);
  return entry;
}

function matchAllSlates() {
  const found = searchSlates(query, INDICES, matchSlate);
  results = found.results;
  capped = found.capped;
  tier = found.tier;
}

function placeCurrent(reason, view) {
  const active = getActiveIndex();
  if (reason === RUN_QUERY) {
    current = currentForCaret(results, active, caretOf(view));
    return;
  }
  if (current == null) {
    return;
  }
  const matches = results.get(current.index) || [];
  if (current.index === active && reason === RUN_EDIT && anchor != null) {
    const at = relocate(matches, anchor);
    current = at < 0 ? null : { index: active, match: at, pending: current.pending };
    return;
  }
  if (current.match >= matches.length) {
    current = matches.length > 0 ? { index: current.index, match: matches.length - 1 } : null;
  }
}

function currentInActive() {
  return current != null && current.index === getActiveIndex() ? current : null;
}

function updateAnchor() {
  const inActive = currentInActive();
  const match = inActive == null ? null : activeMatches()[inActive.match];
  anchor = match == null ? null : match.from;
}

function postResults() {
  const counts = [];
  const cappedFlags = [];
  for (const index of INDICES) {
    const matches = results.get(index);
    counts.push(matches == null ? 0 : matches.length);
    cappedFlags.push(capped.get(index) === true);
  }
  postToHost("searchResults", {
    counts,
    capped: cappedFlags,
    tier,
    current: current == null ? null : { index: current.index, ordinal: ordinalOf(results, order, current) },
  });
}

// Pushes the active slate's matches to the highlight plugin, optionally scrolling the current match
// into view, in one transaction.
function showHighlights(view, reveal) {
  const inActive = currentInActive();
  const matches = activeMatches();
  setSearchHighlights(view.state.doc, matches, inActive == null ? -1 : inActive.match);
  const effects = [refreshSearchHighlight.of(null)];
  const match = inActive == null ? null : matches[inActive.match];
  if (reveal && match != null) {
    effects.push(revealEffect(match));
  }
  view.dispatch({ effects });
}

function revealEffect(match) {
  return EditorView.scrollIntoView(match.from, { y: "nearest" });
}

function cancelReveal() {
  if (revealHandle !== 0) {
    cancelAnimationFrame(revealHandle);
    revealHandle = 0;
  }
}

// Reveals the current match once the view has restored the slate's saved scroll position. A reveal
// dispatched in the same frame would replace the snapshot's scroll target, and "nearest" would then
// be measured from wherever the previous slate left the shared scroller. CM's own measure frame was
// queued by setState, ahead of this one.
function revealAfterRestore(index) {
  cancelReveal();
  const inActive = currentInActive();
  const match = inActive == null ? null : activeMatches()[inActive.match];
  if (match == null) {
    return;
  }
  revealHandle = requestAnimationFrame(() => {
    revealHandle = 0;
    const view = getView();
    const still = currentInActive();
    if (!open || view == null || getActiveIndex() !== index || still == null || activeMatches()[still.match] !== match) {
      return;
    }
    view.dispatch({ effects: revealEffect(match) });
  });
}

// `allowReveal` is false while a tab switch is restoring the new slate's scroll position.
function run(reason, allowReveal) {
  const view = getView();
  const revealWanted = reason === RUN_QUERY || revealOnNextRun;
  revealOnNextRun = false;
  if (!open || view == null) {
    return;
  }
  const started = performance.now();
  matchAllSlates();
  placeCurrent(reason, view);
  updateAnchor();
  showHighlights(view, allowReveal && revealWanted);
  postResults();
  const elapsed = performance.now() - started;
  if (elapsed > FRAME_BUDGET_MS) {
    logDebug("Search took " + Math.round(elapsed) + " ms (tier " + tier + ", query length " + foldedQuery.length + ").");
  }
}

function schedule(reason) {
  // A query change outranks an edit: the caret decides where the current match lands.
  if (scheduledRun !== RUN_QUERY) {
    scheduledRun = reason;
  }
  if (frameHandle !== 0) {
    return;
  }
  frameHandle = requestAnimationFrame(() => {
    frameHandle = 0;
    const pending = scheduledRun;
    scheduledRun = null;
    if (pending != null) {
      run(pending, true);
    }
  });
}

// Runs a search still waiting for its frame now, so a step never walks stale matches.
function flushScheduled(allowReveal) {
  if (scheduledRun == null) {
    return;
  }
  const pending = scheduledRun;
  scheduledRun = null;
  run(pending, allowReveal);
}

// Folds the slates ahead of need, the active one first, until `deadline`. True once all five are
// folded whole. A query that arrives first simply finishes the folding itself.
function warmUntil(deadline) {
  const active = getActiveIndex();
  const pending = [active].concat(INDICES.filter((index) => index !== active));
  for (const index of pending) {
    const state = getBufferState(index);
    if (state != null && !foldCache.warm(index, state.doc, deadline)) {
      return false;
    }
  }
  return true;
}

function warmNextSlice() {
  warmHandle = 0;
  if (open && !warmUntil(performance.now() + WARM_SLICE_MS)) {
    warmHandle = setTimeout(warmNextSlice, 0);
  }
}

// Idle-time warm-up: never takes a frame's time from anything else, and hands over to the
// back-to-back slices while the pill is open.
function scheduleIdleWarm() {
  if (idleWarmHandle === 0) {
    idleWarmHandle = requestIdleCallback(warmInIdle);
  }
}

function warmInIdle(idle) {
  idleWarmHandle = 0;
  if (open) {
    return;
  }
  const slice = Math.min(idle.timeRemaining(), WARM_SLICE_MS);
  if (!warmUntil(performance.now() + slice)) {
    scheduleIdleWarm();
  }
}

// The five slates are in place: fold them in idle time, so the pill rarely opens on cold text.
export function onBuffersLoaded() {
  scheduleIdleWarm();
}

// The host's query, the strip order and the pill's height. An empty query clears.
export function handleSearchQuery(message) {
  if (!open && warmHandle === 0) {
    warmHandle = setTimeout(warmNextSlice, 0);
  }
  const marginTop = Number(message.scrollMarginTop) || 0;
  if (open && marginTop > getSearchScrollMargin()) {
    revealOnNextRun = true;
  }
  open = true;
  order = normaliseTabOrder(message.tabOrder, INDICES);
  setSearchScrollMargin(marginTop);
  setSearchSpacer(getView(), marginTop);
  const nextFolded = foldQuery(typeof message.query === "string" ? message.query : "");
  if (nextFolded === foldedQuery && query != null) {
    schedule(RUN_ORDER);
    return;
  }
  foldedQuery = nextFolded;
  query = prepareQuery(nextFolded);
  slateCache = new Map();
  navigatingTo = null;
  schedule(RUN_QUERY);
}

export function handleSearchStep(message) {
  const view = getView();
  if (!open || view == null) {
    return;
  }
  flushScheduled(true);
  const direction = message.direction === "prev" ? -1 : 1;
  if (message.fromCaret === true) {
    // F3 reopening a closed search: walk on from the caret rather than from the match the query
    // placed. Forward steps start past the selection, so a match Esc left selected is not revisited.
    current = null;
  }
  const selection = view.state.selection.main;
  const caret = direction > 0 ? selection.to : selection.from;
  const next = step(results, order, current, direction, getActiveIndex(), caret);
  if (next == null) {
    postResults();
    return;
  }
  current = next;
  if (next.index !== getActiveIndex()) {
    // The host selects the tab through its normal path; activation lands the view on the match.
    navigatingTo = next;
    anchor = null;
    postResults();
    postToHost("searchNavigate", { index: next.index });
    return;
  }
  updateAnchor();
  showHighlights(view, true);
  postResults();
}

// Closes the search. Esc turns the current match into the selection, ready to be typed over,
// copied, or read in context.
export function handleSearchClose(message) {
  const view = getView();
  if (frameHandle !== 0) {
    cancelAnimationFrame(frameHandle);
    frameHandle = 0;
  }
  scheduledRun = null;
  revealOnNextRun = false;
  cancelReveal();
  if (warmHandle !== 0) {
    clearTimeout(warmHandle);
    warmHandle = 0;
  }
  scheduleIdleWarm();
  const inActive = currentInActive();
  const match = inActive == null ? null : activeMatches()[inActive.match];

  open = false;
  // Forgotten so a reopen on the same query runs as a fresh query and lands on a match again.
  foldedQuery = "";
  query = null;
  results = new Map();
  capped = new Map();
  slateCache = new Map();
  current = null;
  anchor = null;
  navigatingTo = null;
  tier = NO_TIER;
  clearSearchHighlights();
  setSearchScrollMargin(0);
  setSearchSpacer(view, 0);
  if (view == null) {
    return;
  }

  const spec = { effects: [refreshSearchHighlight.of(null)] };
  if (message.selectCurrent === true && match != null) {
    spec.selection = { anchor: match.from, head: match.to };
    spec.scrollIntoView = true;
    spec.userEvent = "select.search";
  }
  view.dispatch(spec);
}

// A tab switch while searching: the current match moves to the new slate — onto the match the walk
// was heading for, or else the first at or after that slate's own caret. Only an arrival the walk
// asked for scrolls, and only once the slate's saved position is back; a tab switched by hand keeps
// its own scroll.
export function onSlateActivated(index) {
  const view = getView();
  cancelReveal();
  if (!open || view == null) {
    return;
  }
  flushScheduled(false);
  const arriving = navigatingTo != null && navigatingTo.index === index;
  if (arriving) {
    current = navigatingTo;
  } else {
    current = currentForCaret(results, index, caretOf(view));
  }
  navigatingTo = null;
  updateAnchor();
  showHighlights(view, false);
  postResults();
  if (arriving) {
    revealAfterRestore(index);
  }
}

// A slate that is not on screen had its text replaced by the host.
export function onSlateReplaced() {
  if (open) {
    schedule(RUN_EDIT);
  } else {
    scheduleIdleWarm();
  }
}

function isUserSelection(update) {
  return update.transactions.some((tr) => tr.isUserEvent("select") && !tr.isUserEvent("select.search"));
}

// Every update of the active slate: keeps the fold cache in step with edits and, while searching,
// re-matches after an edit and follows a click.
export function onSearchUpdate(update) {
  if (update.docChanged) {
    foldCache.update(getActiveIndex(), update.startState.doc, update.state.doc, update.changes);
  }
  if (!open) {
    if (update.docChanged && !foldCache.isWarm(getActiveIndex(), update.state.doc)) {
      scheduleIdleWarm();
    }
    return;
  }
  if (update.docChanged) {
    if (anchor != null) {
      anchor = update.changes.mapPos(anchor, -1);
    }
    schedule(RUN_EDIT);
    return;
  }
  if (update.selectionSet && isUserSelection(update)) {
    // A click or a caret move while the pill is open: the next Enter continues from the caret, so
    // the match there becomes current without being landed on. Deferred to a microtask because a
    // view cannot be dispatched to from inside its own update.
    const caret = update.state.selection.main.head;
    queueMicrotask(() => followCaret(caret));
  }
}

function followCaret(caret) {
  const view = getView();
  if (!open || view == null) {
    return;
  }
  flushScheduled(true);
  const placed = currentForCaret(results, getActiveIndex(), caret);
  current = placed == null ? null : { index: placed.index, match: placed.match, pending: true };
  updateAnchor();
  showHighlights(view, false);
  postResults();
}
