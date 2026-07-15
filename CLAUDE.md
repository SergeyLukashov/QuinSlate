## Stack

| Layer | Choice |
|---|---|
| UI framework | WinUI 3 (.NET 10) |
| Buffer editor | CodeMirror 6 hosted in a single `WebView2` (vendored bundle under `QuinSlate.Ui/WebEditor/`, served via a virtual-host mapping) |
| Tray icon | Win32 `Shell_NotifyIcon` via P/Invoke |
| Global hotkeys | Win32 `RegisterHotKey` via P/Invoke |
| Persistence | Plain `.txt` files in `%AppData%\QuinSlate\` |
| Always-on-top | Win32 `SetWindowPos` via P/Invoke |
| Single instance | Named Mutex (`Local\QuinSlateSingleInstance`) |
| Clipboard | WinRT `Windows.ApplicationModel.DataTransfer.Clipboard` (read/write via managed API) |
| Window background | Per-pixel TPDF-dithered gradient into a `WriteableBitmap` (no external deps); the editor page shows the same mesh as a host-rendered PNG |

WinUI 3 has no native tray API. All tray behaviour is Win32 via P/Invoke.

The five buffers are edited in one WebView2 hosting CodeMirror 6; host and page
talk over a JSON bridge (`Components/EditorHost.cs`). **The "never log buffer
contents" rule extends across the bridge:** messages carrying buffer text
(`init`, `setText`, `insert`, `contentSync`, `calcRequest`/`calcResult`) are
never logged on either side — only message names, indices, and lengths.

---

## Project structure

```
QuinSlate/
├── QuinSlate.Ui/             # WinUI 3 desktop application source code and assets
├── QuinSlate.Tests/          # Unit tests for models and services
├── QuinSlate.AssetGenerator/ # Asset generator utility console app
├── QuinSlate.EmojiPickerBench/ # Benchmark harness for the emoji picker
├── Docs/
│   ├── Specs/                # Product specifications and feature queue
│   ├── Investigations/       # Deep-dives into platform bugs, dead-end archaeology
│   ├── Decisions/            # Architecture decision records (ADRs)
│   ├── Plans/                # Implementation plans for multi-step work
│   └── Wiki/                 # "Read this before you change X" — distilled platform knowledge
├── Scratch/                  # Local-only scratchpad for temporary code/scripts
```

---

## Build baseline

Do not enable nullable reference types. Do not use `?` annotations on reference
types or `!` null-forgiving operators. Null checks are explicit `if (x == null)` guards.

Serilog is an accepted dependency for application logging (the one sanctioned
exception to "no third-party NuGet packages") — see `Docs/Decisions/01-LOGGING-SERILOG.md`
and `Docs/Specs/16-LOGGING.md`. Logs roll daily into the `Logs/` subfolder
of the app-data directory. Never log buffer/note contents.

---

## Code style

- C# 13 / .NET 10 idioms. File-scoped namespaces everywhere.
- Namespaces must match the folder structure exactly.
- One class per file. The filename must be identical to the class name.
- Follow SOLID principles. Each class has a single, focused responsibility.
- `private` fields use `camelCase`. Properties and methods use `PascalCase`.
- `var` is fine when the right-hand side makes the type obvious; use concrete types elsewhere.
- Keep files, classes, and functions as small as possible. **A class over 500 lines is not acceptable** — split it along SOLID lines (extract the distinct responsibility into its own class, in its own file) rather than letting it grow.
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

**Settings** — Non-buffer state lives in a single `settings.json`. Do not use the registry. "Launch on startup" is **not** a registry run-key — QuinSlate is MSIX-packaged, so it uses the `windows.startupTask` manifest extension plus the `Windows.ApplicationModel.StartupTask` API (registry run-key writes get virtualized and never run; see `Docs/Specs/02-STARTUP.md`).

**Single instance** — Enforce via named mutex before hotkey registration. The existing instance must respond to a second launch by surfacing the panel.

**Hotkey IDs** — Keep all hotkey ID constants in one place. Duplicate IDs across instances cause silent failures; this is why single-instance enforcement must
run first.

---

## UI controls

Always use modern WinUI 3 controls. Some examples, documentation, and a comprehensive list of all available controls can be found in the [WinUI Gallery repository](https://github.com/microsoft/WinUI-Gallery).
Always use the WinUI 3 Expert agent when working with UI.

---

## Commands

```bash
dotnet build QuinSlate.slnx -p:Platform=x64   # verify compilation
dotnet test QuinSlate.slnx -p:Platform=x64    # after significant changes; required before considering any task complete
dotnet format QuinSlate.slnx                  # after EVERY task that writes or modifies any .cs file — no exceptions
```

QuinSlate is MSIX-packaged: `dotnet run` and launching the bare exe do not work
(unpackaged WinUI fails with `REGDB_E_CLASSNOTREG`).

---

## Before you touch X, read Y

| Area | Read first |
|---|---|
| Window/editor background, `AppGradient*` colours, `DitheredGradientBrushFactory` | `Docs/Wiki/05-BACKGROUND-GRADIENT-DITHERING.md` — colours live **only** in `App.xaml`; window and editor meshes swap in together (all-or-nothing); never stretch a dithered bitmap |
| `Interop/`, tray icon, hotkeys, clipboard | `Docs/Wiki/07-WIN32-GOTCHAS.md` |
| `QuinSlate.Ui/WebEditor/`, the `EditorHost` bridge | `QuinSlate.Ui/WebEditor/CLAUDE.md` (auto-loads when working there) and `Docs/Wiki/06-WEB-EDITOR-BUNDLE.md` — never hand-edit `editor.bundle.js`; keep `autocorrect: "on"` |
| Tab strip, `TabViewItem` template, tab sizing | `Docs/Wiki/01-TABVIEW-DRAG-REORDER.md` |
| Editor `contentAttributes`, OS text input (emoji panel, dictation) | `Docs/Wiki/03-EDITOR-OS-TEXT-INPUT.md` |
| Creating or renaming any doc under `Docs/` | `Docs/Wiki/04-DOCS-CONVENTIONS.md` |

Full index: `Docs/Wiki/00-INDEX.md`.

---

## Docs

If it is decided during implementation to drift away from the initial requirements, agents should modify the corresponding `Docs/Specs/` `.md` files to reflect the new direction.

---

## Temporary code

All temporary code, experimental scripts, or one-off tests go in `Scratch/` only. This folder is excluded from source control and is never referenced by any project.
