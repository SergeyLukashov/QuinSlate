namespace Jott.AssetGenerator.Imaging;

/// <summary>
/// One uncompressed (DIB) frame of an icon: square BGRA pixels with straight
/// (non-premultiplied) alpha, top-down row order.
/// </summary>
public sealed class IconBitmapFrame
{
    /// <summary>
    /// Creates an icon frame.
    /// </summary>
    /// <param name="size">Edge length in pixels.</param>
    /// <param name="straightBgra">Pixel bytes in BGRA order, straight alpha, top-down, tightly packed.</param>
    public IconBitmapFrame(int size, byte[] straightBgra)
    {
        Size = size;
        StraightBgra = straightBgra;
    }

    /// <summary>Edge length of the frame in pixels.</summary>
    public int Size { get; }

    /// <summary>BGRA pixels with straight alpha, top-down, tightly packed (stride = Size * 4).</summary>
    public byte[] StraightBgra { get; }
}
