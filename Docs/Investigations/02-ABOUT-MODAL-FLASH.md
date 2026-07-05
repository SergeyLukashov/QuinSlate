# Investigation: About Modal — Main Window Flashes on Open

> _Last updated: 2026-07-05_

## Status

**FIXED** (2026-06-14, panel-frame flash; 2026-06-15, foreign-app flash). `ShowAboutDialog` prepares
the panel via `PrepareModalBackdrop`, which never re-activates or refocuses an already-visible panel.
A first fix attempt (2026-06-11) rebuilt the scrim mechanism and regressed three other behaviours
(see "First fix attempt and its regressions" below); a 2026-06-12 refinement — the
`EnsureModalBackdrop` version this doc originally described — was **lost before it was ever committed**
(`git log -S EnsureModalBackdrop` finds nothing), so the committed code had silently regressed to the
original flash. The current fix re-implements the same idea minimally as `PrepareModalBackdrop`,
retaining every dead end and regression lesson below.

A **second, distinct flash** was reported and fixed 2026-06-15: with the panel *active* before the
tray click, another application would intermittently flash *above* the panel for one frame before the
About card appeared. This was a separate root cause (a disabled owner being skipped by the tray
menu's foreground reshuffle), not a regression of the above — see "Second flash: an unrelated app
appears above the panel" below.

---

## Symptom

Opening the About modal from the tray context menu produced a visible flash on the main window — described progressively across retries as:

1. "The main window flashes for a split second before the modal animation kicks off."
2. "Looks like getting focus for a split second."
3. "A border shadow" — i.e., the window's drop shadow briefly rendered in its active/focused state before the About card appeared.

The flash was sub-100 ms and occurred reliably every time About was opened. It did not occur when the panel was already visible and the user interacted with it directly.

---

## Root Cause

The tray menu's `onAbout` handler called `ShowPanel()` before `ShowAboutDialog()` regardless of
panel state. `ShowPanel()` does two things:

1. `NativeMethods.SetForegroundWindow(windowHandle)` — re-activates the main window.
2. `Panel.FocusActiveEditor()` — drops keyboard focus into the `RichEditBox`.

At the moment `onAbout` runs, the panel is **always inactive** — the tray menu lives in its own
helper window which holds the foreground. Re-activating the panel pulses its border and drop shadow
through the foreground state, and focusing the editor flashes its caret, for one or two frames
right before the owned About window takes focus. That spike is the "border shadow" flash.

### Why this was hard to find: the DEBUG build always has the panel visible

`MainWindow.Initialise` ends with:

```csharp
#if DEBUG
    ShowPanel();
#else
    HidePanel();
#endif
```

RELEASE builds start with the panel hidden; DEBUG builds (used for development and all iterative
testing) start with the panel visible, so `isPanelVisible`-dependent paths behave differently
between the two. The flash itself reproduced in DEBUG (panel visible, re-activated needlessly).
The branch taken was identified by adding a temporary `File.AppendAllText` log at the top of
`ShowAboutDialog`: every entry showed `isPanelVisible=True`.

---

## Fix

**File:** [QuinSlate.Ui/MainWindow.xaml.cs](../../QuinSlate.Ui/MainWindow.xaml.cs)

`onAbout` calls only `ShowAboutDialog()`, which calls `PrepareModalBackdrop()` before creating the
About window. (On a re-request while About is already open, `ShowAboutDialog` re-activates the
existing window and returns without re-preparing the backdrop.) `PrepareModalBackdrop` branches on
`isPanelVisible`:

- **Panel visible:** raise it in z-order only — `SetWindowPos` with `HWND_TOP` (or `HWND_TOPMOST`
  when pinned) and `SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE`. The panel is inactive at this point
  and **stays** inactive; nothing repaints its frame, so there is nothing to flash. Raising matters
  when the panel is buried under other apps' windows: without it, the About card opens floating
  over whatever is on top while its dimmed backdrop stays buried.
- **Panel hidden:** show it via `ShowPanel(focusEditor: false)` — the same show/activate/raise as a
  normal panel reveal but **without** `Panel.FocusActiveEditor()`, because the modal takes keyboard
  focus and disables the panel anyway, so focusing the editor would only flash its caret. (The
  `focusEditor` overload is the only structural change to `ShowPanel`; its normal callers still pass
  `true`.)

The scrim choreography is the original one: create the About window, fade the black scrim to 0.45
over 130 ms (`ShowModalScrim`), activate About. On close, fade the scrim back to 0 and collapse it.

One hardening change to the scrim hide: a superseded fade's clock still runs out and raises
`Completed`, so the collapse callback only hides the scrim when **`aboutWindow == null`** — i.e.
when About was not reopened in the meantime. This ties the scrim's visibility to the real invariant
("a scrim is shown iff an About window exists") without a dedicated identity field. Without it,
closing and reopening About within ~130 ms collapses the scrim for the entire new modal session.
Storyboards are superseded only by beginning a newer one — never via `Stop()`, see below.

---

## Second flash: an unrelated app appears above the panel

### Symptom

Reported 2026-06-15, with the **panel active** before clicking the tray icon: opening About would
*intermittently* ("not every time but often enough") flash a different application — whatever was
behind the panel — *above* the panel for a single frame, just before the About card faded in. This
is a different artifact from the 2026-06-14 panel-frame pulse: here a foreign window briefly occludes
the panel, rather than the panel's own frame repainting.

### Diagnostic methodology

The `+600 ms` foreground sample (added during the first fix) only proved the *settled* state — About
always wins the foreground eventually — so it could not see a one-frame transient. A separate
external watcher was used: `Scratch/watch-foreground.ps1` tight-polls `GetForegroundWindow` every
4 ms from its own process (so it is **not** starved by QuinSlate's UI thread during the open) and
logs every foreground transition with the owning process name, window class, and rect. Window
identity is read off the rect: the tray menu helper sits offscreen at `(-12800,-12800)`, the panel is
`660×560`, the About card is `480×440`.

The log showed the open settling two ways. Clean opens went `menu helper → About card`. Flash opens
went `menu helper → WindowsTerminal → About card`, with the foreign window foreground for ~16 ms
(one frame) and the panel **never** appearing — exactly the reported flash, and intermittent because
it depends on a race (below).

### Root Cause

`AboutWindow`'s constructor disabled its owner immediately (`EnableWindow(owner, false)`, for modal
input blocking) — **before** About won the foreground. When the tray menu's topmost helper window
relinquishes the foreground as it tears down, Windows reassigns the foreground; it tries to restore
the previously-active window (the panel), finds it **disabled, skips it, and activates the next
top-level window instead** — an unrelated app, which is raised above the panel for the frame before
About's own activation lands. Whether the flash appears depends on whether the menu-teardown
reshuffle beats About's activation, hence the intermittency. (The reshuffle is **asynchronous** — it
runs after the menu's `onClose` returns, driven by the helper window's destruction — which is why no
synchronous re-ordering in `onAbout`/`onClose` can pre-empt it.)

### Fix

**Files:** [QuinSlate.Ui/MainWindow.xaml.cs](../../QuinSlate.Ui/MainWindow.xaml.cs),
[QuinSlate.Ui/Components/AboutWindow.cs](../../QuinSlate.Ui/Components/AboutWindow.cs)

Keep the owner **enabled** through the teardown reshuffle, so Windows restores the foreground to the
panel (the modal backdrop — an all-QuinSlate, on-top window) instead of diverting to another app.
Disable it for modality only *after* the reshuffle has settled:

- `AboutWindow` no longer disables the owner in its constructor; it exposes `DisableOwnerForModality()`
  (idempotent) for the launcher to call once the modal is up.
- The tray menu's `onClose` schedules `ScheduleAboutModalFinalise()` — a one-shot ~64 ms
  `DispatcherQueue` timer that lands *after* the asynchronous reshuffle, then raises About
  (`BringToForeground`) and calls `DisableOwnerForModality()`. 64 ms is comfortably past the
  near-immediate (~16 ms) reshuffle yet imperceptible behind the fading scrim.

Verified with the same watcher: across ~10 opens, no non-`QuinSlate.Ui` window ever became foreground
between the menu vanishing and the About card. Opens now read `menu helper → panel → About card` (or
`menu helper → About card` when About wins outright). The brief panel-active step is the backdrop the
card sits over and is imperceptible (confirmed by eye); the panel's `WM_ACTIVATE` handler only flips
state flags — it does **not** focus the editor — so this does not reintroduce the 2026-06-14 caret/
frame pulse.

> [!NOTE]
> Deferring the disable was the only approach that works because the reshuffle is asynchronous.
> Disabling in `onClose` (synchronously, before the reshuffle) or in `AboutWindow`'s first `Activated`
> (which can fire synchronously inside `Activate()`, before the reshuffle) both leave the owner
> disabled when the reshuffle runs — reproducing the flash. The disable must be on a deferred tick.

---

## First fix attempt and its regressions

The first fix (2026-06-11) correctly removed the re-activation, but also rebuilt the scrim
mechanism and dropped all z-order handling. Three regressions resulted:

| Regression | Cause |
|---|---|
| White background flash when opening About with the panel hidden | The fix pre-set the scrim opacity (`ShowModalScrimInstant`) before `appWindow.Show()` so the window would appear pre-dimmed. But the first on-screen frames after `Show()` are the Win32 erase fill / stale pre-hide frame — near-white in light theme — because the `AboutWindow` constructor blocks the UI thread, so the first XAML frame (with the scrim) presents only after `ShowAboutDialog` returns. Result: light undimmed window for several frames, then a hard snap to fully dimmed. The original "show, then fade the scrim in" reads as intentional animation instead. |
| About "doesn't appear right" when the main window is not active | The visible-panel path no longer touched z-order at all. The panel is always inactive when `onAbout` runs and may be buried under other windows; About then opened over the wrong content. Fixed by the `SWP_NOACTIVATE` raise. |
| About broken when opened twice in a row | The fix added `StopScrimStoryboard()` before every fade and direct `Opacity =` writes. In WinUI, `Storyboard.Stop()` reverts the property to its **local (base) value**, and the direct write had poisoned that base to 0.45. Closing About snapped the dim away instantly, and the next open either popped the scrim with no fade (animating 0.45 → 0.45) or collapsed it entirely. The original pattern — begin a new storyboard and let it take over from the current animated value, never `Stop()` — is the correct WinUI handoff. |

---

## Verified Dead Ends

Do not retry these. Each was tested and produced a visible artifact.

| Approach | Why it fails |
|---|---|
| `ShowPanel()` before/inside `ShowAboutDialog` (any ordering with the scrim) | Re-activates the window and focuses the editor — border/shadow pulse and caret flash right before About takes focus. |
| `appWindow.Show(false)` (non-activating show) for the hidden-panel path | Produced a visible frame flicker in burst captures when About then activated. (Note: the original explanation — that the owner is flipped active by `WM_NCACTIVATE(TRUE)` when its owned window activates — is wrong; see "Corrected Win32 facts". The flicker observation itself stands.) |
| `DwmSetWindowAttribute(DWMWA_TRANSITIONS_FORCEDISABLED)` | Suppresses DWM open/show animation. The 8-frame (~120 ms) fade visible in burst captures was not a DWM animation — it was the `ModalScrim` 130 ms `FadeScrim` storyboard. No effect on the artifact. |
| Pre-dimming the hidden panel (`ShowModalScrimInstant` before `Show()`) | White flash; see regression table. The first presented frames cannot contain the scrim because the UI thread is blocked by the `AboutWindow` constructor until after `ShowAboutDialog` returns. |
| `Storyboard.Stop()` before starting a replacement fade | Reverts `Opacity` to its local base value (instant visual snap); combined with any direct `Opacity =` write, permanently poisons that base. Begin the new storyboard without stopping the old one. |

---

## Corrected Win32 facts (probed 2026-06-12)

Verified empirically with a Win32 probe (`Scratch/NcActivateProbe.cs`, mirrors the app's ownership
setup: `GWLP_HWNDPARENT`, `EnableWindow(owner, false)`, borderless toolwindow):

- **Activating an owned window does not repaint its owner's frame.** The owner does *not* receive
  `WM_NCACTIVATE(TRUE)`; if the owner was inactive it simply stays inactive-looking. (The
  "both look active" behaviour seen in some apps is hand-rolled, e.g. MFC `MFS_SYNCACTIVE`.) This
  is why the `SWP_NOACTIVATE` raise is flash-free: zero frame transitions on the panel.
- `SetWindowPos` with `HWND_TOP | SWP_NOACTIVATE` raises z-order without activation, focus change,
  or any `WM_NCACTIVATE`.
- The owner does receive `WM_NCACTIVATE(TRUE)` when About closes and `SetForegroundWindow(owner)`
  runs from `AboutWindow.OnClosed` — the close path is fine.

---

## Diagnostic Methodology

The flash was sub-frame and not reproducible by eye alone, so two diagnostic tools were used.

### Burst frame capture

`Scratch/capture-about-flash.ps1` — launches the resident instance via AUMID, waits for warmup, then starts grabbing ~1810 × 820 px bitmaps via `Graphics.CopyFromScreen` at ~26 fps. At 150 ms it fires a second AUMID launch, which posts `WM_QUINSLATE_ACTIVATE` to the resident instance (opening About without needing to click the tray). Frames are saved as numbered PNGs with ms timestamps for frame-by-frame inspection.

Key discovery from the capture: the main window sits at roughly (2030, 480) on a 3840 × 2400 display — well off-centre. `Cap.FindBiggestWinUI()` (an inline `EnumWindows` call in the script) was needed to locate it dynamically, since hard-coding the capture region at screen centre captured desktop background.

Pixel sampling at specific points (interior, shadow zone, title bar) was used to distinguish window states. The desktop background, the scrim-dimmed interior, and the focus-active border all have distinguishable colour signatures.

### Foreground-window polling

After applying the fix, `GetForegroundWindow` was polled 8 times at 250 ms intervals following the AUMID trigger. The About window (480 × 440 DIP at its screen rect) was foreground on every poll, confirming that the About window reliably activates without `ShowPanel()` priming it first.

---

## Known remaining gaps (out of scope)

- `EnableWindow(owner, false)` does not block `WM_HOTKEY` or the tray callback: the global hotkey
  can hide/re-show/refocus the panel underneath an open About modal. The About re-request path
  re-shows a hidden backdrop, but the hotkey path itself is unguarded.
- If `aboutWindow.Activate()` is ever denied foreground rights, the failure is silent (About is a
  `WS_EX_TOOLWINDOW`, so there is no taskbar button to flash).

---

## Key Files

| File | Role |
|---|---|
| [QuinSlate.Ui/MainWindow.xaml.cs](../../QuinSlate.Ui/MainWindow.xaml.cs) | `ShowAboutDialog`, `PrepareModalBackdrop`, `ScheduleAboutModalFinalise`, `ShowModalScrim`, `HideModalScrim`, `FadeScrim`, `ShowPanel(bool)` |
| [QuinSlate.Ui/Components/AboutWindow.cs](../../QuinSlate.Ui/Components/AboutWindow.cs) | Owned About window; `DisableOwnerForModality()` disables the owner (`EnableWindow(ownerHwnd, false)`) — called by the launcher *after* the menu teardown reshuffle, not from the constructor; `BringToForeground()` re-asserts foreground past the foreground lock; fade-in via `WS_EX_LAYERED` / `SetLayeredWindowAttributes` |
| [QuinSlate.Ui/Interop/NativeMethods.cs](../../QuinSlate.Ui/Interop/NativeMethods.cs) | `HWND_TOP`, `SWP_NOACTIVATE` for the no-activate raise; `GetForegroundWindow`, `GetWindowThreadProcessId`, `AttachThreadInput`, `GetCurrentThreadId` for `BringToForeground` |
| `Scratch/capture-about-flash.ps1` | Burst capture harness (git-ignored) |
| `Scratch/watch-foreground.ps1` | External 4 ms foreground-transition watcher; logs each foreground hwnd + process name to catch the one-frame foreign-app flash (git-ignored) |
| `Scratch/NcActivateProbe.cs` | Win32 probe proving owner frame is untouched by owned-window activation (git-ignored) |
