# ADR 0003: Emoji picker sprites from a pre-rasterized atlas

> _Last updated: 2026-07-07_

## Status
Accepted — 2026-07-07. Amends [02-EMOJI-PICKER-STATIC-GLYPH-SHEET.md](02-EMOJI-PICKER-STATIC-GLYPH-SHEET.md):
the static-sheet architecture stands; its glyph-cache warmer and paced reveal are replaced.

## Context
ADR 0002 rebuilt the picker as a static sheet of plain `TextBlock` glyphs and
identified first-time colour-glyph rasterization as the dominant first-open
cost (~6 ms/glyph on fast hardware). It paid that cost with an invisible
`EmojiGlyphCacheWarmer` that revealed throwaway glyphs 2 per
`CompositionTarget.Rendering` tick. Field logs from a slower laptop (200% DPI)
showed the approach failing in practice:

- The warmer needed **~17.4 s of continuously rendering, visible window time**
  (~28 ms/glyph there). `CompositionTarget.Rendering` does not tick while the
  window is hidden, and QuinSlate is a tray app that hides on focus loss, so
  the warm silently froze mid-way and completed in only one of six sessions —
  even one where the picker opened 2m20s after launch still saw a cold cache.
- With the cache cold, first open settled in ~9 s with 1.7 s frame stalls and
  took up to 18 s to reveal the full sheet.
- The glyph cache is process-wide only, so every launch re-paid the full cost.
  A "warm invisibly at startup" strategy structurally cannot work for an app
  that spends most of its life hidden in the tray.

The slow operation is DirectWrite rasterizing Segoe UI Emoji COLR glyphs, so
the fix is to stop rasterizing fonts at runtime altogether.

## Decision
Render all picker emoji **once, at build time**, into a single sprite atlas
shipped with the package, and draw the sheet from decoded bitmaps:

- **Build-time generation.** `QuinSlate.AssetGenerator emoji-atlas` renders
  every `EmojiData` entry with Segoe UI Emoji into `Assets/EmojiAtlas.png` —
  a 25-column grid of 128 px cells at **4× scale** (32 logical px per sprite,
  glyphs at 19.8 px × 4), ink-centred per cell like the old TextBlocks.
  Rendering goes through **Win2D (Direct2D/DWriteCore)**, not Skia: Segoe UI
  Emoji on Windows 11 carries both the modern COLRv1 gradient glyphs (what
  DWrite — and therefore XAML/RichEdit — draws) and a flat COLRv0 fallback;
  Skia renders only the flat layers, whose solid fills look visibly more
  saturated than the emoji the editor shows. DWrite also shapes fallback
  fonts and ZWJ sequences exactly like a TextBlock. A metadata JSON
  carries a SHA-256 hash of the ordered emoji list; unit tests
  (`EmojiAtlasConsistencyTests`) recompute it from `EmojiData` so a stale
  atlas fails the test run instead of silently showing wrong sprites. The
  generator links the app's own `EmojiData`/`EmojiAtlasFormat` sources, so
  order and geometry cannot drift. The linked `EmojiAtlasFormat` is the single
  contract both sides compile.
- **One atlas covers all DPIs.** `EmojiSpriteAtlas` decodes the PNG at the
  display's exact `RasterizationScale` in one `BitmapDecoder` pass
  (Fant interpolation), then slices it into 627 per-emoji `WriteableBitmap`s
  (the `PixelBuffer.AsStream` idiom already used by
  `DitheredGradientBrushFactory`). Downscaling from 4× is visually lossless
  for dense colour emoji art — unlike text, there is no ClearType/subpixel
  concern — and standard scales (100–400%) land on integer cell sizes.
  The decode is re-run if the scale changes (checked on every prewarm/open).
  Load cost is a few hundred ms of mostly off-thread work, once per process.
- **Sheet cells are `Image` sprites.** `EmojiSpriteFactory` replaces
  `EmojiGlyphFactory`; sprites are fixed 32-px squares, so all measuring
  disappeared. The presenter and recent strip assign sources when the atlas
  (re)loads via `SpritesReady`. Everything else from ADR 0002 — single static
  canvas, reposition-only search, grid-math hit-testing, prewarm — is intact.
- **The warmer and paced reveal are deleted.** Drawing a cached bitmap costs
  a fraction of rasterizing a glyph, so all transitions (initial reveal,
  search keystrokes, return to browse) now apply in a single frame:
  `EmojiGlyphCacheWarmer`, `EmojiSheetRevealPlanner`, and the presenter's
  per-frame pacing (plus their tests and bench scenarios) are gone. The
  first-open settle/frame-delta Serilog instrumentation remains, now tagged
  with `sprite atlas loaded:` instead of `glyph cache warm:`, so the change
  is verifiable from logs on any machine.

## Alternatives considered
- **Rendering the atlas with SkiaSharp** — implemented first (it was already
  in the generator for SVG assets); rejected after side-by-side crops showed
  Skia drawing the flat COLRv0 fallback of Segoe UI Emoji, which reads as
  oversaturated next to the COLRv1 gradient glyphs DWrite renders in the
  editor. SkiaSharp offers no switch for this; Win2D goes through the same
  DWrite pipeline as the app.
- **Fixing the warmer** — impossible in principle: a hidden XAML window
  presents no frames, so nothing can rasterize into the glyph cache while the
  app sits in the tray; and slicing differently only redistributes the same
  ~17 s of render-thread work.
- **Render-once-per-machine, persist to disk** — keeps perfect fidelity with
  the installed font, but the first session on slow hardware still pays the
  full cost and still needs the unreliable warm to complete once.
- **627 individual PNGs** — no slicing code, but hundreds of package files
  and per-file decodes; one atlas is one decode and one asset.
- **Per-cell `ImageBrush` cropping of one shared bitmap** — avoids the
  per-sprite copies, but brush transform/alignment cropping is less
  predictable in lifted XAML than the `WriteableBitmap` path this codebase
  already exercises heavily.

## Consequences
- First open no longer depends on a warm-up having run: cold first open is
  the atlas decode (hundreds of ms, off the UI thread, normally finished long
  before the user opens the picker) instead of ~17 s of render-thread work
  per session on slow hardware. Search and scroll characteristics of ADR 0002
  are unchanged.
- The package carries a ~3.7 MB atlas PNG (gradient glyphs compress worse
  than flat fills); decoded sprites cost roughly
  10–17 MB of bitmap memory at 200–250% scale, of the same order as the old
  resident TextBlock visuals plus the warmer's throwaway set, and independent
  of how often the picker opens.
- Emoji visuals are frozen at build time: the picker shows the glyph designs
  of the build machine's Segoe UI Emoji, which can subtly differ from the OS
  font that renders the emoji once inserted into the editor. Cosmetic only;
  regenerating the atlas refreshes the designs.
- Adding/removing/reordering emoji in `EmojiData` requires regenerating the
  atlas (`dotnet run --project QuinSlate.AssetGenerator -p:Platform=x64 --
  emoji-atlas`, copy outputs to `QuinSlate.Ui/Assets/`); the consistency
  tests enforce this.
- A recent-strip emoji that is no longer in `EmojiData` has no sprite and is
  skipped (previously a TextBlock would have rendered any string).
- ZWJ/keycap emoji sequences would need a shaping engine the generator does
  not carry; it fails loudly on them. The current set is single-codepoint
  only.
