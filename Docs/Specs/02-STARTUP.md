# SPEC: Launch on startup

> _Last updated: 2026-07-05_

## What
Register QuinSlate to start automatically with Windows so the tray icon is
always present without manual intervention.

## Behaviour

- On first launch, QuinSlate registers itself to run on startup. Default is on.
- The user can toggle this via the tray context menu (see SPEC_TRAY_MENU).
- The current state of the toggle must reflect the actual startup-task state,
  not a cached preference — read the live state on each menu open.

## Implementation

QuinSlate ships as an MSIX-packaged desktop app, so the legacy
`HKCU\...\Run` registry approach **cannot** be used:

- Package registry virtualization redirects an `HKCU\...\Run` write into a
  per-package private hive that Windows never reads at login, so the app
  never actually starts — yet reading the value back through the same
  virtualized view makes the menu checkbox look enabled. (This was the
  original "Launch on startup does nothing" bug.)
- Even written to the real key, the bare packaged executable cannot be
  activated without package identity (`REGDB_E_CLASSNOTREG`).

Instead, use the packaged **startup task** mechanism:

1. Declare the task in `Package.appxmanifest` under the application's
   `<Extensions>` (namespace
   `http://schemas.microsoft.com/appx/manifest/desktop/windows10`):

       <desktop:Extension Category="windows.startupTask"
                          Executable="QuinSlate.Ui.exe"
                          EntryPoint="Windows.FullTrustApplication">
         <desktop:StartupTask TaskId="QuinSlateStartupTask"
                              Enabled="false"
                              DisplayName="QuinSlate" />
       </desktop:Extension>

   `Enabled="false"` keeps redeploys/updates from forcibly re-enabling the
   task; the "default on" behaviour is driven once in code on first launch.

2. At runtime use `Windows.ApplicationModel.StartupTask`:
   - State:   `StartupTask.GetAsync("QuinSlateStartupTask")` → `.State`.
   - Enable:  `await task.RequestEnableAsync()` (no consent dialog for a
              packaged *desktop* app).
   - Disable: `task.Disable()`.

## Edge cases

- If the user disables the task via Task Manager or the Settings Startup
  page (`StartupTaskState.DisabledByUser`), `RequestEnableAsync` does **not**
  override their choice — it returns the still-disabled state. The menu
  checkbox reflects the actual resulting state because it re-reads on each
  open. Log when an enable request does not take effect.
- When the app runs without package identity (e.g. an unpackaged dev run),
  `StartupTask.GetAsync` is unavailable; the service treats this as
  "disabled" and the toggle is a no-op rather than throwing.
