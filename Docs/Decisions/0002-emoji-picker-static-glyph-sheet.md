# ADR 0002: Emoji picker as a static glyph sheet

## Status
Accepted — 2026-07-03

## Context
The emoji picker (a flyout over ~630 emoji in 7 fixed groups) was built on
grouped `GridView`s. Users reported lag and freezes on first open, while
typing in the search box, and on first scroll — even on powerful hardware.
The cause is structural: `GridView` pays UI-thread container realization
exactly when the user acts. Opening realizes ~50 `GridViewItem`s and their
templates, every search keystroke re-realizes the viewport, and scrolling
realizes containers on demand. Each item container is a full `Control` with
a template, visual states, selection infrastructure, and an automation peer.
For a small, fixed, immutable dataset, virtualization buys nothing and costs
everything. Incremental tuning (debounced search, `ItemsSource` swap
minimization, `ItemsWrapGrid` sizing, an `Opacity=0` hidden-host prewarm —
which the compositor culls, so it never rasterized glyphs) was tried and
produced no perceivable difference.

## Decision
Hand-roll the picker as a **static glyph sheet**: one `Canvas` inside a
`ScrollViewer`, with one plain `TextBlock` per emoji plus one per category
header, all created exactly once and kept alive for the app's lifetime.

- **Search repositions, never rebuilds.** Each keystroke computes matches
  (`EmojiSearch`) and moves the existing TextBlocks: matches into a compact
  grid, non-matches collapsed. Zero element creation, no debounce needed.
- **Scroll is pure composition.** Nothing virtualizes, so panning is
  render-thread work; the UI thread is idle during scroll.
- **Hover/press/click are grid math.** A shared highlight border pair moves
  under the pointer using reverse hit-testing in a pure calculator
  (`EmojiSheetLayoutCalculator`); `Tapped` on the canvas selects, so touch
  panning never mis-picks. No per-item event handlers exist.
- **Prewarm without hidden-host hacks.** `EmojiPicker.Prewarm()` builds the
  view ahead of time without inserting it into the visual tree, plus a
  disconnected measure/arrange pass (with graceful degradation to
  construct-only). `BufferPanel` schedules it at low dispatcher priority
  after load; `Open()` builds synchronously if it wins the race.
- **The glyph cache is warmed invisibly at startup.** Frame-level
  benchmarking (`QuinSlate.EmojiPickerBench`, fresh process per scenario) showed
  the dominant cost is first-time color-glyph rasterization: ~6 ms per glyph,
  ~3.4 s for the full set on fast hardware — render-thread work invisible to
  UI-thread stopwatches — while a second, freshly built sheet attaches in
  ~250 ms because the rasterization cache is process-wide. Measured culling
  behavior: `Opacity=0` subtrees are culled (never rasterize), but content
  clipped to 1×1, covered by opaque siblings, or positioned off-screen DOES
  rasterize. `EmojiGlyphCacheWarmer` therefore hosts throwaway glyph
  TextBlocks in a 1×1-clipped, non-interactive host inside the live window
  and reveals them 2 per frame (~10-15 ms of render work per frame, so a
  user already typing never feels it), starting 2 s after panel load and
  self-removing when done. A warmed first open settles in ~60 ms.
- **Every transition is a windowed paced placement, not one frame.** A
  non-virtualized ScrollViewer re-renders its full content extent, so making
  hundreds of glyphs appear or move in one frame stalls the render thread —
  cold (~6 ms/glyph) or even warm (~0.6 ms/glyph: a full search reposition
  cost 200-270 ms frames). Glyphs are built `Collapsed` (sizes captured at
  build time, since collapsed elements measure to zero) and every transition
  — initial reveal, each search keystroke, each return to browse — applies
  visibility in bounded slices: cells intersecting the current viewport
  window first (up to one slice of 28 synchronously, so the visible region
  never blanks), then one slice per `CompositionTarget.Rendering` tick
  (`Layout/EmojiSheetRevealPlanner`, pure and unit-tested; the window
  follows the restored scroll offset for browse). Reopening in
  fully-visible browse state skips re-pacing entirely. Pacing pauses while
  the flyout is closed and resumes where it left off.
- Layout math, search matching, and reveal planning live in pure, unit-tested
  classes with no UI types (`Layout/EmojiSheetLayoutCalculator`,
  `Layout/EmojiSheetRevealPlanner`, `Models/EmojiSearch`).

## Alternatives considered
- **Incremental GridView tuning** — attempted first (debounce, single
  ItemsSource swap, ItemsWrapGrid sizing, hidden-host prewarm); rejected by
  the user as imperceptible. The realization cost is architectural, not a
  tuning problem.
- **ItemsRepeater** — lighter containers, but still realizes elements on
  scroll and rebuilds on filter changes; same structural cost profile.
- **Third-party picker package** — the original spec suggestion; violates
  the no-third-party-packages baseline and still virtualizes internally.
- **Windows system picker (`Win + .`)** — inserts into the focused text
  control rather than the flyout's field and cannot be anchored/contained
  to the panel.

## Consequences
- Benchmarked on the dev machine (frame-tick deltas, fresh process per run):
  first open after the ~7 s invisible warm settles in **~60 ms with no frame
  over 33 ms** (baseline: ~3.4 s with a 3.1 s frame, or 19+ frames over
  100 ms when paced); steady-state search typing has p95 frame deltas under
  ~29 ms (baseline: repeated 200-270 ms frames); browse↔search mode
  transitions cost at most one ~60-90 ms frame (canvas resize + mass
  collapse); scrolling never exceeds ~20 ms. Opening within the first
  seconds after launch, before the warm completes, falls back to the paced
  reveal (~3.4 s to full sheet, fold first, interactive throughout).
- First open, search-as-you-type, and scroll never create, destroy, or
  rebind UI elements; the one-time UI-thread build cost is paid at idle via
  prewarm, and the one-time render-thread rasterization cost is paid by the
  invisible warmer.
- ~640 live `TextBlock`s (plus 7 headers) are held for the app's lifetime,
  and the warmer briefly holds a second throwaway set until it completes.
  Plain TextBlocks carry no template/VSM/peer overhead, so the resident cost
  is small and fixed.
- Per-emoji UIA automation peers and per-item keyboard navigation are
  intentionally dropped. The sheet exposes a single "Emoji grid" automation
  name; `Enter` in the search box picks the first visible match. Narrator
  users cannot enumerate individual emoji.
- Content-free Serilog timings (sheet build, glyph-cache warm with max frame
  delta, per-filter apply, placement pacing completion, and first open
  measured to the first presented frame AND to frame-delta settle —
  UI-thread-only stopwatches provably miss render-thread stalls) make the
  improvement verifiable from logs on any machine. Buffer/note contents and
  typed queries are never logged.
- Visual parity with the GridView picker is preserved (7×38px cells, 19.8px
  glyphs, 15px headers, 240px scroll area, identical hover/press brushes).
