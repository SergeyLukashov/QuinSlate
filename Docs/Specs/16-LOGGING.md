# SPEC: Application logging

> _Last updated: 2026-07-19_

## What
Add a proper, always-on logging subsystem so that the application's behaviour,
important lifecycle events, errors, and crashes are recorded to disk in a form
that is useful for debugging — verbosely during development, conservatively in
release. Logs live alongside the user's data and are automatically pruned so
they never clutter the C: drive.

## Decisions (resolved during brainstorming)

| Decision | Choice |
|---|---|
| Logging library | **Serilog** (deliberately waives the CLAUDE.md "no third-party NuGet" rule for logging — see ADR) |
| Rolling strategy | One file per calendar day, with a per-file size cap that rolls into parts |
| Retention | Keep 14 days, with a file-count backstop; pruned automatically |
| Release minimum level | Information (lifecycle events + all Warning/Error/Fatal/exceptions) |
| Debug minimum level | Verbose (full breadcrumb trail) |
| Environment header | Written once per **session** (not per rolled file) |

## Dependencies

Add the following NuGet packages to `QuinSlate.Ui`:

- **Serilog** — core.
- **Serilog.Sinks.File** — rolling file sink.
- **Serilog.Sinks.Async** — wraps the file sink so log writes never block the
  UI thread (the app already takes care to keep disk I/O off the keystroke
  path; logging must not regress that). Its buffer is flushed synchronously on
  shutdown.
- **Serilog.Sinks.Debug** — referenced but only wired up in DEBUG builds, to
  mirror output into the Visual Studio Output window during development.

No thread-id enricher package is added: a small custom `ILogEventEnricher`
supplies the managed thread id instead (keeps the dependency tree minimal).

### Trimming

Release builds set `PublishTrimmed=True`. Serilog configured **in code** (not
via configuration files / reflection) is trim-safe, and the app logs scalars
and exceptions rather than relying on reflective object destructuring. The
implementation must nonetheless verify that a trimmed `Release` build still
produces a log file, and add a `TrimmerRootAssembly` entry for the Serilog
assemblies if the linker strips anything needed.

## Structure

New folder `QuinSlate.Ui/Logging/`, namespace `QuinSlate.Ui.Logging`. One class
per file, filename identical to the class name, XML doc comments on public
members (these are infrastructure types, same bar as `Services/` and `Interop/`).

- **`LogBootstrapper`** (static)
  - `Initialize(string appDataDirectory)` — configures the global
    `Serilog.Log.Logger`: resolves the `Logs` subfolder, sets the
    build-dependent minimum level, configures the async-wrapped rolling file
    sink (and the Debug sink in DEBUG builds), applies the output template and
    the thread-id enricher.
  - `Shutdown()` — calls `Log.CloseAndFlush()`. Idempotent and safe to call
    more than once.
- **`EnvironmentReport`** (pure, testable)
  - Produces the PC-configuration block written at session start.
- **`GlobalExceptionHandlers`** (static)
  - `Register()` — wires the three crash hooks (see "Global exception capture").
- **`ThreadIdEnricher`** — tiny `ILogEventEnricher` adding
  `Environment.CurrentManagedThreadId` as a `ThreadId` property.

Call sites obtain a contextual logger idiomatically via
`Serilog.Log.ForContext<T>()`; no custom logging facade is introduced.

**Web editor page (added 2026-07-19):** the CodeMirror page logs into the same
pipeline. `WebEditor/build/src/pageLog.js` posts `log` messages (level,
message, optional stack) over the WebView2 bridge, and
`Components/EditorPageLogForwarder.cs` forwards them to the file sink under the
`QuinSlate.Ui.WebEditor.EditorPage` source context. Page levels mirror
Serilog's (`debug`/`information`/`warning`/`error`), so the release
Information floor applies unchanged. Uncaught page errors and unhandled
promise rejections are captured globally; repeats of one message are capped at
10 per page load and both sides clamp lengths. The PII guardrail is identical:
log calls carry names, indices, counts, and lengths — never buffer text.

## File location, rolling and retention

- **Folder:** `{AppDataPathResolver.Resolve()}\Logs\`. This is the same root the
  buffers and `settings.json` already use, so it is correct for both deployment
  models: `LocalFolder\Logs` when packaged, `%AppData%\QuinSlate\Logs` when
  unpackaged. The directory is created if missing; a missing directory is not
  an error.
- **Filename:** base `quinslate-.log` with `RollingInterval.Day`, producing e.g.
  `quinslate-20260615.log`.
- **Size cap:** `fileSizeLimitBytes ≈ 10 MB` with `rollOnFileSizeLimit: true`, so
  a runaway day rolls into `quinslate-20260615_001.log` parts rather than one
  unbounded file.
- **Retention:** `retainedFileTimeLimit = 14 days` plus a `retainedFileCountLimit`
  backstop. Serilog prunes on roll, keeping the footprint bounded to tens of MB
  worst case. No custom cleanup code is written — retention is delegated to the
  sink.

## Levels and verbosity

- **DEBUG build:** minimum level **Verbose**; the Debug-output sink is enabled.
- **RELEASE build:** minimum level **Information** — startup, hotkey
  registration, tray lifecycle, shutdown, plus every Warning/Error/Fatal and all
  exceptions. Verbose/Debug breadcrumbs are dropped.

The minimum level is selected at compile time via `#if DEBUG`; no runtime level
switch is required for v1.

**Output template:**

    {Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] ({ThreadId}) {SourceContext}: {Message:lj}{NewLine}{Exception}

## Global exception capture

Registered early in `App` (in the constructor, after `InitializeComponent`).
The handlers reference Serilog's global `Log`, which is a silent no-op until
`LogBootstrapper.Initialize` runs, so early registration is harmless.

- **`Microsoft.UI.Xaml.Application.UnhandledException`** — UI-thread exceptions.
  Logged at **Fatal**, then `Log.CloseAndFlush()`; the process is allowed to
  terminate (handler does not swallow a genuine crash).
- **`AppDomain.CurrentDomain.UnhandledException`** — background-thread and
  finalizer crashes. Logged at **Fatal** (include `IsTerminating`), then flush.
- **`TaskScheduler.UnobservedTaskException`** — unobserved faulted tasks,
  exactly the fire-and-forget `_ = WriteFileAsync(...)` pattern in
  `BufferService`. Logged at **Error**, then `SetObserved()` so it does not
  escalate.

## Startup ordering and shutdown flush

**`App.OnLaunched` order:**

1. Single-instance mutex acquisition stays **first** (CLAUDE.md mandate; also
   avoids two processes contending for the same day's log file).
2. Second-instance branch: keeps its current lightweight behaviour (surface the
   existing window and exit). It does **not** initialise file logging, avoiding
   file-lock contention with the primary instance.
3. Primary instance: resolve the app-data directory →
   `LogBootstrapper.Initialize(...)` → write the session banner + environment
   report → continue with the existing service initialisation.

**Shutdown flush — critical:** `Log.CloseAndFlush()` must run before every
process exit, because `Environment.Exit` does not let the async sink drain:

- `MainWindow.Teardown()` — after the buffer flush, before releasing the mutex.
- `MainWindow.ExitApplication()` — before `Environment.Exit`.
- The second-instance branch in `App.OnLaunched` — before its `Environment.Exit`
  (no-op when logging was never initialised, which is the case there).
- The Fatal exception handlers — before the process terminates.

## Session banner and environment report (PC configuration)

At each session start the environment report is written as the first entries.
With daily rolling this satisfies "configuration at the beginning of the file"
on a **per-session** basis; a session that runs past midnight produces a new
day-file without a repeated banner. This is the standard trade-off and is
accepted for v1. (Strict per-file headers would require a custom sink wrapper
and are explicitly out of scope.)

Fields captured (all local to the user's own machine):

- Application name, version (from the assembly / `<Version>`), build
  configuration (Debug/Release), and packaged-vs-unpackaged state.
- A per-session GUID/id so restarts within one file are distinguishable.
- OS caption, version, and build number.
- OS architecture and process architecture (x64 / ARM64 / x86).
- Logical processor count and total physical RAM.
- .NET runtime description (`RuntimeInformation.FrameworkDescription`).
- Display configuration: monitor count, primary resolution, and DPI / scale.
- System culture, UI culture, and time zone.

## What gets logged — and what must NOT

**Instrumented (replacing today's silent `catch` blocks and `Debug.WriteLine`):**

- App start (with version and packaged state), single-instance outcome, normal
  shutdown, and exit.
- Hotkey registration success and **failure** (`HotkeyManager`) — a prime
  "the hotkey stopped working" debugging target that is currently invisible.
- Tray icon create / delete (`TrayIcon`).
- Settings load / save failures (`SettingsService`, currently swallowed).
- Buffer read / write failures (`BufferService`, currently swallowed) — at
  Warning.
- Startup run-key registration result (`StartupService`).
- Window show / hide and clipboard capture — at Debug (development only).

**Must NOT be logged — PII guardrail (the most important addition beyond the
original requirements):** the buffer/note **contents are never logged**. The
buffers are the user's private notes. Where it is useful, log only content
**length** or the buffer **index**, never the text itself. The same applies to
clipboard payloads captured by the dictate/capture feature.

## CLAUDE.md and ADR updates

Adopting Serilog deviates from the documented build baseline ("No third-party
NuGet packages unless absolutely necessary"). To keep the codebase honest and
stop a future agent from reverting it:

- Add `Docs/Decisions/01-LOGGING-SERILOG.md` (first ADR; the
  `Docs/Decisions/` folder is created here) recording the decision, the
  alternatives considered (custom logger, Microsoft.Extensions.Logging), and the
  rationale.
- Update `CLAUDE.md` to note that Serilog is an accepted dependency for logging
  and to point at the new `Logs/` location and this spec.

## Testing

- `EnvironmentReport` — produces a non-empty report containing the expected
  keys (app version, OS, architecture, runtime). Pure and deterministic enough
  to assert on.
- Log-path resolution — the `Logs` subfolder is composed correctly under the
  resolved app-data directory.
- Retention is delegated to Serilog, so there is no custom cleanup code to
  unit-test.

## Edge cases

- **Logs directory cannot be created / is not writable:** logging initialisation
  must fail soft — the app must still start and function. A failure to set up
  the file sink is caught; the app continues (Debug sink only, or no sink).
- **Disk full while writing:** Serilog's file sink drops the write rather than
  throwing into the caller; this is acceptable.
- **Two instances launched quickly:** only the primary opens the log file; the
  second exits before initialising logging, so there is no file-lock race.
- **Crash before `Initialize`:** the exception handlers are registered before
  the logger is configured, so a very early crash is a silent no-op (acceptable
  — the window is tiny and pre-dates any app state).
- **Long-running session crossing midnight:** the new day-file has no repeated
  environment banner (accepted, see above).

## Out of scope (v1)

- Runtime log-level switching / a settings toggle for verbosity.
- A log viewer UI or "open logs folder" menu entry.
- Strict per-rolled-file environment headers.
- Remote / telemetry log shipping.
