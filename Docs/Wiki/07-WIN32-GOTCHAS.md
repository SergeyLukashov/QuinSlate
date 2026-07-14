# Win32 gotchas

> _Last updated: 2026-07-14_

Platform behaviour to know before touching the interop layer (`Interop/`, tray, hotkeys,
clipboard).

- **Message loop** — `RegisterHotKey` requires a Win32 message loop. Hook into the existing HWND
  via `WindowNative` / `Win32Interop`; do not create a hidden secondary window for this.

- **Tray icon lifetime** — Call `NIM_DELETE` before the referenced window is destroyed, or Windows
  leaves a ghost icon until the next Explorer restart.

- **Clipboard and STA** — All clipboard operations must run on an STA thread. WinUI 3 is STA by
  default, but async work that hops threads will throw. Keep clipboard operations synchronous or
  explicitly marshal back to the UI thread.
