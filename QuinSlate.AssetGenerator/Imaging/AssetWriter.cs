using System;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace QuinSlate.AssetGenerator.Imaging;

/// <summary>
/// Writes WinUI assets from an <see cref="IImageRenderer"/>: square "fill" tiles,
/// "centered" tiles (logo centred on a transparent canvas), and icon frames. All
/// pixels flow as straight-alpha BGRA and are premultiplied only at the point of
/// PNG encoding.
/// </summary>
public sealed class AssetWriter
{
    private const int BytesPerPixel = 4;
    private const int OpaqueAlpha = 255;
    private const int AlphaRounding = 127;

    private readonly IImageRenderer renderer;

    /// <summary>
    /// Creates a writer backed by the given image renderer.
    /// </summary>
    public AssetWriter(IImageRenderer renderer)
    {
        this.renderer = renderer;
    }

    /// <summary>
    /// Renders a "fill" asset (source scaled to the full canvas) and writes it as a PNG.
    /// </summary>
    public async Task RenderFillAsync(int width, int height, string outputPath)
    {
        byte[] pixels = await renderer.RenderStraightBgraAsync(width, height);
        await SavePngAsync(pixels, width, height, outputPath);
    }

    /// <summary>
    /// Renders a "centered" asset: the source scaled to a square logo box and
    /// centred on a transparent canvas, written as a PNG.
    /// </summary>
    public async Task RenderCenteredAsync(int canvasWidth, int canvasHeight, int logoSize, string outputPath)
    {
        byte[] logo = await renderer.RenderStraightBgraAsync(logoSize, logoSize);

        byte[] canvas = new byte[canvasWidth * canvasHeight * BytesPerPixel];
        int offsetX = (canvasWidth - logoSize) / 2;
        int offsetY = (canvasHeight - logoSize) / 2;
        int logoStride = logoSize * BytesPerPixel;
        int canvasStride = canvasWidth * BytesPerPixel;
        for (int row = 0; row < logoSize; row++)
        {
            int destination = (offsetY + row) * canvasStride + offsetX * BytesPerPixel;
            Array.Copy(logo, row * logoStride, canvas, destination, logoStride);
        }

        await SavePngAsync(canvas, canvasWidth, canvasHeight, outputPath);
    }

    /// <summary>
    /// Returns a square icon frame at the given size with straight (non-premultiplied) alpha.
    /// </summary>
    public async Task<IconBitmapFrame> GetIconFrameAsync(int size)
    {
        byte[] pixels = await renderer.RenderStraightBgraAsync(size, size);
        return new IconBitmapFrame(size, pixels);
    }

    /// <summary>
    /// Returns the PNG-encoded bytes of the source rendered to a square of the given size.
    /// </summary>
    public async Task<byte[]> GetScaledPngBytesAsync(int size)
    {
        byte[] pixels = await renderer.RenderStraightBgraAsync(size, size);
        return await EncodePngAsync(pixels, size, size);
    }

    private static SoftwareBitmap WritePixels(byte[] premultiplied, int width, int height)
    {
        var bitmap = new SoftwareBitmap(BitmapPixelFormat.Bgra8, width, height, BitmapAlphaMode.Premultiplied);
        var writer = new DataWriter();
        writer.WriteBytes(premultiplied);
        var buffer = writer.DetachBuffer();
        bitmap.CopyFromBuffer(buffer);
        return bitmap;
    }

    private static byte[] ToPremultipliedAlpha(byte[] straight)
    {
        byte[] premultiplied = new byte[straight.Length];
        for (int i = 0; i < straight.Length; i += BytesPerPixel)
        {
            int alpha = straight[i + 3];
            premultiplied[i] = Premultiply(straight[i], alpha);
            premultiplied[i + 1] = Premultiply(straight[i + 1], alpha);
            premultiplied[i + 2] = Premultiply(straight[i + 2], alpha);
            premultiplied[i + 3] = (byte)alpha;
        }

        return premultiplied;
    }

    private static byte Premultiply(byte channel, int alpha)
    {
        return (byte)((channel * alpha + AlphaRounding) / OpaqueAlpha);
    }

    private static async Task SavePngAsync(byte[] straight, int width, int height, string outputPath)
    {
        byte[] encoded = await EncodePngAsync(straight, width, height);
        await System.IO.File.WriteAllBytesAsync(outputPath, encoded);
    }

    private static async Task<byte[]> EncodePngAsync(byte[] straight, int width, int height)
    {
        var bitmap = WritePixels(ToPremultipliedAlpha(straight), width, height);

        var outStream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, outStream);
        encoder.SetSoftwareBitmap(bitmap);
        await encoder.FlushAsync();

        outStream.Seek(0);
        var reader = new DataReader(outStream);
        await reader.LoadAsync((uint)outStream.Size);
        byte[] encoded = new byte[outStream.Size];
        reader.ReadBytes(encoded);
        return encoded;
    }
}
