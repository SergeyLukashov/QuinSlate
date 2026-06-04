using QuinSlate.AssetGenerator.Imaging;
using System.Collections.Generic;
using System.IO;

namespace QuinSlate.AssetGenerator;

/// <summary>
/// Parsed command-line options for the asset generator.
/// </summary>
public sealed class GeneratorOptions
{
    private const string DefaultIconName = "AppIcon";
    private const string DefaultOutputFolderName = "GeneratedAssets";
    private const int DefaultLobes = 3;
    private const int MinimumLobes = 2;
    private const int MaximumLobes = 4;
    private const string IconNameOption = "--icon-name";
    private const string InterpolationOption = "--interpolation";
    private const string LobesOption = "--lobes";
    private const string NoIconOption = "--no-icon";
    private const string OptionPrefix = "--";

    private GeneratorOptions(string inputPath, string outputDirectory, string iconName, ResamplingMode interpolation, int lobes, bool generateIcon)
    {
        InputPath = inputPath;
        OutputDirectory = outputDirectory;
        IconName = iconName;
        Interpolation = interpolation;
        Lobes = lobes;
        GenerateIcon = generateIcon;
    }

    /// <summary>Path to the source image to derive assets from.</summary>
    public string InputPath { get; }

    /// <summary>Directory that generated assets are written to.</summary>
    public string OutputDirectory { get; }

    /// <summary>Base file name (without extension) of the generated <c>.ico</c>.</summary>
    public string IconName { get; }

    /// <summary>Resampling algorithm used for every downscale.</summary>
    public ResamplingMode Interpolation { get; }

    /// <summary>Lanczos kernel radius; used only when <see cref="Interpolation"/> is <see cref="ResamplingMode.Lanczos"/>.</summary>
    public int Lobes { get; }

    /// <summary>Whether to emit a multi-resolution <c>.ico</c> alongside the PNG assets.</summary>
    public bool GenerateIcon { get; }

    /// <summary>
    /// Parses command-line arguments. Returns <c>false</c> and sets
    /// <paramref name="error"/> when the arguments are invalid.
    /// </summary>
    public static bool TryParse(string[] args, out GeneratorOptions options, out string error)
    {
        options = null;
        error = null;

        var positionals = new List<string>();
        string iconName = DefaultIconName;
        var interpolation = ResamplingMode.Lanczos;
        int lobes = DefaultLobes;
        bool generateIcon = true;

        for (int i = 0; i < args.Length; i++)
        {
            string argument = args[i];
            if (argument == IconNameOption)
            {
                if (i + 1 >= args.Length)
                {
                    error = $"{IconNameOption} requires a value.";
                    return false;
                }

                iconName = args[++i];
            }
            else if (argument == InterpolationOption)
            {
                if (i + 1 >= args.Length)
                {
                    error = $"{InterpolationOption} requires a value.";
                    return false;
                }

                if (TryParseInterpolation(args[++i], out interpolation) == false)
                {
                    error = $"Unknown interpolation mode '{args[i]}'. Use lanczos, fant, cubic, linear, or nearestneighbor.";
                    return false;
                }
            }
            else if (argument == LobesOption)
            {
                if (i + 1 >= args.Length)
                {
                    error = $"{LobesOption} requires a value.";
                    return false;
                }

                if (int.TryParse(args[++i], out lobes) == false || lobes < MinimumLobes || lobes > MaximumLobes)
                {
                    error = $"{LobesOption} must be an integer between {MinimumLobes} and {MaximumLobes}.";
                    return false;
                }
            }
            else if (argument == NoIconOption)
            {
                generateIcon = false;
            }
            else if (argument.StartsWith(OptionPrefix))
            {
                error = $"Unknown option '{argument}'.";
                return false;
            }
            else
            {
                positionals.Add(argument);
            }
        }

        if (positionals.Count == 0)
        {
            error = "An input image path is required.";
            return false;
        }

        if (positionals.Count > 2)
        {
            error = "Too many arguments. Expected an input path and an optional output directory.";
            return false;
        }

        string inputPath = positionals[0];
        string outputDirectory = positionals.Count == 2
            ? positionals[1]
            : Path.Combine(Directory.GetCurrentDirectory(), DefaultOutputFolderName);

        options = new GeneratorOptions(inputPath, outputDirectory, iconName, interpolation, lobes, generateIcon);
        return true;
    }

    private static bool TryParseInterpolation(string value, out ResamplingMode interpolation)
    {
        switch (value.ToLowerInvariant())
        {
            case "lanczos":
                interpolation = ResamplingMode.Lanczos;
                return true;
            case "fant":
                interpolation = ResamplingMode.Fant;
                return true;
            case "cubic":
                interpolation = ResamplingMode.Cubic;
                return true;
            case "linear":
                interpolation = ResamplingMode.Linear;
                return true;
            case "nearestneighbor":
            case "nn":
                interpolation = ResamplingMode.NearestNeighbor;
                return true;
            default:
                interpolation = ResamplingMode.Lanczos;
                return false;
        }
    }
}
