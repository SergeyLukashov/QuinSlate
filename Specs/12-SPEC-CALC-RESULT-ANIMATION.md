# SPEC: Calculation result highlight animation

## What
When a line is evaluated by the inline calculator, the appended result
briefly highlights to signal that the value was computed automatically,
then fades back to match the surrounding text. At rest the line looks
like plain text — no persistent visual distinction.

---

## Animation sequence

The result portion (everything after and including ` = `) plays a
two-phase animation immediately after the rewrite:

**Phase 1 — Appear highlighted (instant)**
The result text is rendered with a distinct foreground color at full
opacity. No fade-in — it snaps to the highlight color immediately so the
signal is crisp.

**Phase 2 — Fade to normal (300 ms)**
The highlight color fades linearly to the normal editor foreground color
over 300 ms. The result ends up visually identical to the rest of the
line. No italic, no gray, no persistent treatment — just text.

Total animation duration: ~350 ms (instant snap + 300 ms fade).

---

## Highlight color

Use the accent color from the active Windows theme
(`SystemAccentColor` / `ColorHelper.ToDisplayName`). Do not hardcode a
color. This ensures the highlight looks intentional and on-brand in both
light and dark mode, and respects any custom accent the user has set in
Windows Settings.

The fade target is the control's normal foreground — whatever the
`RichEditBox` text color is in the current theme. Do not assume white or
black.

---

## Scope of the highlight

Highlight only the result portion of the line — everything from and
including the ` = ` separator to the end of the line. The expression the
user typed is not highlighted or animated.

    450 * 1.21 = 594.5
    ^^^^^^^^^^^          not animated
                ^^^^^^   highlighted then faded

---

## Implementation

### Approach

Apply character-level foreground color formatting to the result range in
the `ITextDocument` immediately after the line rewrite. Use a
`DispatcherTimer` or a WinUI animation storyboard to step the foreground
color from the accent color to the normal foreground color over 300 ms.

On each timer tick (aim for ~16 ms intervals for 60 fps), interpolate the
foreground color linearly and re-apply it to the result range via
`ITextCharacterFormat.ForegroundColor`. Release the timer and clear the
format override when the fade completes.

### Only one animation at a time

If the user evaluates a second line before the first animation finishes,
snap the first result to normal immediately and start the new animation
fresh. Do not queue or stack animations.

### No animation on file load

When the buffer is loaded from disk on startup, text is plain and no
animations run. The highlight is a session-only, moment-of-evaluation
signal — not a persistent result style.

---

## Edge cases

- **User edits the result line during animation** — snap the animation
  immediately to its end state (normal foreground) and let the edit
  proceed. Do not fight the user's cursor.

- **Theme change mid-session** — if the user switches Windows theme while
  a fade is in progress, the animation may briefly show the wrong target
  color. This is acceptable; do not add theme-change detection to the
  animation path.

- **Reduced motion** — check `UISettings.AnimationsEnabled`. If the user
  has turned off animations in Windows accessibility settings, skip the
  fade entirely and apply the final state (normal foreground, no color
  override) immediately after the line rewrite.
