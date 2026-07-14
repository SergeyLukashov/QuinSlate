# Tray icon moves to overflow after every Store update

> _Last updated: 2026-07-14_

## Symptom

Every time QuinSlate is updated through the Microsoft Store, its notification-area
(tray) icon jumps back under the "hidden icons" chevron. The user has to drag it
back onto the taskbar after each update. A fresh position choice survives normal
reboots and app restarts — but never a version bump.

## Root cause

The icon was identified only by the `(HWND, uID)` pair — `uID = 1`, no
`guidItem` — in [`TrayIcon.cs`](../../QuinSlate.Ui/Tray/TrayIcon.cs).

Windows 11 stores each notification icon's **promotion state** (shown on the
taskbar vs. hidden in the overflow flyout) under
`HKCU\Control Panel\NotifyIconSettings`, keyed by the icon's identity. When no
GUID is supplied, that identity is derived from the **executable path** (plus the
uID).

QuinSlate is MSIX/Store-packaged, so each version installs into a *new* versioned
folder:

```
…\WindowsApps\QuinSlate_0.9.7.0_x64__<pubid>\QuinSlate.exe
…\WindowsApps\QuinSlate_0.9.8.0_x64__<pubid>\QuinSlate.exe   ← after update
```

The path changes on every update, so the shell no longer matches the icon to the
existing `NotifyIconSettings` entry, treats it as a brand-new icon, and applies
the default — which is "hidden in the overflow." Hence the icon demotes on every
update but is stable across reboots (path unchanged) and Explorer restarts.

## Fix

Identify the icon by a fixed `guidItem` with the `NIF_GUID` flag instead of by
path. Per the Win32 docs, a registered GUID "overrides `uID` and is the
recommended method of identifying the icon." Because the GUID is path
independent, the shell matches the post-update icon to the same
`NotifyIconSettings` entry and preserves the user's chosen position.

Changes:

- `NativeMethods.NIF_GUID` (`0x00000020`) added.
- `TrayIcon.TrayIconGuid` — a fixed, ship-once `Guid` set on `guidItem` in
  `BuildData`, with `NIF_GUID` OR-ed into `uFlags` on every `Shell_NotifyIcon`
  call (`NIM_ADD`, `NIM_MODIFY` for balloon/tooltip arming, `NIM_DELETE`,
  `NIM_SETVERSION`) and on the `NOTIFYICONIDENTIFIER` used by
  `Shell_NotifyIconGetRect` (once `NIF_GUID` is in play the uID is overridden, so
  the rect lookup must match by GUID too).
- **Both** `Shell_NotifyIconGetRect` call sites had to switch to the GUID:
  `TrayIcon.IsCursorOverIcon` (hover-leave detection) **and**
  `TrayPeekWindow.TryQueryIconRect` (peek positioning). A GUID-registered icon is
  no longer resolvable by `(HWND, uID)`, so any lookup still passing
  `guidItem = Guid.Empty` fails — this initially broke the peek (it couldn't find
  the icon's rect to position against, so it never showed). `TrayIcon.TrayIconGuid`
  is the single public source of truth both call sites read.

### Stale-GUID guard

A GUID-identified `NIM_ADD` **fails** if that GUID is still registered elsewhere —
to the previous package version's path immediately after an update, or leaked by
a prior instance that never removed its icon. `RegisterIcon` therefore treats a
failed add as recoverable: it issues a `NIM_DELETE` by GUID (`DeleteByGuid`) to
clear the stale registration, then retries the add once. Without this guard, the
position fix could trade itself for a worse bug — no icon at all after an update.

## Caveats

- **One last move for existing users.** Installs that predate this change already
  have a path-keyed `NotifyIconSettings` entry. Windows cannot retroactively link
  it to the new GUID, so the *first* update after this ships still demotes the
  icon once; every update after that is stable. New installs are stable from the
  start.
- `TrayIconGuid` must **never change** once shipped — changing it is equivalent to
  starting over with a fresh identity and would demote the icon again.
- Orthogonal to the Win11 blank-tooltip fix
  ([01-TRAY-TOOLTIP-WIN11-BLANK-BOX.md](01-TRAY-TOOLTIP-WIN11-BLANK-BOX.md)); the
  hover/tooltip state machine is untouched.

## Verification

- `dotnet build` clean; `dotnet test` 183/183 pass.
- Manual: icon appears, hover/click/peek/tooltip all still work.
- A true Store-update version bump cannot be simulated locally; the mechanism is
  confirmed against the documented `NotifyIconSettings` keying (an `IconGuid`
  value now accompanies the icon's entry).
