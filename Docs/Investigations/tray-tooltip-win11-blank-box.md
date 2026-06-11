# Investigation: Empty Tray Tooltip Box — Windows 11 Explorer Bug

## Status

**FIXED** (2026-06-11). Dynamic tooltip arming in `Tray/TrayIcon.cs`.

---

## Symptom

An empty tooltip box appeared above the QuinSlate tray icon after:

1. Opening a Windows 11 system flyout — Quick Settings (wifi/volume/battery widget),
   notification centre, or calendar.
2. Dismissing that flyout (by any means — Esc, clicking the taskbar, clicking away).
3. Hovering the tray icon.

The box appeared with the normal ~700 ms tooltip delay, as if it were a real tooltip with
no text. It rendered as a blank floating rectangle above the icon / peek preview footer.

A harder variant existed: the box appeared only after the specific sequence
**hover icon (peek shows) → click wifi/volume/battery widget → click the taskbar to
dismiss → re-hover**. This variant was not reproducible in an isolated lab tray icon;
it required the real app with its WinUI 3 peek window present.

---

## Root Cause

Explorer's XAML tooltip popup (`Xaml_WindowedPopupClass`, rendered as a DComp visual)
glitches after a system flyout is dismissed. It shows a tooltip for the icon regardless
of whether `NIF_SHOWTIP` is set, or even whether the tooltip text is empty. The popup is
a DComp surface — HWND z-order manipulation does nothing to it.

The bug is **inside explorer**. No API exists to suppress it from the outside.
The only lever is the tooltip registration state at the moment explorer decides to show it.

---

## Fix

**File:** [Tray/TrayIcon.cs](../../QuinSlate.Ui/Tray/TrayIcon.cs)

**Strategy: dynamic tooltip arming.**

- Steady state: icon registered with `NIF_TIP | NIF_SHOWTIP` and text `"QuinSlate"`.
- On the first `WM_MOUSEMOVE` tray callback (hover begin): send `NIM_MODIFY` with only
  `NIF_TIP` (no `NIF_SHOWTIP`) — this withdraws the tooltip.
- While the pointer stays over the icon: **re-send that same withdrawal on every 150 ms
  poll tick.** This is essential — a single withdrawal at hover begin is enough for the
  plain and esc-dismissed-flyout cases, but NOT for the hard variant (taskbar-dismiss after
  prior hover). Explorer resists the single withdrawal in that state; repeated NIM_MODIFY
  calls keep cancelling the queued display.
- After the pointer leaves: wait ~1.5 s (10 × 150 ms ticks) before re-arming with
  `NIF_TIP | NIF_SHOWTIP`. Re-arming immediately after hover end makes explorer pop the
  now-available "QuinSlate" tooltip for the just-left icon.

Two knock-on discoveries:

- Explorer does **not** send `NIN_POPUPOPEN` while `NIF_SHOWTIP` is armed, so the peek
  trigger had to move from `NIN_POPUPOPEN`/`NIN_POPUPCLOSE` to `WM_MOUSEMOVE` (hover
  begin) + cursor-position polling (hover end).
- Re-arming too soon (even 1 tick = 150 ms) after hover end causes a stray "QuinSlate"
  tooltip on the just-left icon. Hence the 10-tick quiet period.

---

## Verified Dead Ends

Do not retry these. Each was tested and failed.

| Approach | Why it fails |
|---|---|
| Static `szTip = ""` (empty tooltip text) | Explorer shows an empty box — the glitch fires regardless of text content. |
| Omitting `NIF_TIP` entirely on `NIM_ADD` | Explorer still shows the box. `NIF_SHOWTIP` without `NIF_TIP` or with no text makes no difference. |
| `NIM_DELETE` + `NIM_ADD` re-add on hover | Breaks notification area promotion (`IsPromoted`): re-add resets the promoted state and the icon disappears to the overflow tray. |
| `ShowWindow(SW_HIDE)` on the popup HWND | The popup is a DComp visual, not a classic HWND; `ShowWindow` does nothing to it. |
| `SetWindowPos` z-order tricks on the popup HWND | Same reason — it is a DComp surface, not a window in the traditional sense. |
| Direct2D high-precision gradient stops (separate issue) | Was too weak for the dithering problem; unrelated to the tooltip. |
| `NIN_POPUPOPEN` / `NIN_POPUPCLOSE` as hover triggers | Explorer does not send `NIN_POPUPOPEN` while `NIF_SHOWTIP` is armed — peek never appeared. |
| Single `NIM_MODIFY` disarm at hover begin only | Fixes the plain and esc-dismissed cases; fails the hard variant (taskbar-click dismiss after prior hover). |

---

## Residual Edge Case

If the user: hovers the icon → leaves → opens + dismisses a flyout → re-hovers, all
within the ~1.5 s re-arm quiet window — the icon is still disarmed from the first hover
and explorer could theoretically show an empty box on the re-hover. Accepted as negligible:
the window is narrow and the sequence is contrived.

---

## Lab Harnesses

All scripts live in `Scratch/` (git-ignored).

| Script | Purpose |
|---|---|
| `tray-lab.ps1` | Standalone Add-Type tray icon with `armed`/`suppressed`/`fix` modes; SendInput hover, strip screenshot + pixel diff. Use this to reproduce in isolation. |
| `verify-quinslate-tray.ps1` | Drives the real app's icon; probes `Shell_NotifyIconGetRect` across all process HWNDs (the peek window is a second `WinUIDesktop` window — the icon is on the main window HWND). |
| `scenario-hoverfirst.ps1` | Full realistic flow: hover → widget click → taskbar-click dismiss → re-hover; 2 runs with strip snapshots. This was the script that reproduced the hard variant 2/2. |
| `probe-rehover.ps1` | hover → leave (brief) → re-hover; verifies peek shows on second hover and no stray "QuinSlate" tooltip leaks. |
| `crop-icon-zone.ps1` | Crops bottom-right 300×170 px from each strip frame for close-up inspection. |

**Note:** `SetCursorPos` teleports do NOT register with the XAML taskbar. Use `SendInput`
with a gradual approach path (`Glide` function in the scripts).

**Note:** To promote a test icon to the visible taskbar without `NIM_DELETE`/`NIM_ADD`
re-add: set `HKCU:\Control Panel\NotifyIconSettings\<key>\IsPromoted = 1` on the live icon.
`NIM_DELETE` + `NIM_ADD` resets promotion; don't do it.

---

## Key Win32 APIs

- `Shell_NotifyIcon` (`NIM_ADD`, `NIM_MODIFY`, `NIM_SETVERSION`, `NIM_DELETE`)
- `NIF_TIP`, `NIF_SHOWTIP`, `NIF_MESSAGE`, `NIF_ICON`, `NOTIFYICON_VERSION_4`
- `Shell_NotifyIconGetRect` — required to locate the icon rect for cursor hit-testing
- `WM_MOUSEMOVE` (0x0200) in the tray callback — used as hover-begin signal
- `GetCursorPos` + icon rect polling — used as hover-end detection (150 ms interval)
