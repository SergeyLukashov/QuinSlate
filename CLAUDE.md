## Stack

| Layer | Choice |
|---|---|
| UI framework | WinUI 3 (.NET 10) |
| Tray icon | Win32 `Shell_NotifyIcon` via P/Invoke |
| Global hotkeys | Win32 `RegisterHotKey` via P/Invoke |
| Persistence | Plain `.txt` files in `%AppData%\QuinSlate\` |
| Always-on-top | Win32 `SetWindowPos` via P/Invoke |
| Single instance | Named Mutex (`Local\QuinSlateSingleInstance`) |
| Clipboard capture | `SendInput` + `WM_CLIPBOARDUPDATE` + `OpenClipboard` |
| Window/editor background | Per-pixel TPDF-dithered gradient into a `WriteableBitmap` (no external deps) |

WinUI 3 has no native tray API. All tray behaviour is Win32 via P/Invoke.

> [!NOTE]
> The application was renamed from **Jott** to **QuinSlate**. All namespaces, project names, settings paths, and mutex names have been updated accordingly.

---

## Project structure

```
QuinSlate/
├── QuinSlate.Ui/             # WinUI 3 desktop application source code and assets
├── QuinSlate.Tests/          # Unit tests for models and services
├── QuinSlate.AssetGenerator/ # Asset generator utility console app
├── Specs/                    # Product specifications and feature queue
├── Scratch/                  # Local-only scratchpad for temporary code/scripts
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

## UI controls

Always use modern WinUI 3 controls. Some examples, documentation, and a comprehensive list of all available controls can be found in the [WinUI Gallery repository](https://github.com/microsoft/WinUI-Gallery).
Always use the WinUI 3 Expert agent when working with UI.

---

## Background gradient (dithering)

The window and editor surfaces are painted with a warm, logo-derived diagonal gradient (amber
`#F2900F` / warm grey `#98948D`). A plain XAML `LinearGradientBrush` is **8-bit per channel and
does not dither**, so on this dark, low-contrast ramp it shows visible "false-contour" banding
lines. Acrylic/Mica hide this with their built-in noise layer, but those backdrops were removed.

The fix is `DitheredGradientBrushFactory` (`Components/`). It computes the gradient **per pixel in
floating point** and adds **triangular-PDF (TPDF) noise of ±1 quantization level before rounding**
to 8-bit, then writes the pixels into a `WriteableBitmap` exposed as an opaque `ImageBrush`.
Dithering *before* the quantization is the crucial part — it makes pixels near a band boundary
round up/down at random in proportion to the sub-level fraction, smearing the boundary away. (Note
what does **not** work: rendering an already-8-bit gradient and adding noise *on top* — the band
edges are already baked in, so the contours stay and you just get grain. Direct2D's own gradient
dithering via a high-precision stop collection was also too weak here. Both were tried.)

Rules when touching this:

- **Single source of truth for the colours: the `AppGradient{Start,End}{Dark,Light}` `Color`
  resources in `App.xaml`.** Change the gradient there and nowhere else. Everything else derives
  from them: `DitheredGradientBrushFactory` reads them by key at runtime (the brush actually shown),
  the XAML fallback brushes (`AppBackgroundGradient` in `App.xaml`, `TextControlBackground*` in
  `BufferPanelResources.xaml`) reference them via `ThemeResource`, and MainWindow's flash fill reads
  them via `DitheredGradientBrushFactory.MidColor`. (XAML has no `x:Static` and code cannot reliably
  read theme-keyed brushes, so the colours live in XAML resources and C# reads them by key.)
- The brush is **opaque** — required so the native text caret stays visible and ClearType keeps
  working (a transparent editor surface hides the caret).
- The XAML `AppBackgroundGradient` / `TextControlBackground*` brushes are only a **fallback**, shown
  before the dithered brush is applied on load (or if it cannot be built).
- The dithered brush is applied in code on `Loaded`, and rebuilt on `ActualThemeChanged` and on
  resize (`BufferPanel` debounces the resize rebuild; `TrayPeekPanel` is fixed-size). It overrides
  `TextControlBackground*` in each editor's own resource scope so the focus/hover visual states
  stay dithered, and re-enters the editor's visual state after applying (the focused state pins the
  background via `ThemeResource` when entered and won't otherwise pick up the swapped brush).
- **Render at native pixel size, never stretch.** Each surface's bitmap is built at that element's
  DIP size × `XamlRoot.RasterizationScale` and shown 1:1. Dithering is a per-pixel pattern;
  stretching the bitmap blurs it and the 8-bit output re-quantizes, which brings the banding
  straight back — hence per-element sizing and rebuild on resize.
- Gradient stops must be **collinear and monotonic** per channel; a non-monotonic or off-line
  interior stop creates its own seam line independent of dithering.

---

## Win32 gotchas

- **Message loop** — `RegisterHotKey` requires a Win32 message loop. Hook into the existing HWND via WindowNative` / `Win32Interop`; do not create a hidden secondary window for this.

- **Tray icon lifetime** — Call `NIM_DELETE` before the referenced window is destroyed, or Windows leaves a ghost icon until the next Explorer restart.

- **Clipboard and STA** — All clipboard operations must run on an STA thread. WinUI 3 is STA by default, but async work that hops threads will throw. Keep clipboard operations synchronous or explicitly marshal back to the UI thread.

---

## Build

To verify compilation during development, run:

```bash
dotnet build QuinSlate.slnx -p:Platform=x64
```

---

## Testing

After making significant changes, run the tests to verify correctness and catch regressions:

```bash
dotnet test QuinSlate.slnx -p:Platform=x64
```

This must be done before considering any task complete.

---

## Formatting

After **every** task that writes, modifies, or generates any `.cs` file, run:

```bash
dotnet format QuinSlate.slnx
```

This applies to new files and edited files alike. Do not skip it for trivial changes.

---

## Specs

If it is decided during implementation to drift away from the initial requirements, agents should modify the corresponding `Specs/` `.md` files to reflect the new direction.

---

## Temporary code

All temporary code, experimental scripts, or one-off tests go in `Scratch/` only. This folder is excluded from source control and is never referenced by any project.
