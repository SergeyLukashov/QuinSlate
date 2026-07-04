# Spec #14 — Tab System Redesign

## Overview

Replace the current numbered, colour-coded fixed buffers with a tab bar integrated into
the title bar. Each tab carries a user-editable emoji and title. The five buffers remain
fixed — no add or remove affordance is exposed at any point.

---

## 1. Tab Bar Layout

The tab bar lives **inside the title bar**, using WinUI 3's `AppWindow.TitleBar`
customisation with `ExtendsContentIntoTitleBar = true`. The tab strip occupies the
draggable title area; empty space to the right of the last tab remains a drag region;
the tab labels themselves are not draggable.

- The content area below the tab bar fills **100 % of the remaining window height**
  (`window height − title bar height`). No toolbar row or additional chrome sits between
  the tab strip and the text editor.
- Title bar height follows the **Windows system default** for the current DPI/scale
  setting and is not overridden in code.
- The system title bar is removed via `OverlappedPresenter.SetBorderAndTitleBar(true, false)`
  (the resize border is kept). The pin toggle and a custom WinUI close button are drawn by
  the panel itself, sitting flush together at the far right of the title bar. They share one
  modern WinUI tooltip style; the close button uses the standard red hover treatment. There
  is no minimise button.
- The pin and close buttons are an **overlay in the panel root grid**, outside the
  `TabView`, so they are never subject to the `TabView` tab-strip layout and are always
  pinned flush to the top-right corner as a **tight pair** and fully visible at every window
  width. A fixed-width **transparent spacer** occupies the `TabView.TabStripFooter`; because
  `TabView` sets its right content column's `MinWidth` to the footer's desired width, this
  reservation forces the equal-width tabs (and the tab scroll buttons that appear only when
  tabs overflow at narrow widths) to stop short of the button cluster. The spacer width is
  deliberately wider than the button cluster by a fixed **gap** so a small visible gap always
  remains between the last tab and the pin button. A small right inset keeps the close button
  clear of the rounded top-right window corner. The reservation is computed from named
  constants: `footer = rightInset + 2 × buttonWidth + gap` (`8 + 2×40 + 12 = 100`), the
  cluster occupies the rightmost `rightInset + 2 × buttonWidth` (`88`), and the difference is
  the gap (`12`).

---

## 2. Fixed Tab Count

There are exactly **5 tabs**. No tab may be added or removed. The add-tab button (`+`)
and any per-tab close affordance (`×`) are **absent from the UI entirely** — they must
not appear even in a disabled state.

---

## 3. Tab Anatomy

Each tab renders as:

```
[emoji]  [title]
```

- Tab widths are **equal**, dividing the available title-bar width (the strip width minus
  the logo on the left and the reserved footer spacer on the right) evenly across all five
  tabs. `TabViewItemMaxWidth` is raised well above any realistic per-tab share, so on wide
  windows the five tabs **stretch to fill** the strip up to the footer reservation rather
  than capping early and leaving a large empty void; the only empty region on the right is
  the reserved footer (button cluster + gap). On narrow windows the tabs shrink to
  `TabViewItemMinWidth` and then the strip scrolls. The tabs never overlap or render under
  the button cluster.
- Long titles are **truncated with an ellipsis** (`…`) at the tab's inner boundary.
- **Active tab** — uses the WinUI 3 `TabView` selected-state treatment; background
  matches the content area so tab and editor read as one continuous surface.
- **Inactive tabs** — slightly muted; no separator lines between tabs.

---

## 4. Default Tab Definitions

Applied on first launch, or whenever `settings.json` contains no `tabs` entry.

| # | Emoji | Title   |
|---|-------|---------|
| 1 | 📋    | Scratch |
| 2 | ✅    | Tasks   |
| 3 | 💡    | Ideas   |
| 4 | 🔗    | Links   |
| 5 | 📖    | Notes   |

---

## 5. Editing Emoji and Title

### Trigger

**Double-click** (or `F2` when the tab strip has keyboard focus) on a tab label opens an
inline edit popover anchored to that tab. A single click still switches to that tab
normally.

### Popover Layout

```
┌─────────────────────────────────┐
│  [emoji button]  [____________] │  ← emoji picker trigger + title field
│                  10 / 12 chars  │
│              [Cancel]  [Save]   │
└─────────────────────────────────┘
```

**Emoji button** — opens a dedicated emoji picker grid (see §5.1).

**Title field** — plain text input, maximum **12 characters**, enforced client-side with a
live character counter (`n / 12`). No error dialog is shown; input is blocked at the
limit.

**Save** (`Enter` or button) — persists both fields to `settings.json` and updates the
tab label in place with no flicker.

**Cancel** (`Esc` or button) — discards changes and closes the popover.

### Validation Rules

- Empty titles are allowed (saving an empty title is permitted and does not revert to the previous value).
- If the user closes the popover without touching the emoji field, the existing emoji is
  retained unchanged.

### 5.1 Emoji Picker

The picker is a fully custom **static glyph sheet**: a single `Canvas` inside a
`ScrollViewer`, with one plain `TextBlock` per emoji and one per category header, all
created exactly once and kept alive for the app's lifetime. No NuGet package and no
virtualizing items control (GridView / ListView / ItemsRepeater) is used: over this
small, fixed dataset, virtualization pays UI-thread container realization exactly when
the user acts (first open, every search keystroke, first scroll), which produced visible
lag and freezes even on fast hardware. With the pre-built sheet, opening and scrolling
are pure composition work and searching only repositions the existing TextBlocks
(matches move into a compact grid under a "Matches" header; non-matches collapse) —
no UI element is created, destroyed, or rebound on any user action after the one-time
build, which `EmojiPicker.Prewarm()` performs off the critical path shortly after the
panel loads. The Windows system picker (`Win + .`) is **not** used — the in-app sheet
keeps the interaction contained to the panel.

Because a non-virtualized `ScrollViewer` re-renders its full content extent, a fully
visible sheet would rasterize every color-emoji glyph in the first presented frame — a
multi-second render-thread stall (~6 ms per glyph, measured). Two mechanisms absorb it:

- **Invisible glyph-cache warm-up at startup.** `EmojiGlyphCacheWarmer` hosts throwaway
  glyph TextBlocks in a 1×1-clipped, non-interactive host in the live window (measured
  to rasterize despite being invisible, unlike `Opacity=0`, which the compositor culls)
  and reveals 2 per frame starting 2 s after panel load, then removes itself. Color
  glyph rasterization is cached process-wide, so a warmed first open settles in tens of
  milliseconds.
- **Windowed paced transitions.** Glyphs are built collapsed, and every transition —
  initial reveal, each search keystroke, each return to browse — applies visibility
  cells-intersecting-the-viewport-window first (up to one slice of 28 synchronously so
  the visible region never blanks), then one slice per `CompositionTarget.Rendering`
  tick (`EmojiSheetRevealPlanner`). Reopening in fully-visible browse state skips
  re-pacing; pacing pauses while the picker is closed and resumes where it left off.

See ADR 0002 for the measured baseline and outcome numbers.

Requirements for the picker component:

- Grouped by standard Unicode category (Smileys, Objects, Symbols, etc.); all
  positions come from a pure, unit-tested calculator (`EmojiSheetLayoutCalculator`),
  and the reveal order/partitions from a pure, unit-tested planner
  (`EmojiSheetRevealPlanner`)
- Search / filter field at the top; filtering is applied synchronously per keystroke
  (repositioning is cheap enough that no debounce is needed), and `Enter` picks the
  first visible match
- Hover/press feedback is a single shared highlight border moved by pointer-to-cell
  math; single-click (tap) on a glyph selects it, updates the emoji button preview,
  and closes the picker grid
- Recently used row (last 7 selections, persisted in `settings.json`), rendered by a
  small pool of reused TextBlocks
- Accepted trade-off: no per-emoji automation peers and no per-item keyboard
  navigation — the sheet exposes a single "Emoji grid" automation name (see ADR 0002)

---

## 6. Persistence

### Active Tab on Relaunch

The panel **always opens on Tab 1** on relaunch. Active tab state is not persisted.

### settings.json Schema

`settings.json` gains a `tabs` array. Buffer content files are unchanged.

```json
{
  "hotkey": "Ctrl+Shift+Space",
  "pinned": false,
  "recentEmoji": ["🔥", "⭐", "📌", "✏️", "🧠", "🗂️", "📎"],
  "tabs": [
    { "id": 1, "emoji": "📋", "title": "Scratch" },
    { "id": 2, "emoji": "✅", "title": "Tasks"   },
    { "id": 3, "emoji": "💡", "title": "Ideas"   },
    { "id": 4, "emoji": "🔗", "title": "Links"   },
    { "id": 5, "emoji": "📖", "title": "Notes"   }
  ]
}
```

Buffer content remains in `buffer_1.json` … `buffer_5.json`, keyed by `id`.
**Renaming a tab does not rename or migrate its file.**

---

## 7. Tray Tooltip

When the peek preview window is enabled, the system tray icon tooltip is set to an empty string to suppress it, preventing it from competing with the custom peek preview UI.

When the peek preview window is disabled, the tray icon tooltip is set to the static application name `"QuinSlate"`, and no tab content or preview tooltip is shown on hover.

---

## 8. Keyboard Navigation

| Key | Action |
|-----|--------|
| `Ctrl+1` … `Ctrl+5` | Switch to tab N (active while panel is open) |
| `F2` with a tab focused | Open edit popover for that tab |
| `Enter` in edit popover | Save and close popover |
| `Esc` | Close edit popover without saving |
