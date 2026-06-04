using System.Threading.Tasks;

namespace Jott.AssetGenerator.Imaging;

/// <summary>
/// Produces the source artwork as straight-alpha BGRA pixels at a requested size.
/// Raster sources are downscaled; vector (SVG) sources are rasterised natively at
/// the exact size for maximum sharpness.
/// </summary>
public interface IImageRenderer
{
    /// <summary>
    /// Renders the source at the given pixel dimensions.
    /// </summary>
    /// <returns>BGRA pixels with straight (non-premultiplied) alpha, top-down, stride = width * 4.</returns>
    Task<byte[]> RenderStraightBgraAsync(int width, int height);
}
