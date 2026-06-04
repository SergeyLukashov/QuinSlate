# QuinSlate.AssetGenerator

A command-line tool that turns **one source image** — a raster (PNG/JPG/…) **or an SVG** —
into the **complete WinUI 3 / MSIX asset set** plus a multi-resolution `.ico`. It is how
QuinSlate's app icons and tiles are (re)generated — don't hand-roll resizing scripts.

SVG sources get rasterised **natively at each target size**, so every output is razor-sharp
with no downscaling at all. Raster sources are downscaled with a high-quality Lanczos filter.

## Why it exists

WinUI 3 / MSIX apps need a sprawl of pre-scaled PNGs (every tile at scales 100–400,
Square44x44 target sizes, store logo, wide tile, splash screen), and the tray icon needs
a real multi-frame `.ico`. Producing these by hand is tedious and error-prone. This tool
derives all of them from a single high-resolution logo, with a resampler tuned for
**sharp, clean** results at small sizes.

## Usage

```
QuinSlate.AssetGenerator <input-image> [output-directory] [options]
```

- `<input-image>` — source logo. An **`.svg`** (best — resolution-independent) or a raster
  image (PNG/JPG/…). A raster source should be square and high-resolution (e.g. 1024×1024)
  for the best downscales. Non-square sources are scaled to fit and centred.
- `[output-directory]` — where files are written. Defaults to `./GeneratedAssets`
  (created if missing).

Run it from the repo root via the .NET CLI:

```bash
dotnet run --project QuinSlate.AssetGenerator -p:Platform=x64 -- <input-image> [output-directory] [options]
```

### Example

```bash
# From a raster logo
dotnet run --project QuinSlate.AssetGenerator -p:Platform=x64 -- ^
  QuinSlate.Ui/Assets/Logo-1024x1024.png Scratch/GeneratedAssets --icon-name QuinSlate

# From an SVG (rasterised natively at every size)
dotnet run --project QuinSlate.AssetGenerator -p:Platform=x64 -- ^
  Assets/Logo.svg Scratch/GeneratedAssets --icon-name QuinSlate
```

## Options

| Option | Default | Description |
|---|---|---|
| `--icon-name <name>` | `AppIcon` | Base file name (without extension) of the generated `.ico`. |
| `--interpolation <mode>` | `lanczos` | **Raster only.** Resampling algorithm: `lanczos`, `fant`, `cubic`, `linear`, or `nearestneighbor`. |
| `--lobes <2-4>` | `3` | **Raster only.** Lanczos kernel radius. Higher is sharper (4 is crispest, with a slight risk of ringing). Ignored unless `--interpolation lanczos`. |
| `--no-icon` | _(off)_ | Skip `.ico` generation; emit PNGs only. |

`--interpolation` and `--lobes` apply to raster sources only — SVG inputs are rendered
directly and ignore them.

## What it generates

All PNGs are 32-bit (BGRA) with a transparent background. Square tiles are downscaled to
fill the canvas; the wide tile and splash screen center the logo on a transparent canvas.

**Tiles, each at scales 100 / 125 / 150 / 200 / 400:**

- `Square44x44Logo` (44–176 px)
- `Square71x71Logo` (small tile)
- `Square150x150Logo` (medium tile)
- `Square310x310Logo` (large tile)
- `StoreLogo`
- `LockScreenLogo`
- `Wide310x150Logo` (centered)
- `SplashScreen` (centered)

**Square44x44 target sizes** (used by the shell for taskbar / Start), at 16, 24, 32, 48,
and 256 px — each as a plated `targetsize-N.png` and an `targetsize-N_altform-unplated.png`.

**`<icon-name>.ico`** — a multi-resolution icon with uncompressed DIB frames at 16, 20,
24, 32, 40, 48, 64 px (the sizes the shell loads for tray/taskbar) plus a 256 px PNG frame.

That's 50 PNGs + 1 `.ico` = 51 files.

## Image quality

**SVG sources** ([`Imaging/SvgImageRenderer.cs`](Imaging/SvgImageRenderer.cs)) are rasterised
with Skia at each target resolution. Since the artwork is vector, there is no downscaling
and therefore no resampling blur — the result is as sharp as the size allows at every size.
This is the recommended source format. If you have the logo as SVG, use it.

**Raster sources** ([`Imaging/RasterImageRenderer.cs`](Imaging/RasterImageRenderer.cs)) are
downscaled with a hand-written **Lanczos** resampler
([`Imaging/LanczosResampler.cs`](Imaging/LanczosResampler.cs)) — a separable windowed-sinc
filter, the standard choice for sharp, faithful downscaling. All resampling is done in
**premultiplied alpha** so the transparent edges of the source don't bleed dark fringes
into the result. WIC (`Windows.Graphics.Imaging`) does not expose Lanczos through WinRT,
which is why it is implemented in managed code. The other modes route through WIC and are
kept as fallbacks:

- **`fant`** — area averaging; clean but soft on large reductions.
- **`cubic`** — bicubic; sharp but prone to ringing halos.
- **`linear`**, **`nearestneighbor`** — basic, mostly for comparison.

For crisp app icons from a raster source, stick with the `lanczos` default. If you need
extra bite, try `--lobes 4`.

## Using the output in QuinSlate.Ui

`QuinSlate.Ui` only references a subset of the full set (the `scale-200` variants, one
target-size, `StoreLogo.png`, and `TrayIcon.ico`). After generating into a scratch directory,
copy the needed files into `QuinSlate.Ui/Assets/`, mapping `StoreLogo.scale-100.png` →
`StoreLogo.png`. The base logo lives at `QuinSlate.Ui/Assets/Logo-1024x1024.png`.

> If you want crisp icons at 100 / 125 / 150 % DPI as well, wire the full multi-scale set
> into `Package.appxmanifest` and `QuinSlate.Ui.csproj` instead of just the `scale-200` files.

## Project layout

- [`Program.cs`](Program.cs) — entry point, orchestration, console output.
- [`GeneratorOptions.cs`](GeneratorOptions.cs) — command-line parsing and defaults.
- [`Catalog/`](Catalog/) — the asset table: `AssetSpecification`, `AssetPlacement`, and
  `WinUiAssetCatalog` (computes every file name and size from base dimensions × scales).
- [`Imaging/`](Imaging/) — the rendering pipeline:
  - `IImageRenderer` — abstraction that produces straight-alpha BGRA at a given size.
  - `RasterImageRenderer` — raster decode + Lanczos/WIC downscale; `LanczosResampler` is the
    managed Lanczos kernel.
  - `SvgImageRenderer` — Skia-based native SVG rasterisation.
  - `AssetWriter` — composes/centres and PNG-encodes the rendered pixels.
  - `IconFileWriter` — assembles the multi-frame `.ico`.

The project targets `net10.0-windows10.0.19041.0` and runs unpackaged
(`WindowsPackageType=None`). It uses WIC (in-box) for raster decode and PNG encode, and
**SkiaSharp + Svg.Skia** for SVG rasterisation. These NuGet packages are confined to this
build-time tool and are **not** referenced by the shipping `QuinSlate.Ui` app.
