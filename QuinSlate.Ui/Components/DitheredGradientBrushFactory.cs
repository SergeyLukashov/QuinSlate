using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.UI;

namespace QuinSlate.Ui.Components;

/// <summary>
/// Builds an opaque <see cref="ImageBrush"/> containing a diagonal two-colour gradient that is
/// dithered so it does not show 8-bit colour banding ("false contouring") on a dark,
/// low-contrast ramp.
/// </summary>
/// <remarks>
/// The gradient is computed per pixel in floating point and a symmetric triangular-PDF (TPDF)
/// noise of +/- 1 quantization level is added <b>before</b> rounding to 8-bit. Dithering the
/// value before it is quantized is what removes the banding: near a band boundary, pixels round
/// up or down at random in proportion to the sub-level fraction, smearing the boundary away. (A
/// plain XAML <see cref="LinearGradientBrush"/> quantizes with no dither, and adding noise on top
/// of an already-quantized gradient does not help — the band edges are already baked in.) TPDF of
/// 1 level is the minimum that fully decorrelates the quantization error, so the grain is far
/// below what an additive overlay would need.
///
/// The bitmap MUST be rendered at the consuming element's native pixel size and shown 1:1
/// (<see cref="Stretch.Fill"/> onto an element of the matching DIP size). Scaling a dithered
/// bitmap blurs the per-pixel pattern and re-bands it, so callers pass the element's DIP size and
/// rasterization scale and rebuild on resize. The brush is opaque so the native text caret stays
/// visible (and ClearType keeps working) when it paints a <c>RichEditBox</c>/<c>TextBox</c>.
/// </remarks>
internal static class DitheredGradientBrushFactory
{
    // The gradient endpoint colours are the single source of truth and live in App.xaml as the
    // AppGradient* Color resources (see that file). They are read by key here so the dithered
    // brush, the XAML fallback brushes, and MainWindow's flash fill all stay in sync.
    private const string StartColorKeyDark = "AppGradientStartDark";
    private const string EndColorKeyDark = "AppGradientEndDark";
    private const string StartColorKeyLight = "AppGradientStartLight";
    private const string EndColorKeyLight = "AppGradientEndLight";

    private const int BytesPerPixel = 4;

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
            return Create(StartColor(isDark), EndColor(isDark), element.ActualWidth, element.ActualHeight, scale);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// The flat mid-tone of the gradient for the given theme, used as the bare-window erase fill
    /// that prevents a flash before the dithered brush is painted.
    /// </summary>
    public static Color MidColor(bool isDark)
    {
        Color start = StartColor(isDark);
        Color end = EndColor(isDark);
        return Color.FromArgb(
            0xFF,
            (byte)((start.R + end.R) / 2),
            (byte)((start.G + end.G) / 2),
            (byte)((start.B + end.B) / 2));
    }

    private static Color StartColor(bool isDark)
    {
        return ReadColor(isDark ? StartColorKeyDark : StartColorKeyLight);
    }

    private static Color EndColor(bool isDark)
    {
        return ReadColor(isDark ? EndColorKeyDark : EndColorKeyLight);
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

    private static ImageBrush Create(Color startColor, Color endColor, double widthDips, double heightDips, double rasterizationScale)
    {
        int width = Math.Max(1, (int)Math.Round(widthDips * rasterizationScale));
        int height = Math.Max(1, (int)Math.Round(heightDips * rasterizationScale));

        byte[] pixels = new byte[width * height * BytesPerPixel];

        // Gradient runs corner-to-corner (StartPoint 0,0 -> EndPoint width,height), matching the
        // XAML fallback. t is the projection of the pixel onto that diagonal, normalised to 0..1.
        double diagonalLengthSquared = ((double)width * width) + ((double)height * height);

        uint rngState = 0x9E3779B9u ^ (uint)(width * 73856093) ^ (uint)(height * 19349663);
        if (rngState == 0)
        {
            rngState = 1;
        }

        int index = 0;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                double t = (((double)x * width) + ((double)y * height)) / diagonalLengthSquared;
                if (t < 0.0)
                {
                    t = 0.0;
                }
                else if (t > 1.0)
                {
                    t = 1.0;
                }

                // BGRA order, opaque. Each channel is dithered independently because the channels
                // band at different positions (their endpoint deltas differ).
                pixels[index] = DitherChannel(startColor.B + (t * (endColor.B - startColor.B)), ref rngState);
                pixels[index + 1] = DitherChannel(startColor.G + (t * (endColor.G - startColor.G)), ref rngState);
                pixels[index + 2] = DitherChannel(startColor.R + (t * (endColor.R - startColor.R)), ref rngState);
                pixels[index + 3] = 0xFF;
                index += BytesPerPixel;
            }
        }

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
