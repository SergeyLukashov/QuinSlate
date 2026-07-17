# Tray peek hover fail-fast crash (0xc000027b in Microsoft.UI.Xaml.dll)

> _Last updated: 2026-07-17_

## Symptom

On an HP ProBook 455 G7 (Ryzen with Vega iGPU, 1920×1080 @ 125 %, Windows 11 26200,
fresh 0.9.5 MSIX install), hovering the tray icon intermittently killed the process
instantly and silently. Serilog showed nothing: sessions simply end without
"Shutdown complete". Reproduced several times in a row on that machine; never seen
on the (faster) dev machine.

## Evidence

Windows Error Reporting / Application event log (captured from the affected laptop):

- Event `MoAppCrash`, faulting module **`Microsoft.UI.Xaml.dll` 3.2.2.0**
  (Windows App Runtime 2.2), exception code **`0xc000027b`** — a *stowed exception*
  fail-fast — with stowed HRESULT **`0x8000FFFF` (E_UNEXPECTED)**.
- Fault offset `0x3ad79d` symbolizes (dbh.exe + the public Microsoft symbol server,
  PDB `Microsoft.ui.xaml.pdb` `8A067FD7…`) to **`FailFastWithStowedExceptions+0x61`**:
  the XAML framework hit an internal failure it could not propagate and deliberately
  terminated the process. This never reaches the managed
  `Application.UnhandledException` / `AppDomain.UnhandledException` hooks, which is
  why the logs are clean.
- Crash timestamps line up with tray hovers, including one ~10 s after launch —
  consistent with the **first peek show of a session**.

## Analysis

The only XAML work on the tray-hover path is `TrayPeekWindow` (`Docs/Specs/07-BUFFER-PEEK.md`).
Two properties of the show path put the XAML renderer at risk, both amplified on a
slow integrated GPU where the island's first composition takes much longer:

1. **`WS_EX_LAYERED` was toggled around every show.** The entrance fade
   (`Docs/Specs/15-PEEK-SHOW-ANIMATION.md`) added the layered style before
   `ShowWindow`, ramped alpha 0→255 over ~90 ms via `SetLayeredWindowAttributes`,
   then **stripped the style bit at the end of the fade** — on the slow machine that
   lands mid-first-composition. Changing a window's layered status invalidates how
   DWM/DirectComposition target the window while the XAML render thread is
   presenting into it; the renderer surfaces this class of failure as `E_UNEXPECTED`
   and fail-fasts with a stowed exception.
   (The style removal was a leftover from the Mica era; with the opaque in-tree
   dithered-gradient backdrop there is no reason to remove it.)
2. **The island's first-ever bring-up ran on the hover path.** The peek `Window` was
   created lazily on first hover, then immediately shown non-activated with the fade
   racing content load, layout, brush construction, and swap-chain creation.

## Fix (0.9.6)

- `WS_EX_LAYERED` is now set **once at window creation and never removed**
  (`TrayPeekWindow.ApplyNonActivatingStyles`); alpha rests at 0. The fade itself is
  unchanged (`SetLayeredWindowAttributes` ramp), so the visuals are identical — only
  the racy style *transition* is gone. Note a layered window is not displayed until
  `SetLayeredWindowAttributes` is called, hence the alpha initialisation at creation.
- `TrayPeekWindow.WarmUp()` runs the island's first composition **at startup**: the
  window is created, shown non-activated and fully transparent (alpha 0 — nothing is
  painted), and hidden again when its content loads. `MainWindow.Initialise`
  enqueues this at low dispatcher priority when tray peek is enabled, so the panel's
  first paint is not delayed. A hover arriving mid-warm-up cancels the pending
  hide-on-loaded and takes over the already-created window. Side benefit: the first
  real peek no longer pays the cold-start cost, so it appears as fast as later ones.

## Confirming the root cause (if it recurs)

The WER report queue on the affected machine holds a triage dump whose stowed
exception stack names the exact failing frame:

    C:\ProgramData\Microsoft\Windows\WER\ReportQueue\AppCrash_19589Sergejdev.Q_*

Copy the whole report folder off the machine and open the `.dmp` in WinDbg with
`!analyze -v` (or `dx` on the stowed exception parameters); symbols come from
`https://msdl.microsoft.com/download/symbols`. If a new dump still shows the crash
with 0.9.6, the stowed stack will say whether the remaining trigger is elsewhere
(e.g. `SW_HIDE` during first present).

## Recurrence (2026-07-10, v0.9.6, at startup — warm-up hide is the leading cause)

A second laptop ("MyLaptopUltra3", 2880×1800 @ 200 %, Windows 11 26200) hit the
same signature on **0.9.6 — the build carrying the hover fix**: Event 1000
`0xc000027b` in `Microsoft.UI.Xaml.dll` 3.2.2.0, fault offset `0x3ad79d`
(`FailFastWithStowedExceptions+0x61` — the generic termination site, so the
offset alone does not identify the failing code path).

Established facts:

- **It is a startup crash, not a hover crash.** The WER `ProcessCreationTime`
  FILETIME (`0x1DD10354DECBA03`) decodes to 08:28:21.765 +02:00; the crash event
  was logged 08:28:23.036 — the process lived **at most ~1.3 s** (less, by
  however long WER dump collection took).
- **The crashed session wrote zero log lines.** The day's log file starts (BOM
  first) with the *next* launch, 33 s later, which ran normally. The file sink is
  async-wrapped, so this means either the crash pre-dates the startup banner
  (early XAML/Application bring-up) or the async worker had not yet flushed
  (plausible under a login/boot storm with cold Defender scans).

Hypotheses, in current order of likelihood:

1. **Peek warm-up `SW_HIDE` racing the island's first present** — the residual
   risk named above. `OnWarmUpPanelLoaded` hides the window the instant
   `panel.Loaded` fires, but `Loaded` (layout done) does not mean the render
   thread's first present has completed. The warm-up is the only startup-path
   XAML work that is *new* in 0.9.6, and the timing (~1 s in, enqueued at low
   priority after `Activate`) fits the process lifetime. **This race is still
   present in HEAD (v0.9.9)** — the 0.9.7 reworks changed sizing and foreground
   handling, not the hide-on-`Loaded` timing.
2. **Login-launch `startHidden` sequence** (`Cloak → Activate → HidePanel →
   Uncloak`, in the tree since 0.9.5/2026-06-23): a hide immediately after the
   main island's first `Activate`. Same operation class; but 0.9.5 never showed
   startup crashes, so ranked second.
3. **Early `Application`/`Window` bring-up failure before logging initialised**
   (would explain the empty log perfectly). Known platform class — see
   microsoft-ui-xaml issue #8446 (stowed fail-fast on some systems just from
   instantiating `Microsoft.UI.Xaml.Window`).

`Report.wer` retrieved from the laptop's `ReportArchive` on 2026-07-17 (no `.dmp`
remained locally — `ReportStatus` indicates the cab was uploaded and purged;
Microsoft bucket `ee29950da177439749cd20e4e4183a97`). It resolves the open
questions:

- **Stowed HRESULT is `0x8000FFFF` (`E_UNEXPECTED`)** — `Sig[7]` — the same
  renderer-class failure as the 0.9.5 layered-style crash, ruling out the
  module-load variant (`0x8007007E`, hypothesis 3) and the platform
  `Window`-instantiation issue.
- **The loaded-module list places the crash deep in startup**, not in early
  bring-up: `Serilog.dll` + `Serilog.Sinks.Async` + `Serilog.Sinks.File`
  (logging initialised — the banner was emitted and lost in the async queue),
  `System.Text.Json` (settings load), `Microsoft.UI.Windowing` /
  `Microsoft.UI.Input` / `textinputframework` / `DataExchange` (main window
  fully up), `windowscodecs` + `PhotoMetadataHandler` (image decode — tray
  icon / emoji-atlas territory, ~1 s in). This is exactly the window in which
  `TrayPeekWindow.WarmUp()` runs.
- **The crash was the first-ever run of 0.9.6 on that machine** — the previous
  day's log shows two clean 0.9.5 sessions ("Shutdown complete") on the same
  hardware, and no log exists for 2026-07-09. First run of 0.9.6 = first run of
  the warm-up code.
- Curiosity, not conclusive: `D3D10Warp.dll` (software rasteriser) is loaded
  alongside the Intel hardware driver — consistent with the compositor doing
  device work under login-time contention.

Conclusion: the leading cause, by a wide margin, is the warm-up's `SW_HIDE` on
`panel.Loaded` racing the peek island's first present — the residual risk this
document predicted. Without a dump this is not frame-level proof, but every
discriminator (stowed HRESULT class, startup phase, version delta, machine
history) points at it.

### Repro attempts on the dev machine (2026-07-17, negative)

The race could not be reproduced on a fast dev machine (24 threads, healthy
dGPU) despite: 25 launches under all-core CPU spinners; 15 launches with the
process hard-capped to ~1 core via a job object from "Tray icon added" onward;
and a timing fuzzer sweeping an explicit `SW_HIDE` across 0–200 ms from
`ShowWindow` (oversized 5760×3240 warm-up window, 24 launches). A healthy
compositor tolerates hide-at-any-point during first composition; the laptop
crash evidently also needed the degraded graphics path (`D3D10Warp.dll` in its
module list). Harnesses live in `Scratch/Repro-WarmUpFailFast*.ps1` +
`Scratch/Deploy-DevLayout.ps1` (dev-identity loose layout) if this needs to be
re-run — e.g. on the affected laptop itself.

### Fix (2026-07-17, unreleased)

Warm-up lifecycle extracted to `TrayPeekWarmUp` (`QuinSlate.Ui/Tray/`). The hide
no longer happens on `Loaded`; instead the warm-up waits for the first
`Microsoft.UI.Xaml.Media.CompositionTarget.Rendered` tick after `Loaded` (the
frame containing the panel has been rendered and submitted), then for
`Compositor.RequestCommitAsync()` to complete (the compositor has confirmed a
commit cycle — the first present is no longer in flight), and only then calls
`SW_HIDE`, from a dispatcher continuation rather than inside the frame-event
dispatch. Waiting for *multiple* `Rendered` ticks was rejected on WinUI 3 Expert
review: `Rendered` only reports frames and does not force them, so a second tick
can stall forever on a render-idle thread. Verified on the dev machine: warm-up
completes ~100 ms after show, hover peek unaffected.
