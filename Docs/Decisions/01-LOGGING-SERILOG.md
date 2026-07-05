# ADR 0001: Logging with Serilog

> _Last updated: 2026-07-05_

## Status
Accepted — 2026-06-15

## Context
QuinSlate had no real logging: failures in hotkey registration, settings, and
buffer IO were swallowed or written only to the debugger via `Debug.WriteLine`,
and unhandled exceptions left no trace. The build baseline in CLAUDE.md says
"No third-party NuGet packages unless absolutely necessary."

## Decision
Adopt **Serilog** (with `Serilog.Sinks.File`, `Serilog.Sinks.Async`, and
`Serilog.Sinks.Debug`) for application logging. This is a deliberate, scoped
exception to the no-third-party rule: robust, banding-free rolling-file logging
with retention and async writes is enough value to justify the dependency, and
re-implementing it correctly in-house is more code and more risk than the
library.

## Alternatives considered
- **Custom lightweight logger** — fits the no-dependency ethos and the app's
  roll-your-own pattern, but re-implements rolling, size caps, retention,
  and async buffering that Serilog already does well.
- **Microsoft.Extensions.Logging** — first-party, but still NuGet packages, and
  has no official rolling-file provider, so a file sink would still be
  hand-rolled or community-sourced.

## Consequences
- Logs are written to `{AppData}\QuinSlate\Logs\` (or the package LocalFolder
  `\Logs\` when packaged), rolling daily with a 10 MB size cap and 14-day /
  31-file retention.
- Release logs at Information; Debug builds log at Verbose and mirror to the
  debugger output.
- The trimmed Release build roots the Serilog assemblies (`TrimmerRootAssembly`)
  so trimming cannot strip types Serilog reaches via reflection.
- Buffer/note contents are never logged (only lengths, indices, and paths).
- See [Docs/Specs/16-LOGGING.md](../Specs/16-LOGGING.md).
