# Background gradient: the TPDF-dithered four-corner mesh

> _Last updated: 2026-07-14_

The window and editor surfaces are painted with a warm, logo-derived **four-corner bilinear
gradient mesh** (amber `#F2900F` / warm grey `#98948D`). Each corner of the surface has its own
colour and every pixel is the bilinear blend of the four; the top-right corner carries a faint
"whisper of amber" warmth while the rest stay near-neutral, giving dim, organic, non-linear depth
rather than a flat ramp. A plain XAML `LinearGradientBrush` is **8-bit per channel and does not
dither**, so on this dark, low-contrast field it shows visible "false-contour" banding lines.
Acrylic/Mica hide this with their built-in noise layer, but those backdrops were removed.

The fix is `DitheredGradientBrushFactory` (`Components/`). It computes the mesh colour **per pixel
in floating point** (bilinear across the four corners) and adds **triangular-PDF (TPDF) noise of ±1
quantization level before rounding** to 8-bit, then writes the pixels into a `WriteableBitmap`
exposed as an opaque `ImageBrush`.
Dithering *before* the quantization is the crucial part — it makes pixels near a band boundary
round up/down at random in proportion to the sub-level fraction, smearing the boundary away. (Note
what does **not** work: rendering an already-8-bit gradient and adding noise *on top* — the band
edges are already baked in, so the contours stay and you just get grain. Direct2D's own gradient
dithering via a high-precision stop collection was also too weak here. Both were tried.)

## Rules when touching this

- **Single source of truth for the colours: the `AppGradient{Start,End,CornerTR,CornerBL}{Dark,Light}`
  `Color` resources in `App.xaml`.** `Start`/`End` are the diagonal endpoints (top-left /
  bottom-right corners); `CornerTR`/`CornerBL` are the other two mesh corners. Change the gradient
  there and nowhere else. Everything else derives from them: `DitheredGradientBrushFactory` reads all
  four corners by key at runtime (the brush actually shown), the XAML fallback brushes
  (`AppBackgroundGradient` in `App.xaml`, `TextControlBackground*` in `BufferPanelResources.xaml`)
  reference `Start`/`End` via `ThemeResource`, and MainWindow's flash fill reads them via
  `DitheredGradientBrushFactory.MidColor` (the average of the four corners). (XAML has no `x:Static`
  and code cannot reliably read theme-keyed brushes, so the colours live in XAML resources and C#
  reads them by key.)
- The brush is **opaque** — required so the native text caret stays visible and ClearType keeps
  working (a transparent editor surface hides the caret).
- The XAML `AppBackgroundGradient` / `TextControlBackground*` brushes are only a deep **fallback**
  (used if even the code path below cannot run). They are linear gradients and **must never be the
  surface actually presented**: on this dark, low-contrast field they band, and the window vs.
  editor gradients meet in a visible seam. To avoid a banded flash before the dithered mesh is
  ready, `BufferPanel.ApplyFallbackBackground` paints the window and editors with the flat
  `DitheredGradientBrushFactory.MidColor` **synchronously in `Initialise`, before the window is
  first shown**, so the first composited frame is a uniform flat tone. The dithered mesh swaps in
  on load; flat-to-mesh is imperceptible because the mesh barely deviates from its mid-tone.
- The dithered brush is applied in code on `Loaded`, and rebuilt on `ActualThemeChanged` and on
  resize (`BufferPanel` debounces the resize rebuild; `TrayPeekPanel` is fixed-size). It overrides
  `TextControlBackground*` in each editor's own resource scope so the focus/hover visual states
  stay dithered, and re-enters the editor's visual state after applying (the focused state pins the
  background via `ThemeResource` when entered and won't otherwise pick up the swapped brush).
- **The window and editor meshes must swap in together (all-or-nothing), never the window alone.**
  At startup the panel's `Loaded` fires before the TabView has realized the selected tab's content,
  so the active editor has no size and its brush cannot be built yet. Applying the window mesh at
  that point flashes the full-window gradient through the still-unpainted editor area for a few
  frames and then visibly snaps when the editor paints its flat fallback over it (this was the
  "broken gradient on startup" artifact, captured frame-by-frame and fixed). When the editor brush
  cannot be built, `ApplyDitheredBackground` keeps the flat fallback on every surface and schedules
  a one-shot retry for when the editor is laid out (`ScheduleDitheredRetry`). Do not reorder this
  back to "window first, editors when ready".
- **Render at native pixel size, never stretch.** Each surface's bitmap is built at that element's
  DIP size × `XamlRoot.RasterizationScale` and shown 1:1. Dithering is a per-pixel pattern;
  stretching the bitmap blurs it and the 8-bit output re-quantizes, which brings the banding
  straight back — hence per-element sizing and rebuild on resize.
- The mesh is **bilinear** (C0-continuous, no interior creases), so the corner colours can differ
  freely without creating seam lines — the smoothness comes from the interpolation, not from the
  corners being collinear. Keep the per-corner deltas small so the field stays dim and barely
  visible; the dithering removes the banding that the resulting low-contrast ramp would otherwise
  show.
