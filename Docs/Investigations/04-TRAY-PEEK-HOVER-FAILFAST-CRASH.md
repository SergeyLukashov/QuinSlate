# Tray peek hover fail-fast crash (0xc000027b in Microsoft.UI.Xaml.dll)

> _Last updated: 2026-07-07_

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
