using System;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace QuinSlate.AssetGenerator.Imaging;

/// <summary>
/// Renders a raster source image (PNG/JPG/etc.) at a requested size. The default
/// <see cref="ResamplingMode.Lanczos"/> path decodes the source once and resamples
/// in managed code for sharp results; the other modes use the Windows Imaging
/// Component. Scaling is done with premultiplied alpha so transparent edges stay
/// clean, and the result is returned with straight alpha.
/// </summary>
public sealed class RasterImageRenderer : IImageRenderer
{
    private const int BytesPerPixel = 4;
    private const int OpaqueAlpha = 255;
    private const int AlphaRounding = 127;

    private readonly byte[] sourceBytes;
    private readonly ResamplingMode mode;
    private readonly int lobes;

    private byte[] sourceStraight;
    private int sourceWidth;
    private int sourceHeight;
    private bool sourceLoaded;

    /// <summary>
    /// Creates a renderer over the given encoded source image bytes.
    /// </summary>
    /// <param name="sourceBytes">The encoded (PNG/JPG/etc.) source image.</param>
    /// <param name="mode">The resampling algorithm used for every downscale.</param>
    /// <param name="lobes">Lanczos kernel radius; ignored for the other modes.</param>
    public RasterImageRenderer(byte[] sourceBytes, ResamplingMode mode, int lobes)
    {
        this.sourceBytes = sourceBytes;
        this.mode = mode;
        this.lobes = lobes;
    }

    /// <inheritdoc />
    public async Task<byte[]> RenderStraightBgraAsync(int width, int height)
    {
        if (mode == ResamplingMode.Lanczos)
        {
            await EnsureSourceLoadedAsync();
            return LanczosResampler.Resize(sourceStraight, sourceWidth, sourceHeight, width, height, lobes);
        }

        byte[] premultiplied = await WicScaleAsync(width, height);
        return ToStraightAlpha(premultiplied);
    }

    private async Task EnsureSourceLoadedAsync()
    {
        if (sourceLoaded)
        {
            return;
        }

        var decoder = await CreateDecoderAsync();
        var bitmap = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Straight,
            new BitmapTransform(),
            ExifOrientationMode.RespectExifOrientation,
            ColorManagementMode.DoNotColorManage);

        sourceWidth = bitmap.PixelWidth;
        sourceHeight = bitmap.PixelHeight;
        sourceStraight = ReadPixels(bitmap);
        sourceLoaded = true;
    }

    private async Task<byte[]> WicScaleAsync(int width, int height)
    {
        var decoder = await CreateDecoderAsync();
        var transform = new BitmapTransform();
        transform.ScaledWidth = (uint)width;
        transform.ScaledHeight = (uint)height;
        transform.InterpolationMode = ToWicMode(mode);

        var bitmap = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            transform,
            ExifOrientationMode.RespectExifOrientation,
            ColorManagementMode.DoNotColorManage);

        return ReadPixels(bitmap);
    }

    private async Task<BitmapDecoder> CreateDecoderAsync()
    {
        var stream = new InMemoryRandomAccessStream();
        var writer = new DataWriter(stream);
        writer.WriteBytes(sourceBytes);
        await writer.StoreAsync();
        writer.DetachStream();
        stream.Seek(0);
        return await BitmapDecoder.CreateAsync(stream);
    }

    private static BitmapInterpolationMode ToWicMode(ResamplingMode mode)
    {
        switch (mode)
        {
            case ResamplingMode.NearestNeighbor:
                return BitmapInterpolationMode.NearestNeighbor;
            case ResamplingMode.Linear:
                return BitmapInterpolationMode.Linear;
            case ResamplingMode.Cubic:
                return BitmapInterpolationMode.Cubic;
            default:
                return BitmapInterpolationMode.Fant;
        }
    }

    private static byte[] ReadPixels(SoftwareBitmap bitmap)
    {
        uint length = (uint)(bitmap.PixelWidth * bitmap.PixelHeight * BytesPerPixel);
        var buffer = new Windows.Storage.Streams.Buffer(length);
        bitmap.CopyToBuffer(buffer);
        var reader = DataReader.FromBuffer(buffer);
        byte[] pixels = new byte[buffer.Length];
        reader.ReadBytes(pixels);
        return pixels;
    }

    private static byte[] ToStraightAlpha(byte[] premultiplied)
    {
        byte[] straight = new byte[premultiplied.Length];
        for (int i = 0; i < premultiplied.Length; i += BytesPerPixel)
        {
            int alpha = premultiplied[i + 3];
            if (alpha == 0)
            {
                continue;
            }

            straight[i] = Unpremultiply(premultiplied[i], alpha);
            straight[i + 1] = Unpremultiply(premultiplied[i + 1], alpha);
            straight[i + 2] = Unpremultiply(premultiplied[i + 2], alpha);
            straight[i + 3] = (byte)alpha;
        }

        return straight;
    }

    private static byte Unpremultiply(byte channel, int alpha)
    {
        int value = (channel * OpaqueAlpha + AlphaRounding) / alpha;
        if (value > OpaqueAlpha)
        {
            value = OpaqueAlpha;
        }

        return (byte)value;
    }
}
