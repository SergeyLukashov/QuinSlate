namespace Jott.AssetGenerator.Catalog;

/// <summary>
/// A single output asset: its file name, pixel dimensions, and how the source
/// image is placed onto it.
/// </summary>
public sealed class AssetSpecification
{
    /// <summary>
    /// Creates an asset specification.
    /// </summary>
    /// <param name="fileName">Output file name including the WinUI scale or target-size qualifier.</param>
    /// <param name="width">Canvas width in pixels.</param>
    /// <param name="height">Canvas height in pixels.</param>
    /// <param name="placement">How the source is positioned on the canvas.</param>
    /// <param name="logoSize">Edge length of the centred logo box; ignored when <paramref name="placement"/> is <see cref="AssetPlacement.Fill"/>.</param>
    public AssetSpecification(string fileName, int width, int height, AssetPlacement placement, int logoSize)
    {
        FileName = fileName;
        Width = width;
        Height = height;
        Placement = placement;
        LogoSize = logoSize;
    }

    /// <summary>Output file name including its WinUI qualifier.</summary>
    public string FileName { get; }

    /// <summary>Canvas width in pixels.</summary>
    public int Width { get; }

    /// <summary>Canvas height in pixels.</summary>
    public int Height { get; }

    /// <summary>How the source image is positioned on the canvas.</summary>
    public AssetPlacement Placement { get; }

    /// <summary>Edge length of the centred logo box in pixels; unused for <see cref="AssetPlacement.Fill"/>.</summary>
    public int LogoSize { get; }
}
