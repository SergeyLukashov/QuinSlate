# Search

> _Last updated: 2026-10-27_

Press **Ctrl+F** and a small search pill floats in over the top-right corner of
the editor. Type a word and all five slates are searched at once. Matches
light up in the text, each tab with a hit shows a count badge, and **Enter**
steps through every match in tab-strip order, switching tabs as it goes.
Search is **forgiving**: case, accents and word order don't matter, and a typo
still finds what you meant.

Search **writes nothing to the document** and has no replace. Highlights are
mark decorations, like [links](21-LINKS.md) and
[markdown formatting](23-MARKDOWN-FORMATTING.md), so the buffer `.txt` files are
never touched and undo has nothing to unwind. Matching runs page-side, where all
five CodeMirror states already live. The pill, the tab badges and tab switching
are the host's.

```
┌ ✦ │ 📋 Scratch │ ✅ Tasks  3 │ 💡 Ideas │ 🔗 Links  1 │ 📖 Notes │   📌 ─ □ ✕ ┐
│                          ╭─────────────────────────────────────────╮     │
│  Buy [milk] and eggs     │  ⌕  milk              2 of 6   ˄  ˅  ✕  │     │
│  - [ ] oat [milk], 2 l   ╰─────────────────────────────────────────╯     │
│  ▓milk▓ the cow — joke                                                   │
│                                                                          │
└──────────────────────────────────────────────────────────────────────────┘
        [ ] other match     ▓ ▓ current match     3  tab badge (InfoBadge)
```

## What matches

Modern search tools don't make you choose between exact and fuzzy. They start
precise and loosen only when that finds nothing, the way a web search says
"Showing results for…". Search here works the same way. A query is tried
against three **tiers** in order, and the first tier that finds anything **in
any slate** is the one shown. There are no toggles for case, whole words or
regex, and none are needed.

### Folding (all tiers)

The query and the text are compared after both are folded:

| Fold | Example |
|---|---|
| Case | `MILK` = `milk` |
| Accents and diacritics (NFD, combining marks dropped) | `cafe` = `Café`, `muller` = `Müller` |
| Typographic punctuation | `"` = `“ ”`, `'` = `‘ ’`, `-` = `– —` |
| Whitespace runs, including NBSP | `oat milk` = `oat   milk` |
| Hidden markdown markers | `ship friday` = `Ship **Friday**` |

The last row makes search **match what the user sees**. The emphasis, strike,
code and heading markers that [23-MARKDOWN-FORMATTING.md](23-MARKDOWN-FORMATTING.md)
hides are left out of the folded text. A bold word in the middle of a phrase no
longer breaks the phrase, and `ship friday` finds a line that renders as
"Ship **Friday**". The highlight still covers the original range, markers
included. They are zero-width while hidden, so the user never sees them
highlighted. Markers come from the same `scanInline`/`parseBlock` scanners the
renderer uses, so search and rendering always agree on what a marker is.

Text inside a task or list item's **atomic prefix** (`- [ ] `, `1. `) never
matches. The caret cannot stand inside those ranges, so a match there could be
neither shown nor landed on.

### The three tiers

| Tier | A match is… | Shown when |
|---|---|---|
| **1 · Phrase** | the whole folded query, as a substring | it finds anything, anywhere |
| **2 · All words** | a line containing **every** query word, in any order | the query has 2+ words and tier 1 found nothing |
| **3 · Close** | a line where every query word is found, allowing a typo or two | tiers 1 and 2 found nothing |

Every tier is **line-local**. A phrase does not match across a line break, and
the words of a tier-2 or tier-3 query must share a line. This is the same
line-local stance the markdown scanners take, and it's what keeps a match
something the user can see in one place.

**Tier 3 typo tolerance.** A query word of four or more letters may be off by
**one** edit, or **two** from eight letters up. An edit is an insertion,
deletion, substitution or swap of two neighbouring letters (Damerau–Levenshtein).
The word is compared against the **start** of each word in the text, so
`recieve` finds `received`. Words under four letters never fuzz: at that length
one edit reaches half the dictionary.

| Query | Text | Tier | Why |
|---|---|---|---|
| `cafe` | `Café au lait` | Phrase | Accents fold |
| `oat milk` | `Oat  milk` | Phrase | Case and whitespace fold |
| `ship friday` | `Ship **Friday**` | Phrase | Hidden markers don't count |
| `milk oat` | `oat milk, 2 l` | All words | Any order, same line |
| `mlik` | `milk` | Close | One swap |
| `recieve` | `received the parcel` | Close | One swap, against the word's start |
| `definately` | `definitely` | Close | One substitution (two allowed at 8+ letters) |
| `milk` | `silk scarf` | Close, **only if** `milk` appears in no slate | Fuzzy never dilutes exact hits |
| `cat` | `cart` | None | Under four letters never fuzzes |

### Why tiers, and not fzf-style fuzzy

- **Fuzzy only as a fallback.** When fuzzy and exact hits are mixed, `milk`
  lights up `silk`, `mild` and `mile` next to every real `milk`. In running
  prose that is noise, not recall. The tier is chosen **globally** across all
  five slates, so every badge and highlight on screen comes from the same tier.
- **No subsequence matching.** The fzf/VS Code quick-open model (`bmk` →
  "**b**uy **m**il**k**") is built for picking among short names like files and
  commands. In paragraphs of notes almost every line contains almost every
  three-letter subsequence, so it would match everything.
- **No ranking.** Matches are walked in position order (see
  [Stepping through matches](#stepping-through-matches)). The tiers already put
  the most relevant hits first. A scratchpad's matches are few, and a ranked
  list that reorders itself as you type is harder to follow than one that runs
  top to bottom.

## The pill

A small floating bar anchored to the **top-right of the editor area**. It is
declared after `EditorHostContent` in `BufferPanel.xaml` so it paints over the
WebView2, just as `LimitNoticeView` does.

```
╭──────────────────────────────────────────╮
│  ⌕   milk                2 of 6   ˄ ˅  ✕ │   typing: live results
│  ⌕   milk                6 results ˄ ˅ ✕ │   active tab has none yet: total only
│  ⌕   mlik              ≈ 1 of 3   ˄ ˅  ✕ │   tier 3: close matches
│  ⌕   quokka             No results      ✕ │   muted; arrows disabled
╰──────────────────────────────────────────╯
```

- **Contents, left to right:** a search glyph (Segoe Fluent `Search`), a
  borderless `TextBox` (placeholder "Search all slates", spell-check off), the
  counter, previous/next buttons (`ChevronUp`/`ChevronDown`) and a close button
  (`Cancel`). The buttons are transparent at rest like the title-bar cluster,
  and each has a tooltip with its shortcut.
- **Size and place:** 330 DIP wide (the 360 DIP minimum panel less a 15 DIP
  gutter each side), clamped to the editor width minus the same gutters. The
  editor is always narrower than the window by its frame, so at the minimum
  panel width the clamp wins and the pill sits exactly 15 DIP from both window
  borders, whatever the frame thickness. The gutter is narrower than the
  editor's 16 DIP text inset, so the pill's left edge covers the start of the
  lines beneath it instead of leaving a sliver of glyphs showing. It is inset
  12 DIP from the editor's top edge.
- **Chrome** is the tab-edit flyout's presenter: the stock `FlyoutPresenter`
  background (in-app acrylic), `FlyoutBorderThemeBrush` border at
  `FlyoutBorderThemeThickness`, `OverlayCornerRadius` (8 px) and its 8 px
  inset before the glyph (4 px on the other edges, so the pill stays one
  button high), with a primary-text search glyph spaced from the text as the limit notice's
  glyph is, and a Composition `DropShadow`. The shadow is
  hand-rolled for the reason given in
  [18-CHARACTER-LIMIT-NOTICE.md](18-CHARACTER-LIMIT-NOTICE.md): a `ThemeShadow`
  has no XAML receiver behind it and would draw nothing over the WebView2.
- **Motion:** it slides **down** 8 px and fades in on open, the notice's
  slide-and-fade mirrored for a top-anchored surface. Closing has no
  animation: the pill disappears at once.
- **It never covers the match it points at.** While the pill is open the page
  adds a top `EditorView.scrollMargins` equal to the pill's height plus a gap
  (the host sends the height in CSS px). Every scroll-to-match therefore lands
  below it, and the rest of the text can still scroll underneath.
- **It never traps the top of a slate.** A scroll margin alone cannot help at
  the very top, where there is nothing left to scroll: the ends of the first
  long lines would sit under the pill with no way out. So while the pill is
  open the content also gets a **top spacer** of the same height, the way VS
  Code's find widget adds space on top. It is content padding driven by the
  `--search-spacer` CSS variable (`searchSpacer.js`), so it scrolls with the text.
  - **Opening moves nothing.** The spacer is added above the viewport and the
    scroll position takes up its height, even at the top of a slate. The first
    lines stay where they were, under the pill, until the user scrolls up into
    the spacer, or a match there is revealed.
  - **Short slates too.** The content's minimum height grows by the spacer
    (`calc(100% + spacer)`), so a slate too short to scroll still has room to
    hide the spacer and to reveal it.
  - **Eased.** Every change of the spacer's height (open, close, a pill that
    grows) runs over 200 ms with a cubic ease-out, matching the pill's own
    motion. Each frame places the scroller so the text holds still wherever the
    scroll position can follow. Text visibly moves only where it cannot: on
    close after the user scrolled up into the spacer, the text glides back up
    to the top. When a match on the first lines is revealed while the spacer is
    still growing, the text glides down clear of the pill. With Windows
    animation effects off (`prefers-reduced-motion`), the change is instant.
  - **Anything that scrolls mid-change wins.** A user scroll, or a match
    reveal, during the ease moves the reference the frames hold to. Nothing
    fights them. A scroll all the way up means the top of the slate, so the text
    follows the spacer from there.
  - **Tab switches keep it hidden.** A slate's saved scroll position records the
    spacer height it was taken with. On return the offset is shifted by the
    difference, so a slate left before search opened also has the spacer out of
    sight above it. A slate left scrolled into the spacer comes back at its top
    once the pill has closed.
- **"No results" stays calm.** The counter turns muted. There is no red box,
  shake or sound, the same way the limit notice handles a full slate.

### The counter

| Text | Meaning |
|---|---|
| `2 of 6` | The current match's position among **all** matches, in walk order across tabs |
| `6 results` | There are matches, but none in the active tab yet. Enter jumps to the first |
| `≈ 1 of 3` | Tier 3. Tooltip: "No exact matches — showing close ones" |
| `No results` | Nothing in any tier, in any slate |
| `9,999+` | Counting stops at 9,999 per slate. The pill says so rather than hang on `e` in a full slate |

The counter is a polite UIA live region, so a screen reader announces
"2 of 6" as it changes. The accessible text for tier 3 spells out "close
matches" in place of the `≈`.

### Tab badges

While a query has results, every tab whose slate holds a match shows a WinUI
`InfoBadge` with its count, capped at `99+`. The badge sits flush against the
tab's right edge, and is centred on the emoji's ink rather than its line box
(Segoe UI Emoji draws ~1 DIP below its box centre). The active tab
shows one too. A tab with no match shows nothing, and there is no `0`. The
badge never truncates. The title ellipsizes first
([14-TABS-REDESIGN.md § 3](14-TABS-REDESIGN.md#3-tab-anatomy)). Badges leave
with the pill.

- **Tinted by default.** A badge is the accent at about 22% opacity with
  accent-coloured digits (`AccentTextFillColorPrimaryBrush`), the badge
  equivalent of the editor's other-match highlight in [§ Highlighting](#highlighting).
- **Solid on the current match's tab.** The tab whose slate holds the current
  match gets the solid accent fill with on-accent digits, as the current match
  is the stronger highlight in the text. Only colour changes between the two,
  so the badge never changes size and the titles don't shift as the walk moves.
- **Digits.** 11px SemiBold, with tight line bounds so the digits sit
  optically centred in the 16px capsule.
- **Motion.** A badge scales up from 60% and fades in when it appears (150 ms),
  and does the reverse when it goes (120 ms), both easing out. A count change
  doesn't animate. With animations turned off in Windows, badges appear and go
  at once. The title reclaims a leaving badge's room straight away.
- **High contrast.** Both kinds use the system highlight pair
  (`SystemColorHighlightColor` fill, `SystemColorHighlightTextColor` digits).

## Keyboard

| Key | Where | Result |
|---|---|---|
| **Ctrl+F** | Editor or chrome | Opens the pill with focus in the box and its text selected. If the pill is already open, re-focuses and re-selects |
| **Enter** | Box | Next match; crosses into the next tab that has one, wrapping 5 → 1 |
| **Shift+Enter** | Box | Previous match, wrapping 1 → 5 |
| **F3** / **Shift+F3** | Box or editor, pill open | Next / previous match, as Enter / Shift+Enter |
| **F3** / **Shift+F3** | Editor, pill closed | Re-opens the pill with the last query and steps once |
| **Esc** | Box or editor, pill open | Closes the pill, **selects the current match**, focuses the editor |
| **Ctrl+1–5**, **Ctrl+Tab** | Box | Switch tabs as usual; the pill stays open with focus in the box |
| **Tab** | Box | Standard focus traversal through the pill's buttons |

**Seeding.** If the editor holds a non-empty selection within one line when
Ctrl+F is pressed, the selection becomes the query, minus the hidden markdown
markers and list prefix that [folding](#folding-all-tiers) leaves out. The
caret line shows its markers, so a drag selection over `Ship **Friday**` picks
them up. The seed is `Ship Friday`, which finds its own line as a phrase. A
selection of nothing but markers counts as no selection. Without a selection
the pill opens on the last query of the session, already selected, so typing
replaces it. The query is never persisted.

Inside the editor, Ctrl+F and F3 are captured by `panelShortcuts.js` and
forwarded as `key` commands (`find-open`, `find-next`, `find-prev`,
`find-close`), because keys typed in the WebView2 never reach XAML. From
chrome they go through `BufferKeyboardController.HandlePanelPreviewKey`. Both
land in the same handler, as [06-KEYBOARD-NAV.md](06-KEYBOARD-NAV.md) requires.
Esc in the editor is bound only while the pill is open. At any other time Esc
stays unbound, as it is today.

**Enter in the editor stays a newline.** The pill stays open while you type in
the text (see [Lifecycle](#lifecycle)), and a search that stole Enter would make
the editor unusable until it was closed. From the editor, F3 is the step key.
Enter steps only while focus is in the box.

## Stepping through matches

**Walk order** is tab-strip order, then document order within a slate. Tab
order is the order the strip shows, not buffer ids, so after a drag-reorder
Enter follows the new order. The tray peek does the same
([07-BUFFER-PEEK.md](07-BUFFER-PEEK.md)).

- **As you type**, results update live. The current match becomes the first one
  at or after the caret in the active tab, and it scrolls into view. **Typing
  never switches tabs.** If the active tab has no match, the counter shows the
  total and the badges show where the matches are.
- **Enter** is the only thing that switches tabs. Crossing into another slate
  selects that tab through the host's normal tab-switch path, the same one
  Ctrl+N uses. The usual focus-the-editor step is skipped while the search box
  has focus, so you can keep pressing Enter. On arrival the view scrolls to the
  match. The destination tab's saved scroll position is otherwise untouched.
- **While the box has focus, the current match is a highlight, not the
  selection.** The caret stays where it was until the search ends. Esc then
  turns the current match into the selection, ready to be typed over, copied or
  read in context. Arriving there also reveals its line's markdown markers, as
  any selection does.
- **Clicking into the text** while the pill is open leaves the pill open and
  places the caret normally. The match at or after the caret becomes current
  without being landed on, so the next step (F3, or Enter back in the box)
  lands on it rather than skipping past it.
- **F3 with the pill closed** reopens it on the last query and steps from the
  caret (`searchStep` carries `fromCaret`). A forward step starts past the
  selection, so after Esc has selected a match, F3 moves to the next one.
- **Tier 2 and 3 matches are lines.** Each query word's occurrence is
  highlighted, and a step moves to the next matching line.

## Highlighting

| Class | Style (starting values, confirm in the app) |
|---|---|
| `.cm-search-match` | Background `color-mix(in srgb, var(--accent) 28%, transparent)`, `border-radius: 2px` |
| `.cm-search-current` | Background `color-mix(in srgb, var(--accent) 55%, transparent)` plus a 1px inset ring of `color-mix(in srgb, var(--accent) 45%, var(--text))` — brighter than the fill on dark, darker on light |

- The highlight is **accent as a fill**, the role [21-LINKS.md § Colour](21-LINKS.md#colour)
  reserves for `--accent`. Text keeps its own colour on top, and at these alphas
  it stays legible on both meshes. Confirm contrast on screen, because jsdom
  cannot measure it.
- Highlights cover **only the active tab's `visibleRanges`**. The counts for the
  other four slates come from matching and don't need decorations.
- No horizontal padding on the marks, for the same monospace-grid reason
  `.cm-md-code` gives in [23-MARKDOWN-FORMATTING.md § Rendering](23-MARKDOWN-FORMATTING.md#rendering).
- They coexist with the selection, links, formatting and the calc highlight,
  which stays on top.
- Closing the pill removes every highlight in the same frame, and every badge starts
  its hide in it (see [§ Tab badges](#tab-badges)).

## Lifecycle

- **Edits while searching.** A change to a slate re-matches that slate only,
  and the counter, badges and highlights follow. If the current match is edited
  away, the next match after its old position becomes current. A `setText` from
  the host re-matches the slate too.
- **Panel hide closes the search.** A re-summoned panel never opens on a stale
  search, the same rule the limit notice follows. The last query is kept for
  the session, so Ctrl+F brings it straight back.
- **Pinning** ([09-PIN-WINDOW.md](09-PIN-WINDOW.md)) has no effect on search.

## Performance

Search runs on every keystroke across up to five slates of up to
1,000,000 characters each, so its cost is part of the design from the start.
It is not something to tune later.

- **Folded text is cached per line** and rebuilt only for lines a transaction
  touches. It holds the folded string and an offset map back to document
  positions (none at all for a plain one-to-one ASCII line). A keystroke in the
  query refolds nothing.
- **Folding runs ahead of the first query.** Once the buffers load, the page
  folds the slates in idle time (`requestIdleCallback`), the active one first,
  in slices of at most 8 ms. Folding also indexes each line's words for tier 3.
  Ctrl+F usually sends the last query immediately, so starting the fold when the
  pill opens would be too late: five full slates take a few hundred milliseconds
  to fold. While the pill is open, any folding that's left runs back to back in
  8 ms slices. A query that arrives first finishes the folding itself. The cache
  lives for the session. An edit keeps the folded lines above it, even in a
  partly folded slate. A host `setText` re-queues the idle fold, so a reopen is
  warm.
- **Matches are cached per slate and tier** against the document they came from,
  so an edit re-matches only its own slate.
- **One search per frame.** Query changes coalesce to `requestAnimationFrame`,
  and the latest query wins.
- **Tier 3 is bounded.** The edit-distance check stops as soon as it exceeds
  the allowed edits, which keeps it O(word length × k). Words are pre-filtered
  by length (a word can't be within k edits of a much shorter or longer one).
- **Budgets:** under 16 ms from keystroke to highlight at typical sizes (five
  slates of 50 KB). Under 100 ms at the worst case (five full slates, a tier-3
  query). Measure both with page-side timings through `pageLog.js`. If a budget
  is missed, change the data structures. Don't add a typing debounce, because
  lag the user can feel is a design problem, not a tuning knob.

## Bridge

**The query is treated as buffer text.** It is a fragment of the user's notes,
so it is never logged on either side. Only message names, lengths, counts and
the tier are logged, in keeping with the rule in
[../Wiki/06-WEB-EDITOR-BUNDLE.md](../Wiki/06-WEB-EDITOR-BUNDLE.md).

| Message | Direction | Payload |
|---|---|---|
| `searchQuery` | host → page | `query`, `tabOrder` (buffer indices in strip order), `scrollMarginTop`. An empty query clears |
| `searchStep` | host → page | `direction`: `next` or `prev`; `fromCaret`: drop the current match and step from the caret |
| `searchClose` | host → page | `selectCurrent`: whether the current match becomes the selection |
| `searchResults` | page → host | `counts[5]`, `capped[5]`, `tier`, `current` (`{ index, ordinal }` or null). **No text** |
| `searchNavigate` | page → host | `index`: the slate the next match is in; the host selects that tab, then the page scrolls to the match |
| `key` | page → host | `command: "find-open"` carries `seed`, the one-line selection (≤ 256 chars) minus its hidden markers. Buffer text: never logged |

## Where it lives

| Piece | Where |
|---|---|
| Folding and the per-line fold cache | `QuinSlate.Ui/WebEditor/build/src/searchFold.js` |
| Tiered matcher (pure: folded lines + query → matches) | `QuinSlate.Ui/WebEditor/build/src/searchMatch.js` |
| Bounded Damerau–Levenshtein against word starts | `QuinSlate.Ui/WebEditor/build/src/fuzzyDistance.js` |
| Walk order, stepping, relocation after an edit (pure) | `QuinSlate.Ui/WebEditor/build/src/searchWalk.js` |
| State across five slates, messages | `QuinSlate.Ui/WebEditor/build/src/search.js` |
| Match and current decorations | `QuinSlate.Ui/WebEditor/build/src/searchHighlight.js` + `editor.css` |
| Ctrl+F / F3 / Esc forwarding | `panelShortcuts.js` |
| Bridge events | `QuinSlate.Ui/Components/EditorSearchChannel.cs`, reached through `EditorHost.Search` |
| Open/close, seeding, step routing, tab switch | `QuinSlate.Ui/Components/SearchController.cs` |
| The pill itself | `QuinSlate.Ui/Components/SearchPillView.xaml`(`.cs`) |
| Counter wording | `QuinSlate.Ui/Components/SearchCounterFormatter.cs` |
| Tab badges | `QuinSlate.Ui/Components/TabHeaderView.cs` (the badge), `SearchCountBadgeStyle` in `Themes/BufferPanelResources.xaml`, `SearchBadgeMotion.cs` (show/hide motion) |

## Logging

One Information line when a search closes: the query **length**, the tier it
ended on, the total count, how many steps were taken and how many tabs were
crossed. Nothing per keystroke. No query text or match text, ever.

## Testing

Pure matcher modules in `build/test/`, one `*.test.mjs` per module, as in the
rest of the bundle. Cases to cover, at minimum:

- **Folding:** case; `é`/`ü`/`ñ` against plain letters; curly quotes and
  dashes; NBSP and whitespace runs; a phrase across a hidden `**` and a hidden
  heading marker; offsets that map back exactly through all of these; a
  surrogate-pair emoji before the match.
- **Tiers:** tier 1 winning over a tier-3 hit elsewhere; tier 2 any-order and
  same-line only; tier 3 swap, insertion, deletion and substitution; the 4- and
  8-letter edit thresholds; word-start matching (`recieve` → `received`);
  3-letter words never fuzzing; the tier chosen globally across slates.
- **Scope:** no match inside a task or list prefix; no match across a line
  break; a match inside a code span and a URL (both are text, both match).
- **Walk:** tab-order stepping and wrap in both directions; a reordered
  `tabOrder`; the first match at or after the caret; an edit deleting the
  current match; the 9,999 cap.
- **Spacer:** the ease; the spacer hidden above the viewport on open, even at
  the top; a close from inside the spacer gliding the text up; a scroll or
  reveal mid-change respected, and one to the top followed; reopening
  mid-close; reduced motion; realigning a restored slate; never scrolling
  above the top.

In the app (jsdom does not do layout): pill placement at the minimum panel
size and at 125% and 250% scaling, highlight contrast on both meshes, the
scroll margin keeping the current match clear of the pill, the spacer
opening with no visible movement (short and long slates) and the close glide, focus staying in
the box across an Enter that switches tabs, and Narrator announcing the
counter.

## Documentation updates (part of this work)

- [06-KEYBOARD-NAV.md](06-KEYBOARD-NAV.md): add Ctrl+F, F3/Shift+F3 and Esc.
- `CLAUDE.md` and [../Wiki/06-WEB-EDITOR-BUNDLE.md](../Wiki/06-WEB-EDITOR-BUNDLE.md):
  add `searchQuery` to the list of never-logged bridge messages.
- [00-FEATURE-QUEUE.md](00-FEATURE-QUEUE.md): tick entry 24.

## Non-goals (for now)

- **Replace.** Find only. Replace writes to the document and would need its own
  spec for undo grouping and cross-slate behaviour.
- Case, whole-word or regex toggles. The tiers stand in for them.
- Subsequence (fzf-style) matching and relevance ranking. See
  [Why tiers](#why-tiers-and-not-fzf-style-fuzzy).
- A results list or Ctrl+Shift+F "search everywhere" panel. The badges and
  Enter cover cross-slate search.
- Match ticks on the scrollbar.
- Search history beyond the one remembered query, and persisting it.
- Searching from the tray peek ([07-BUFFER-PEEK.md](07-BUFFER-PEEK.md)) or
  without the panel open.
- Stemming, synonyms or transliteration (`strasse` ≠ `Straße`).
