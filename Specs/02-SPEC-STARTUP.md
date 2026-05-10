# SPEC: Launch on startup

## What
Register Jott to start automatically with Windows so the tray icon is
always present without manual intervention.

## Behaviour

- On first launch, Jott registers itself to run on startup. Default is on.
- The user can toggle this via the tray context menu (see SPEC_TRAY_MENU).
- The current state of the toggle must reflect the actual registry value,
  not a cached preference — read the registry on each menu open.

## Implementation

Write to the current user run key (no admin rights required):

    HKCU\Software\Microsoft\Windows\CurrentVersion\Run
    Name  : "Jott"
    Value : "<full absolute path to Jott.exe>"

Enable:  create or overwrite the value.
Disable: delete the value if it exists.

## Edge cases

- If the executable path contains spaces it must not be quoted — the run
  key does not require quoting and some versions of Windows mishandle it.
  Test with a path that includes spaces.
- If the registry write fails (permissions, policy), log the error and
  reflect the failure state in the menu checkbox. Do not silently show the
  box as checked when the write did not succeed.
- If the user moves the executable after registering, the stale path will
  silently fail at next login. No special handling required in v1 — this
  is a user error.
