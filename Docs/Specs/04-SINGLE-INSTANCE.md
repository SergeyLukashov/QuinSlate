# SPEC: Single-instance enforcement

> _Last updated: 2026-07-05_

## What
Ensure only one running copy of QuinSlate exists at any time. A second launch
must hand off to the existing instance and exit immediately.

## Behaviour

- If QuinSlate is not running: start normally.
- If QuinSlate is already running: bring the existing panel to focus, then exit
  the new process. No tray icon flicker, no error dialog.

## Implementation

On startup, before any UI is initialised, attempt to acquire a named mutex:

    Mutex mutex = new Mutex(true, "Local\\QuinSlateSingleInstance", out bool acquired);

If `acquired` is false, another instance holds the mutex.

Signal the existing instance by posting a custom message to its window:

    const int WM_QUINSLATE_ACTIVATE = 0x8001;  // WM_APP + 1

    HWND existing = FindWindow(null, "QuinSlateMainWindow");
    if (existing != IntPtr.Zero)
        PostMessage(existing, WM_QUINSLATE_ACTIVATE, 0, 0);

    Environment.Exit(0);

The existing instance handles `WM_QUINSLATE_ACTIVATE` in its Win32 message
loop and responds by showing and focusing the panel.

## Notes

- The mutex must be acquired before `RegisterHotKey`. Duplicate hotkey IDs
  registered by two instances cause silent failures — enforcement must come
  first.
- Do not release the mutex until the app exits. The OS releases it
  automatically on process termination, but explicit release on clean exit
  is good practice.
- `FindWindow` matches on window title. The main window must have a stable,
  non-localised title string (`"QuinSlateMainWindow"`) that is never shown in
  the UI.
