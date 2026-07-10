# Verifying UI behaviour by driving the packaged app

> _Last updated: 2026-07-10_

Some UI defects only exist while the pointer is down — a drag-reorder artifact cannot be seen in a
unit test, a screenshot of a resting window, or a diff. The reliable way to check them is to deploy
the packaged app, inject real mouse input, and capture frames mid-gesture. This page records what
works, what silently does not, and the one safety rule that is not optional.

Scripts live in `Scratch/` (untracked; recreate as needed). Deployment uses the
`winui3-visual-verify` skill's `Deploy-PackagedApp.ps1`, which prints the AUMID.

---

## The safety rule: never inject without a foreground guard

`SetForegroundWindow` **silently fails** under Windows' foreground lock when the user is active at
the machine. The script carries on, and the drag it injects is replayed into whatever window
happens to be on top. This is not hypothetical: during this work a synthetic drag landed in an
unrelated dialog and drag-selected the text on its *Save* button.

Before any `SendInput`, assert **both**:

```powershell
if ([U]::GetForegroundWindow() -ne $hwnd) { throw "ABORT: QuinSlate is not foreground." }
# and that the gesture's start point really belongs to the app
$owner = 0; [void][U]::GetWindowThreadProcessId([U]::WindowFromPoint($start), [ref]$owner)
if ($owner -ne $qsPid) { throw "ABORT: start point belongs to pid $owner." }
```

Screenshot-only scripts need the same guard for a different reason: without it they capture
whatever window is on top and you "verify" the wrong pixels.

---

## `SetCursorPos` does not start a XAML drag

Moving the cursor with `SetCursorPos` and clicking with `mouse_event` **selects a tab fine but
never initiates a drag** — the press-and-move produces no drag visual at all. XAML's drag
recognizer needs genuine injected move events.

Use `SendInput` with `MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK`, with
coordinates normalized to the virtual desktop:

```
dx = (x - SM_XVIRTUALSCREEN) * 65535 / (SM_CXVIRTUALSCREEN - 1)
```

Then: move to start → button down → a few small nudges to clear the drag threshold → the sweep in
~20 ms steps → button up. Capture frames between steps.

---

## Finding the right window

The process owns several WinUI windows (main panel, tray peek, About). Matching on the class
`WinUIDesktopWin32WindowClass` alone returns whichever `EnumWindows` reaches first — which cost a
debugging round where the "tab strip" being screenshotted was another window entirely. Match on
class **and** window title (`QuinSlate`).

`Process.MainWindowHandle` is unreliable here (often zero).

---

## Capture

- Call `SetProcessDPIAware()` **first**, then `GetWindowRect`, then
  `Graphics.CopyFromScreen`. On this 4K/250 % dev monitor `PrintWindow` captures only the top-left
  fraction of a WebView2-hosting window.
- **Keep the window fully on-screen.** `CopyFromScreen` over a rect that extends past the screen
  edge returns desktop garbage, which reads convincingly as "the app is broken". A `MoveWindow` to
  a 900 DIP width at x=1990 on a 3840 px screen produced exactly that false alarm.
- Let the window settle (~1.5 s) after a resize before capturing, or you photograph a mid-resize
  frame — an unpainted right edge and stale scroll chevrons look like real defects.

---

## Prefer measuring to eyeballing

Pixel-guessing from screenshots produced several confident, wrong diagnoses in a row. Temporary
Serilog probes settle things in one run. The layout bug in
[01-TABVIEW-DRAG-REORDER.md](01-TABVIEW-DRAG-REORDER.md) §3 was only pinned down by logging
`ExtentWidth` / `ViewportWidth` / `HorizontalOffset` and each tab's `ActualWidth`:

```
PROBE at-rest hoffset=0.00 extent=572.0 viewport=561.2 scrollable=10.8
PROBE reorder-settled hoffset=10.80 extent=572.0 viewport=561.2 scrollable=10.8
```

Two lines that replaced a page of speculation. Note the probe reads values captured *before* the
pass it is logging from, so it lags one layout pass — account for that when reading a sweep.

**The "never log buffer contents" rule still applies.** Probes log names, indices, sizes, and
offsets only — never text. Strip them before committing.

---

## Related

- [01-TABVIEW-DRAG-REORDER.md](01-TABVIEW-DRAG-REORDER.md) — the findings this harness produced.
- [../Investigations/04-TRAY-PEEK-HOVER-FAILFAST-CRASH.md](../Investigations/04-TRAY-PEEK-HOVER-FAILFAST-CRASH.md)
  — for defects that kill the process instead, WER + `dbh` symbolication rather than screenshots.
