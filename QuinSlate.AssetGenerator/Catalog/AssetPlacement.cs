namespace QuinSlate.AssetGenerator.Catalog;

/// <summary>
/// Describes how the source image is positioned on an output asset's canvas.
/// </summary>
public enum AssetPlacement
{
    /// <summary>The source fills the entire canvas (square logos and tiles).</summary>
    Fill,

    /// <summary>The source is scaled to a logo box and centred on a transparent canvas (wide tiles, splash screen).</summary>
    Centered
}
