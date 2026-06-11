# SPEC: Calculation result highlight animation

## What
When a line is evaluated by the inline calculator, the appended result
value briefly receives a background highlight to signal that the value
was computed automatically, then the highlight fades away. At rest the
line looks like plain text — no persistent visual distinction.

---

## Animation sequence

The result value plays a two-phase animation immediately after the
rewrite:

**Phase 1 — Appear highlighted (instant)**
The result text is given a background color in the Windows accent color
at full opacity. No fade-in — it snaps in immediately so the signal is
crisp.

**Phase 2 — Fade out (1600 ms)**
The background color fades linearly from the accent color to the
Windows theme background color over 1600 ms. The result ends up visually
identical to the rest of the line.

Note: WinRT's `ITextCharacterFormat.BackgroundColor` ignores the alpha
channel, so the fade is implemented by interpolating the RGB toward the
editor's surface color rather than by fading alpha to transparent.

Total animation duration: ~1600 ms.

---

## Highlight color

Use the accent color from the active Windows theme
(`UISettings.GetColorValue(UIColorType.Accent)`). Do not hardcode a
color. This ensures the highlight looks intentional and on-brand in both
light and dark mode, and respects any custom accent the user has set in
Windows Settings.

The fade target is the Windows theme background color
(`UISettings.GetColorValue(UIColorType.Background)`). The result is
that the highlight visually blends into the editor surface and
disappears.

---

## Scope of the highlight

Highlight only the result value itself — not the ` = ` separator, not
any surrounding spaces.

    450 * 1.21 = 594.5
    ^^^^^^^^^^^^^^       not animated
                 ^^^^^   highlighted then faded

---

## Implementation

### Approach

Apply character-level background color formatting to the result range
in the `ITextDocument` immediately after the line rewrite. Use a
`DispatcherTimer` to step the background color's alpha from the accent
color to fully transparent over 1600 ms.

On each timer tick (aim for ~16 ms intervals for 60 fps), interpolate
the color (including alpha) linearly and re-apply it to the result
range via `ITextCharacterFormat.BackgroundColor`. Release the timer
when the fade completes.

### Only one animation at a time

If the user evaluates a second line before the first animation finishes,
snap the first result to its end state immediately and start the new
animation fresh. Do not queue or stack animations.

### No animation on file load

When the buffer is loaded from disk on startup, text is plain and no
animations run. The highlight is a session-only, moment-of-evaluation
signal — not a persistent result style.

### Cancel on user edit

If the user types or deletes text in the same editor while a fade is in
progress, snap the animation immediately to its end state (transparent
background, no highlight) and let the edit proceed. Distinguish real
edits from format-only `TextChanged` events by comparing document text
length against the length captured immediately after the rewrite.

---

## Edge cases

- **Theme change mid-session** — if the user switches Windows theme
  while a fade is in progress, the accent color used by the animation
  is the value sampled at the start of the animation. This is
  acceptable; do not add theme-change detection to the animation path.
