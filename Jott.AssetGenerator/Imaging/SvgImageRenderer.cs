using SkiaSharp;
using Svg.Skia;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Jott.AssetGenerator.Imaging;

/// <summary>
/// Renders an SVG source natively at each requested size using Skia. Because the
/// source is vector, every output is rasterised at its exact target resolution —
/// there is no downscaling and therefore no resampling blur. The artwork is scaled
/// uniformly (preserving aspect ratio) and centred on a transparent canvas.
/// </summary>
public sealed class SvgImageRenderer : IImageRenderer, IDisposable
{
    private const int BytesPerPixel = 4;
    private const float CenterDivisor = 2f;

    private readonly SKSvg svg;
    private readonly SKRect bounds;

    /// <summary>
    /// Creates a renderer over the given SVG document bytes.
    /// </summary>
    /// <param name="svgBytes">The raw bytes of an SVG document.</param>
    public SvgImageRenderer(byte[] svgBytes)
    {
        svg = new SKSvg();
        using var stream = new MemoryStream(svgBytes);
        svg.Load(stream);

        if (svg.Picture == null)
        {
            throw new InvalidOperationException("The SVG could not be parsed.");
        }

        bounds = svg.Picture.CullRect;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            throw new InvalidOperationException("The SVG has no usable bounds (missing width/height or viewBox).");
        }
    }

    /// <inheritdoc />
    public Task<byte[]> RenderStraightBgraAsync(int width, int height)
    {
        return Task.FromResult(Render(width, height));
    }

    private byte[] Render(int width, int height)
    {
        float scale = Math.Min(width / bounds.Width, height / bounds.Height);
        float offsetX = (width - bounds.Width * scale) / CenterDivisor;
        float offsetY = (height - bounds.Height * scale) / CenterDivisor;

        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        canvas.Translate(offsetX, offsetY);
        canvas.Scale(scale, scale);
        canvas.Translate(-bounds.Left, -bounds.Top);
        canvas.DrawPicture(svg.Picture);
        canvas.Flush();

        byte[] pixels = new byte[width * height * BytesPerPixel];
        var destination = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            bool read = surface.ReadPixels(destination, handle.AddrOfPinnedObject(), destination.RowBytes, 0, 0);
            if (read == false)
            {
                throw new InvalidOperationException("Failed to read rasterised SVG pixels.");
            }
        }
        finally
        {
            handle.Free();
        }

        return pixels;
    }

    /// <summary>Releases the underlying SVG document.</summary>
    public void Dispose()
    {
        svg.Dispose();
    }
}
