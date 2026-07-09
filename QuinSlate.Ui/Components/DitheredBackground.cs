namespace QuinSlate.Ui.Components;

/// <summary>
/// A rendered dithered-gradient background for the editor page: the mesh as a base64-encoded PNG
/// (built at native pixel size) plus the element's DIP/CSS size, which the page uses to display the
/// image 1:1 so the per-pixel dither is not re-quantized.
/// </summary>
internal sealed class DitheredBackground
{
    /// <summary>Creates the background descriptor.</summary>
    public DitheredBackground(string pngBase64, double cssWidth, double cssHeight)
    {
        PngBase64 = pngBase64;
        CssWidth = cssWidth;
        CssHeight = cssHeight;
    }

    /// <summary>The dithered mesh PNG, base64-encoded, for a <c>data:image/png;base64,</c> URI.</summary>
    public string PngBase64 { get; }

    /// <summary>The element's width in DIPs/CSS pixels (the image is shown at this width).</summary>
    public double CssWidth { get; }

    /// <summary>The element's height in DIPs/CSS pixels (the image is shown at this height).</summary>
    public double CssHeight { get; }
}
