# SPEC: Calculation result highlight animation

> _Last updated: 2026-07-09_

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

Note: the fade is a true alpha fade of the highlight background from the accent
colour to transparent (a CodeMirror decoration; the earlier RichEditBox path
faded RGB toward the surface colour because `ITextCharacterFormat.BackgroundColor`
ignores the alpha channel). The end state is identical: indistinguishable from
plain text.

Total animation duration: ~1600 ms.

---

## Highlight color

Use the accent color from the active Windows theme
(`UISettings.GetColorValue(UIColorType.Accent)`). Do not hardcode a
color. This ensures the highlight looks intentional and on-brand in both
light and dark mode, and respects any custom accent the user has set in
Windows Settings.

Read it from `UISettings`, **not** from the `SystemAccentColor` XAML resource:
`UISettings.ColorValuesChanged` can fire before XAML has refreshed its theme
resources, so a handler that reads the resource may still see the old accent.
The panel subscribes to that event (marshalling to the UI thread) and re-sends
the editor colors, so a live accent change applies without restarting the app.

The same accent, fully opaque, is the editor's **text selection** color — the
retired `RichEditBox` took its `SelectionHighlightColor` from
`TextControlSelectionHighlightColor`, which resolves to
`AccentFillColorSelectedTextBackgroundBrush` and thus to `SystemAccentColor`.
In CodeMirror the override must mirror the base theme's full selector
(`&.cm-focused > .cm-scroller > .cm-selectionLayer .cm-selectionBackground`);
a shorter selector loses on CSS specificity while the editor has focus.

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

The highlight is a CodeMirror **mark decoration** over the result range,
applied by the editor page immediately after the line rewrite. The decoration
carries a CSS animation that fades its `background-color` from the accent colour
to `transparent` over 1600 ms, then the decoration is removed so the end state
is plain text. The accent colour is supplied by the host (sampled per animation)
and captured on the decoration element (an inline `--calc-accent` custom
property), so it is stable across a mid-fade theme change. No per-tick timer or
per-frame reformatting is needed — the browser compositor drives the fade.

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
progress, the highlight decoration is cleared immediately (any document-changing
transaction that is not the calc rewrite drops it) and the edit proceeds.

---

## Edge cases

- **Theme change mid-session** — if the user switches Windows theme
  while a fade is in progress, the accent color used by the animation
  is the value sampled at the start of the animation. This is
  acceptable; do not add theme-change detection to the animation path.
