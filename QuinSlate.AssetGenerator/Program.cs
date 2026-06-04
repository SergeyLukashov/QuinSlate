using QuinSlate.AssetGenerator.Catalog;
using QuinSlate.AssetGenerator.Imaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace QuinSlate.AssetGenerator;

internal static class Program
{
    private static readonly int[] IconBmpSizes = { 16, 20, 24, 32, 40, 48, 64 };
    private const int IconPngSize = 256;
    private const int ExitSuccess = 0;
    private const int ExitFailure = 1;
    private const string SvgExtension = ".svg";

    private static async Task<int> Main(string[] args)
    {
        if (GeneratorOptions.TryParse(args, out var options, out var error) == false)
        {
            Console.Error.WriteLine($"Error: {error}");
            PrintUsage();
            return ExitFailure;
        }

        if (File.Exists(options.InputPath) == false)
        {
            Console.Error.WriteLine($"Error: input image not found: {options.InputPath}");
            return ExitFailure;
        }

        Directory.CreateDirectory(options.OutputDirectory);

        byte[] sourceBytes = await File.ReadAllBytesAsync(options.InputPath);
        IImageRenderer renderer = CreateRenderer(options, sourceBytes);
        try
        {
            var writer = new AssetWriter(renderer);

            var specs = WinUiAssetCatalog.Create();
            foreach (var spec in specs)
            {
                string outputPath = Path.Combine(options.OutputDirectory, spec.FileName);
                if (spec.Placement == AssetPlacement.Centered)
                {
                    await writer.RenderCenteredAsync(spec.Width, spec.Height, spec.LogoSize, outputPath);
                }
                else
                {
                    await writer.RenderFillAsync(spec.Width, spec.Height, outputPath);
                }

                Console.WriteLine($"  {spec.FileName} ({spec.Width}x{spec.Height})");
            }

            int fileCount = specs.Count;
            if (options.GenerateIcon)
            {
                var frames = new List<IconBitmapFrame>();
                foreach (int size in IconBmpSizes)
                {
                    frames.Add(await writer.GetIconFrameAsync(size));
                }

                byte[] pngFrame = await writer.GetScaledPngBytesAsync(IconPngSize);
                string iconPath = Path.Combine(options.OutputDirectory, options.IconName + ".ico");
                IconFileWriter.Write(iconPath, frames, pngFrame, IconPngSize);
                Console.WriteLine($"  {options.IconName}.ico ({IconBmpSizes.Length + 1} frames)");
                fileCount++;
            }

            Console.WriteLine($"Done. {fileCount} files written.");
            return ExitSuccess;
        }
        finally
        {
            if (renderer is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    private static IImageRenderer CreateRenderer(GeneratorOptions options, byte[] sourceBytes)
    {
        if (IsSvg(options.InputPath))
        {
            Console.WriteLine($"Rendering SVG '{options.InputPath}' natively at each size into '{options.OutputDirectory}'.");
            return new SvgImageRenderer(sourceBytes);
        }

        Console.WriteLine($"Generating assets from '{options.InputPath}' using {options.Interpolation} into '{options.OutputDirectory}'.");
        return new RasterImageRenderer(sourceBytes, options.Interpolation, options.Lobes);
    }

    private static bool IsSvg(string path)
    {
        return Path.GetExtension(path).Equals(SvgExtension, StringComparison.OrdinalIgnoreCase);
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage: QuinSlate.AssetGenerator <input-image> [output-directory] [options]");
        Console.WriteLine();
        Console.WriteLine("Generates the full WinUI 3 / MSIX asset set (all tiles across display scales,");
        Console.WriteLine("Square44x44 target sizes, store logo, wide tile, splash screen) plus a");
        Console.WriteLine("multi-resolution .ico, all derived from a single source image.");
        Console.WriteLine();
        Console.WriteLine("The input may be a raster image (PNG/JPG/...) or an .svg. SVG sources are");
        Console.WriteLine("rasterised natively at every target size for maximum sharpness.");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --icon-name <name>       Base name for the generated .ico (default: AppIcon).");
        Console.WriteLine("  --interpolation <mode>   Raster only: lanczos | fant | cubic | linear | nearestneighbor (default: lanczos).");
        Console.WriteLine("  --lobes <2-4>            Raster only: Lanczos kernel radius; higher is sharper (default: 3).");
        Console.WriteLine("  --no-icon                Skip .ico generation.");
        Console.WriteLine();
        Console.WriteLine("If no output directory is given, assets are written to ./GeneratedAssets.");
    }
}
