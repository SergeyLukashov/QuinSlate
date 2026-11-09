# Editor click/caret line offset

> _Last updated: 2026-09-24_

**Status: open — instrumentation added, awaiting a log capture from the affected machine.**

## Symptom

On one machine (a laptop, 1920x1080 at 125% scaling), clicking in the editor puts the caret
roughly **one line above the click**. Not reproducible on the development machine (4K at 250%).
Reported from memory; the exact conditions that bring it on — a resize, a maximize, a session that
has been running a while — are not known.

## What the captured logs establish

Eight sessions from that machine, 2026-08-25 through 2026-09-07, all v1.0.1. No warnings, no
errors, no `WebView2 process failed`, `Editor page ready.` every time. Nothing on the pointer or
caret path was logged at all, so the logs cannot contain the bug — but they do pin the environment:

- Windows 11 10.0.26200, x64, a single 1920x1080 monitor, **system DPI 120 (125%)**.
- **`XamlRoot.RasterizationScale` is exactly 1.25.** Not logged directly: it is derived from
  `Emoji sprite atlas loaded ... at 40 px (scale 1.25)`, and `EmojiAtlasFormat.SpriteLogicalSize`
  is 32 (32 x 1.25 = 40).

That second point rules out the Windows **text scale factor**: `RasterizationScale` is the monitor
DPI scale multiplied by it, and here it equals the raw DPI scale, so text scaling is 1.0 and cannot
be the source of a host/page scale disagreement.

## What the code rules out

- **Nothing in the page is scale-dependent.** `editor.css` and `editorTheme.js` are pure CSS px
  (15px at line-height 1.6 is a 24px line box, exact at 1.25x and at 2.5x). The only consumer of
  `devicePixelRatio` is the background PNG in `appearance.js`. A page-side cause would therefore
  reproduce everywhere, which is the opposite of what was reported.
- **Line heights were uniform in 1.0.1.** The variable-height markdown headings
  (`.cm-md-h1`, `line-height: 1.3`) landed in `a67afab`, after these logs. With every line the same
  height, CM6's height map cannot accumulate drift, so a CM6-side fault would need a *constant*
  error in the content rectangle rather than a growing one.
- **There is no XAML transform on the editor.** `BufferPanel.xaml` still says "EditorHostContent
  carries the entrance-animation transform", but that is stale: the entrance moved into Chromium
  (`WebEditor/build/src/entrance.js`) and no `RenderTransform` is set on `EditorHostContent`. The
  classic WebView2 trap — a render transform that moves the rendering but not the hit-testing —
  is not in play.

## Candidate causes

The editor is a top-level overlay whose position is set by **mutating a margin after the WebView2
exists**: `EditorOverlayPositioner.Position()` (`Components/EditorOverlayPositioner.cs`) assigns `EditorHostLayer.Margin`
from the TabView content presenter's origin, and runs on TabView `Loaded`/`SizeChanged`, panel
`Loaded`, and RootGrid `SizeChanged` — so on every window resize, maximize and restore.

1. **Constant offset — the WebView2's input path and its rendering disagree about where the
   control is.** WinUI 3's `WebView2` has a history of getting this math wrong when the control is
   not at the window origin ([microsoft-ui-xaml#3695](https://github.com/microsoft/microsoft-ui-xaml/issues/3695),
   where the offset is folded into the viewport size). The error would be the same everywhere in
   the buffer.
2. **Proportional offset — an undeclared zoom factor.**
   [WebView2Feedback#3839](https://github.com/MicrosoftEdge/WebView2Feedback/issues/3839): on about
   1% of systems WebView2 applies an extra 8-15% zoom that the browser zoom factor still reports as
   1.0, so host and page pixel coordinates diverge. At 125% a 1.25-vs-1.0 disagreement gives an
   error of `0.2 * y`: one 24px line at y = 120px (about line 5, where most clicks land) and two
   lines by line 10.
3. **CM6 resolving the press to the wrong line** — only reachable through a stale content
   rectangle, given the uniform line heights of 1.0.1.

## The probe

Content-free instrumentation on both sides of the bridge, added 2026-09-07:

- `Components/EditorPointerProbe.cs` — a `PointerPressed` handler on the WebView2 element
  (`handledEventsToo`, since the control marks the press handled as it forwards it), logging the
  press in element-relative DIPs.
- `WebEditor/build/src/clickProbe.js` — a bubble-phase `mousedown` on `scrollDOM` logging
  `clientX`/`clientY` and `caretTopDelta`: the distance from the click to the top of the **drawn**
  caret (`.cm-cursor-primary`), read one frame later so the cursor layer's own measure cycle has
  run.

The caret rect is measured rather than re-derived with `posAtCoords`, deliberately. It is what the
report is actually about — where the caret ended up, not where the coordinate maths says it should
be — and `posAtCoords` forces a synchronous re-measure, which would hand CM6 a fresh measurement
microseconds before it resolves the click: a probe that repairs the stale measurement it is hunting
for. It also sidesteps `coordsAtPos`'s `side` argument, which returns the *next* visual row for a
position sitting exactly on a wrap boundary and would report a full positive line height for a
press that was mapped perfectly.

Both sides also log a geometry line: the host at `Loaded` (element size, rasterization scale), the
page 500 ms after the reveal entrance (`devicePixelRatio`, viewport, `defaultLineHeight`, content
top). The host line is unconditional at load on purpose — it is what proves the probe is attached,
so a capture in which no press is ever logged can be told apart from one in which the WebView2
swallowed the pointer input. The page line is deliberately scheduled past the caret hand-off so its
forced layout lands in neither the entrance's frames nor the blink release's.

**Resize is instrumented**, because it is the event candidate 1 turns on: both sides re-log geometry
400 ms after a resize settles, and both reset their press budget, so the forty entries are available
again for the clicks *after* the maximize rather than being spent on the first forty of the session.

Every entry carries a monotonic press index. That pairs a host line with its page partner
unambiguously — adjacency is not enough, because a press on the reserved scrollbar gutter reaches
the host handler and the page's, while a press on the native scrollbar reaches neither (Chromium
dispatches nothing to the page for it). The index also keeps `pageLog.js`'s flood guard, which
counts occurrences of each *distinct* message, from silencing the page side after ten identical
presses on the same spot — which is exactly how this bug gets reproduced.

Both sides log coordinates, sizes and pixel deltas only; no document text crosses either way. The
two counters have separate lifetimes (the host's spans the app, the page's a page load), so after a
WebView2 renderer failure and reload the indices no longer line up — the geometry lines mark where
that happened.

## Reading a capture

Pair each `Click probe (host) #n` with the `Click probe (page)` line carrying the same index.

| Observation | Meaning |
|---|---|
| host `y` == page `y`, `caretTopDelta` within `-24..0` | The press was mapped correctly and the caret landed on the clicked line — the offset was not reproduced in this capture. |
| host `y` == page `y`, `caretTopDelta` past `-24` | The press was mapped correctly and the editor still put the caret a line high — cause 3. |
| host `y` != page `y`, difference constant across the buffer | Cause 1: fix by not repositioning the WebView2 through margin mutation (give `EditorHostLayer` a fixed grid placement, or force a bounds refresh after each `EditorOverlayPositioner.Position()`). |
| host `y` != page `y`, difference growing with `y` | Cause 2: compare the page's `dpr` with the host's rasterization scale in the two geometry lines; if they differ, the only remedy is measuring the ratio at startup and correcting for it. |
| host line with no page partner | A press on the native scrollbar, or on the gutter — ignore, and re-pair from the next matching index. |

The geometry lines are worth reading on their own: `dpr` should equal the rasterization scale (1.25
on that machine) and `lineHeight` should be 24.0. If the offset only appears after a resize, the
geometry pair logged 400 ms after that resize is the one to compare against the pair from startup.

## Removing the probe

It is diagnostic, not product behaviour. Once the capture has answered the question, delete
`EditorPointerProbe.cs`, `clickProbe.js`, `test/clickProbe.test.mjs`, and their three call sites
(`BufferPanel.xaml.cs`, `editorSetup.js`, `startupReveal.js`), and rebuild the bundle.
