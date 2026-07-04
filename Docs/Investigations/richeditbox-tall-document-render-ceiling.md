# Investigation: Text Not Rendered at the Bottom of Long Tabs

## Status

**KNOWN PLATFORM LIMITATION — mitigated, not eliminated** (2026-07-04). The editor is a
`RichEditBox`, which does not virtualize and stops painting glyphs past a fixed rendered height
while still keeping the characters in the document (they remain selectable). This is the documented
Microsoft limitation, not a bug in QuinSlate. Mitigated by lowering `AppConstants.MaxBufferLength`
from 100,000 to 50,000 chars so realistic buffers stay clear of the ceiling. It is a practical, not
absolute, guarantee (see "Why the cap is not an absolute fix").

---

## Symptom

In a long buffer, text near the bottom is not rendered — the glyphs simply do not paint. The text is
still there: it can be selected (drag-select or Ctrl+A highlights it), copied, and is written to disk
intact. Only the on-screen painting stops. Reported reproducing "starting from the 9,240th line."

---

## Root cause

The buffer editor is a `RichEditBox` (`EditorViewBuilder.Build`). `RichEditBox` renders its **entire**
document into a single composition surface — it has no virtualization — and past a large rendered
height the text engine stops emitting glyphs even though the underlying document (the TOM
`ITextDocument`) still holds every character and tracks selection. This is the limitation Microsoft
acknowledges in [microsoft-ui-xaml#1842](https://github.com/microsoft/microsoft-ui-xaml/issues/1842)
("the native text controls … are not able to properly handle large files").

The cutoff is a **rendered-height ceiling** (~260k device pixels in the observed case), not a
character or logical-line count:

- Font is 15px with `LineSpacingRule.Multiple 1.4` (`EditorViewBuilder`), giving ≈28px per line.
  ~9,240 lines × ≈28px ≈ 260k px — a fixed height, matching the report.
- Because it is height-based, the exact line number where it bites **shifts with display DPI**
  (rendered pixels = DIP height × `XamlRoot.RasterizationScale`) **and with line spacing**. "9,240"
  is specific to one machine/content, not universal.

### Ruled out (it is not any of these)

- **Not the length cap.** The text exists and selects; the buffer was under the 100,000-char limit.
- **Not a clip or height cap in QuinSlate.** There is no `MaxHeight`, no `Clip`, no
  `RectangleGeometry` on the editor or its container. `SmoothScrollController` only animates the
  scroll offset (and even notes the control's "lazy text measurement during downwards scrolling") —
  nothing truncates rendering.
- **Not the dithered gradient bitmap.** That bitmap is sized to the editor's *viewport*, not the
  scrollable content height, so it plays no part in the tail going blank.

---

## Mitigation applied

`AppConstants.MaxBufferLength`: **100,000 → 50,000**. This caps `RichEditBox.MaxLength`, paste
truncation, and the pre-write clamp (`BufferService.ClampToMaxLength`). At 50k chars, realistic
content stays far below the render ceiling (the reported ~92k-char / 9,240-line document halves to
well under it).

**Data-loss note:** the pre-write clamp truncates on the next debounced write, so any *existing*
buffer already larger than 50,000 chars loses its tail (the portion past 50k) the next time it is
saved. This was an accepted tradeoff when the cap was lowered.

## Why the cap is not an absolute fix

50,000 chars does **not** mathematically guarantee rendering: 50,000 single-character lines would
still be 50,000 lines tall and exceed the ceiling. No char cap that preserves useful capacity can
guarantee it, because the ceiling is a height, and height depends on line count × line height × DPI.
The cap is chosen to clear *realistic* content with wide margin, not pathological all-newline input.

## The only true fix (not done)

Eliminating the ceiling requires a **virtualized** text surface — e.g. a `RichTextBlock`/`TextBlock`
inside an `ItemsRepeater`/`ListView` that realizes only visible lines, or a paged editor. That is a
large change touching the gradient, smooth-scroll, inline-calc, and selection integration, and was
out of scope for this fix.
