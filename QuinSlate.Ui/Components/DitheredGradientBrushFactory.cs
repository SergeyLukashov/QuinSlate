using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Serilog;
using System;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Windows.UI;

namespace QuinSlate.Ui.Components;

/// <summary>
/// Builds an opaque <see cref="ImageBrush"/> containing a four-corner bilinear gradient mesh that
/// is dithered so it does not show 8-bit colour banding ("false contouring") on a dark,
/// low-contrast ramp.
/// </summary>
/// <remarks>
/// Each corner of the surface has its own colour (top-left, top-right, bottom-left, bottom-right);
/// every pixel is a smooth bilinear blend of the four. This gives an organic, non-linear colour
/// field with subtle directional depth rather than a flat ramp, while staying dim. Bilinear
/// interpolation is C0-continuous with no interior creases, so it introduces no banding seams of
/// its own; the low contrast still requires dithering.
///
/// The colour is computed per pixel in floating point and a symmetric triangular-PDF (TPDF) noise
/// of +/- 1 quantization level is added <b>before</b> rounding to 8-bit. Dithering the value
/// before it is quantized is what removes the banding: near a band boundary, pixels round up or
/// down at random in proportion to the sub-level fraction, smearing the boundary away. (A plain
/// XAML <see cref="LinearGradientBrush"/> quantizes with no dither, and adding noise on top of an
/// already-quantized gradient does not help — the band edges are already baked in.) TPDF of 1
/// level is the minimum that fully decorrelates the quantization error, so the grain is far below
/// what an additive overlay would need.
///
/// The bitmap MUST be rendered at the consuming element's native pixel size and shown 1:1
/// (<see cref="Stretch.Fill"/> onto an element of the matching DIP size). Scaling a dithered
/// bitmap blurs the per-pixel pattern and re-bands it, so callers pass the element's DIP size and
/// rasterization scale and rebuild on resize. The brush is opaque so the native text caret stays
/// visible (and ClearType keeps working) when it paints a <c>RichEditBox</c>/<c>TextBox</c>.
/// </remarks>
internal static class DitheredGradientBrushFactory
{
    // The four mesh corner colours are the single source of truth and live in App.xaml as the
    // AppGradient* Color resources (see that file). They are read by key here so the dithered
    // brush, the XAML fallback brushes, and MainWindow's flash fill all stay in sync. Start/End
    // are the diagonal endpoints (top-left / bottom-right); CornerTR/CornerBL are the other two.
    private const string TopLeftColorKeyDark = "AppGradientStartDark";
    private const string BottomRightColorKeyDark = "AppGradientEndDark";
    private const string TopRightColorKeyDark = "AppGradientCornerTRDark";
    private const string BottomLeftColorKeyDark = "AppGradientCornerBLDark";
    private const string TopLeftColorKeyLight = "AppGradientStartLight";
    private const string BottomRightColorKeyLight = "AppGradientEndLight";
    private const string TopRightColorKeyLight = "AppGradientCornerTRLight";
    private const string BottomLeftColorKeyLight = "AppGradientCornerBLLight";

    private const int BytesPerPixel = 4;

    // The PNG's stored DPI is irrelevant to display (the page sizes the image in CSS pixels);
    // a standard 96 is written so the metadata is well-formed.
    private const float StandardDpi = 96.0f;

    /// <summary>
    /// Creates a dithered gradient brush rendered at <paramref name="element"/>'s native pixel
    /// size for the element's current theme, or <c>null</c> if the element is not yet laid out
    /// (zero size) or the bitmap could not be allocated — in which case the caller should leave
    /// the XAML gradient fallback in place. The brush must be shown 1:1 on <paramref name="element"/>.
    /// </summary>
    public static ImageBrush CreateForElement(FrameworkElement element)
    {
        if (element == null || element.ActualWidth <= 0 || element.ActualHeight <= 0)
        {
            return null;
        }

        double scale = element.XamlRoot != null ? element.XamlRoot.RasterizationScale : 1.0;
        bool isDark = element.ActualTheme == ElementTheme.Dark;

        try
        {
            return Create(
                TopLeftColor(isDark),
                TopRightColor(isDark),
                BottomLeftColor(isDark),
                BottomRightColor(isDark),
                element.ActualWidth,
                element.ActualHeight,
                scale);
        }
        catch (Exception ex)
        {
            Log.ForContext(typeof(DitheredGradientBrushFactory)).Debug(
                ex, "Failed to build dithered gradient brush; caller degrades to the flat mid-tone fallback.");
            return null;
        }
    }

    /// <summary>
    /// The flat mid-tone of the gradient mesh for the given theme (the average of the four corner
    /// colours), used as the bare-window erase fill that prevents a flash before the dithered
    /// brush is painted.
    /// </summary>
    public static Color MidColor(bool isDark)
    {
        Color topLeft = TopLeftColor(isDark);
        Color topRight = TopRightColor(isDark);
        Color bottomLeft = BottomLeftColor(isDark);
        Color bottomRight = BottomRightColor(isDark);
        return Color.FromArgb(
            0xFF,
            (byte)((topLeft.R + topRight.R + bottomLeft.R + bottomRight.R) / 4),
            (byte)((topLeft.G + topRight.G + bottomLeft.G + bottomRight.G) / 4),
            (byte)((topLeft.B + topRight.B + bottomLeft.B + bottomRight.B) / 4));
    }

    private static Color TopLeftColor(bool isDark)
    {
        return ReadColor(isDark ? TopLeftColorKeyDark : TopLeftColorKeyLight);
    }

    private static Color TopRightColor(bool isDark)
    {
        return ReadColor(isDark ? TopRightColorKeyDark : TopRightColorKeyLight);
    }

    private static Color BottomLeftColor(bool isDark)
    {
        return ReadColor(isDark ? BottomLeftColorKeyDark : BottomLeftColorKeyLight);
    }

    private static Color BottomRightColor(bool isDark)
    {
        return ReadColor(isDark ? BottomRightColorKeyDark : BottomRightColorKeyLight);
    }

    private static Color ReadColor(string key)
    {
        if (Application.Current != null
            && Application.Current.Resources.TryGetValue(key, out object value)
            && value is Color color)
        {
            return color;
        }

        // Defensive only: the palette is defined in App.xaml and should always resolve.
        return Color.FromArgb(0xFF, 0x00, 0x00, 0x00);
    }

    /// <summary>
    /// Renders the dithered gradient at <paramref name="element"/>'s native pixel size for the
    /// element's current theme and returns it as a base64-encoded PNG together with the element's
    /// DIP (CSS) size, or <c>null</c> if the element is not laid out yet. The PNG is the exact same
    /// per-pixel TPDF-dithered mesh the brush uses; the consuming page must display it 1:1 at the
    /// returned CSS size so the native-resolution bitmap is not re-quantized (which re-bands).
    /// </summary>
    public static async Task<DitheredBackground> CreateElementBackgroundAsync(FrameworkElement element)
    {
        if (element == null || element.ActualWidth <= 0 || element.ActualHeight <= 0)
        {
            return null;
        }

        double scale = element.XamlRoot != null ? element.XamlRoot.RasterizationScale : 1.0;
        bool isDark = element.ActualTheme == ElementTheme.Dark;
        double cssWidth = element.ActualWidth;
        double cssHeight = element.ActualHeight;

        try
        {
            byte[] pixels = ComputePixels(
                TopLeftColor(isDark),
                TopRightColor(isDark),
                BottomLeftColor(isDark),
                BottomRightColor(isDark),
                cssWidth,
                cssHeight,
                scale,
                out int width,
                out int height);

            string base64 = await EncodePngBase64Async(pixels, width, height);
            return new DitheredBackground(base64, cssWidth, cssHeight);
        }
        catch (Exception ex)
        {
            Log.ForContext(typeof(DitheredGradientBrushFactory)).Debug(
                ex, "Failed to build dithered gradient PNG; the page keeps the flat mid-tone fallback.");
            return null;
        }
    }

    private static ImageBrush Create(
        Color topLeft,
        Color topRight,
        Color bottomLeft,
        Color bottomRight,
        double widthDips,
        double heightDips,
        double rasterizationScale)
    {
        byte[] pixels = ComputePixels(
            topLeft, topRight, bottomLeft, bottomRight, widthDips, heightDips, rasterizationScale,
            out int width, out int height);

        var bitmap = new WriteableBitmap(width, height);
        using (Stream stream = bitmap.PixelBuffer.AsStream())
        {
            stream.Write(pixels, 0, pixels.Length);
        }
        bitmap.Invalidate();

        return new ImageBrush
        {
            ImageSource = bitmap,
            Stretch = Stretch.Fill,
        };
    }

    /// <summary>
    /// Computes the BGRA (opaque) pixel buffer of the dithered four-corner bilinear mesh at the
    /// native pixel size (<paramref name="widthDips"/> x <paramref name="rasterizationScale"/>).
    /// Shared by the <see cref="WriteableBitmap"/> brush path and the PNG-for-page path so both
    /// present an identical surface.
    /// </summary>
    private static byte[] ComputePixels(
        Color topLeft,
        Color topRight,
        Color bottomLeft,
        Color bottomRight,
        double widthDips,
        double heightDips,
        double rasterizationScale,
        out int width,
        out int height)
    {
        int w = Math.Max(1, (int)Math.Round(widthDips * rasterizationScale));
        int h = Math.Max(1, (int)Math.Round(heightDips * rasterizationScale));
        width = w;
        height = h;

        byte[] pixels = new byte[w * h * BytesPerPixel];

        // u/v are the pixel's normalised position across the surface (0..1). The colour at each
        // pixel is the bilinear blend of the four corner colours: interpolate the top and bottom
        // edges horizontally by u, then blend those by v.
        double uDenominator = w > 1 ? w - 1 : 1;
        double vDenominator = h > 1 ? h - 1 : 1;

        uint baseSeed = 0x9E3779B9u ^ (uint)(w * 73856093) ^ (uint)(h * 19349663);

        // Rows are independent, so the fill is parallelised across cores: at native pixel size on a
        // high-DPI window this is millions of RNG draws that would otherwise run single-threaded on
        // the UI thread during a resize/theme rebuild. Each row seeds its own RNG from the row index
        // so the noise stays deterministic and decorrelated between rows; TPDF dithering needs only
        // per-pixel decorrelated noise, not a single globally-sequential stream, so the result is
        // visually identical.
        System.Threading.Tasks.Parallel.For(0, h, y =>
        {
            uint rngState = baseSeed ^ ((uint)(y + 1) * 2654435761u);
            if (rngState == 0)
            {
                rngState = 1;
            }

            double v = y / vDenominator;
            int index = y * w * BytesPerPixel;
            for (int x = 0; x < w; x++)
            {
                double u = x / uDenominator;

                // BGRA order, opaque. Each channel is dithered independently because the channels
                // band at different positions (their corner deltas differ).
                pixels[index] = DitherChannel(BilinearChannel(topLeft.B, topRight.B, bottomLeft.B, bottomRight.B, u, v), ref rngState);
                pixels[index + 1] = DitherChannel(BilinearChannel(topLeft.G, topRight.G, bottomLeft.G, bottomRight.G, u, v), ref rngState);
                pixels[index + 2] = DitherChannel(BilinearChannel(topLeft.R, topRight.R, bottomLeft.R, bottomRight.R, u, v), ref rngState);
                pixels[index + 3] = 0xFF;
                index += BytesPerPixel;
            }
        });

        return pixels;
    }

    /// <summary>
    /// Encodes an opaque BGRA pixel buffer as a PNG and returns it base64-encoded, for embedding
    /// in the editor page as a <c>data:</c> URI.
    /// </summary>
    private static async Task<string> EncodePngBase64Async(byte[] bgraPixels, int width, int height)
    {
        var stream = new InMemoryRandomAccessStream();
        BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Ignore,
            (uint)width,
            (uint)height,
            StandardDpi,
            StandardDpi,
            bgraPixels);
        await encoder.FlushAsync();

        var bytes = new byte[(int)stream.Size];
        using (var reader = new DataReader(stream.GetInputStreamAt(0)))
        {
            await reader.LoadAsync((uint)stream.Size);
            reader.ReadBytes(bytes);
        }

        return Convert.ToBase64String(bytes);
    }

    /// <summary>
    /// Bilinearly blends a single channel across the four corners: interpolates the top edge
    /// (<paramref name="topLeft"/> to <paramref name="topRight"/>) and bottom edge
    /// (<paramref name="bottomLeft"/> to <paramref name="bottomRight"/>) horizontally by
    /// <paramref name="u"/>, then blends those vertically by <paramref name="v"/>.
    /// </summary>
    private static double BilinearChannel(byte topLeft, byte topRight, byte bottomLeft, byte bottomRight, double u, double v)
    {
        double top = topLeft + (u * (topRight - topLeft));
        double bottom = bottomLeft + (u * (bottomRight - bottomLeft));
        return top + (v * (bottom - top));
    }

    /// <summary>
    /// Adds +/- 1-level triangular dither to a floating-point channel value and quantizes to a byte.
    /// </summary>
    private static byte DitherChannel(double value, ref uint rngState)
    {
        double triangular = NextUnit(ref rngState) - NextUnit(ref rngState);
        int quantized = (int)Math.Round(value + triangular);
        if (quantized < 0)
        {
            quantized = 0;
        }
        else if (quantized > 255)
        {
            quantized = 255;
        }

        return (byte)quantized;
    }

    /// <summary>Fast xorshift PRNG returning a value in [0, 1).</summary>
    private static double NextUnit(ref uint state)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return (state & 0xFFFFFF) / 16777216.0;
    }
}
