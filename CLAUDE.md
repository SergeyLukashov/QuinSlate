## Stack

| Layer | Choice |
|---|---|
| UI framework | WinUI 3 (.NET 10) |
| Tray icon | Win32 `Shell_NotifyIcon` via P/Invoke |
| Global hotkeys | Win32 `RegisterHotKey` via P/Invoke |
| Persistence | Plain `.txt` files in `%AppData%\Jott\` |
| Always-on-top | Win32 `SetWindowPos` via P/Invoke |
| Single instance | Named Mutex (`Local\JottSingleInstance`) |
| Clipboard capture | `SendInput` + `WM_CLIPBOARDUPDATE` + `OpenClipboard` |

WinUI 3 has no native tray API. All tray behaviour is Win32 via P/Invoke.

---

## Sample project structure

```
Jott.Ui/
├── App.xaml
├── App.xaml.cs            # Startup, mutex check, tray icon init
├── MainWindow.xaml
├── MainWindow.xaml.cs     # Panel show/hide, pin toggle, keyboard nav
├── Interop/
│   ├── HotkeyManager.cs   # RegisterHotKey / UnregisterHotKey
│   ├── NativeMethods.cs   # All P/Invoke signatures
│   ├── TrayIcon.cs        # Shell_NotifyIcon wrapper
│   └── TrayMenu.cs
├── Models/
│   └── Buffer.cs          # Buffer index (1–7), color, file path, content
├── Services/
│   ├── BufferService.cs   # Read/write files, debounce timer, in-memory state
│   ├── SettingsService.cs # Window position, pin state, startup toggle (JSON)
│   └── StartupService.cs
├── Views/
│   ├── BufferPanel.xaml   # The 7-tab panel UI
│   └── BufferPanel.xaml.cs
└── Assets/                # App logos and icons

Jott.Tests/
├── Models/
│   └── BufferTests.cs
└── Services/
    ├── BufferServiceTests.cs
    └── SettingsServiceTests.cs

Specs/
├── 01-SPEC-CORE.md
├── ...
└── FEATURE-QUEUE.md
```

---

## Build baseline

Do not enable nullable reference types. Do not use `?` annotations on reference
types or `!` null-forgiving operators. Null checks are explicit `if (x == null)` guards.

---

## Code style

- C# 13 / .NET 10 idioms. File-scoped namespaces everywhere.
- Namespaces must match the folder structure exactly.
- One class per file. The filename must be identical to the class name.
- Follow SOLID principles. Each class has a single, focused responsibility.
- `private` fields use `camelCase`. Properties and methods use `PascalCase`.
- `var` is fine when the right-hand side makes the type obvious; use concrete types elsewhere.
- No `dynamic`.
- Prefer `async`/`await` over raw `Task.Run` where possible.
- Async/await for file I/O. Synchronous file writes only in the shutdown flush path.
- No third-party NuGet packages unless absolutely necessary.
- No regions (`#region`).
- No magic numbers. Every constant, raw string, or error code is extracted into a clearly named `const` or `enum`.
- XML doc comments (`///`) on all public members of `Interop/` and `Services/`.
- Do not add internal comments beyond what is genuinely necessary. Code must be self-descriptive. Only comment Win32 behaviour that cannot be made obvious through the code itself.

---

## Architecture rules

**P/Invoke** — All native signatures live in `NativeMethods.cs`. Do not scatter `[DllImport]` declarations across other files. Use `partial class` if a file needs to call native methods — import from `NativeMethods`, don't redeclare.

**Persistence** — Buffer files use UTF-8 with BOM (`new UTF8Encoding(true)`). Writes are debounced 300 ms after the last keystroke; never write on every keystroke. On exit, flush any pending debounced write synchronously before the process ends. A missing file on startup is an empty buffer — do not throw.

**Settings** — Non-buffer state lives in a single `settings.json`. Do not use the registry for anything except the startup run key.

**Single instance** — Enforce via named mutex before hotkey registration. The existing instance must respond to a second launch by surfacing the panel.

**Hotkey IDs** — Keep all hotkey ID constants in one place. Duplicate IDs across instances cause silent failures; this is why single-instance enforcement must
run first.

---

## Win32 gotchas

- **Message loop** — `RegisterHotKey` requires a Win32 message loop. Hook into the existing HWND via WindowNative` / `Win32Interop`; do not create a hidden secondary window for this.

- **Tray icon lifetime** — Call `NIM_DELETE` before the referenced window is destroyed, or Windows leaves a ghost icon until the next Explorer restart.

- **Clipboard and STA** — All clipboard operations must run on an STA thread. WinUI 3 is STA by default, but async work that hops threads will throw. Keep clipboard operations synchronous or explicitly marshal back to the UI thread.

---

## Build

To verify compilation during development, run:

```bash
dotnet build Jott.slnx
```

---

## Testing

After making significant changes, run the tests to verify correctness and catch regressions:

```bash
dotnet test Jott.slnx
```

This must be done before considering any task complete.

---

## Formatting

After **every** task that writes, modifies, or generates any `.cs` file, run:

```bash
dotnet format Jott.slnx
```

This applies to new files and edited files alike. Do not skip it for trivial changes.

---

## Specs

If it is decided during implementation to drift away from the initial requirements, agents should modify the corresponding `Specs/` `.md` files to reflect the new direction.

---

## Temporary code

All temporary code, experimental scripts, or one-off tests go in `Scratch/` only. This folder is excluded from source control and is never referenced by any project.
