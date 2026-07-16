using System;
using Windows.UI;

namespace QuinSlate.Ui.Services;

/// <summary>
/// Turns a Windows accent shade into a colour that is actually readable as <em>text</em> on the
/// app's gradient background (see <c>Docs/Specs/21-LINKS.md</c>).
/// </summary>
/// <remarks>
/// <para>
/// The raw <c>SystemAccentColor</c> is a mid-tone: correct behind white glyphs (the task checkbox
/// fill, the selection, the calc highlight) and too dim in front of the gradient. WinUI draws the
/// same distinction — <c>AccentFillColorDefaultBrush</c> is the raw accent, while
/// <c>AccentTextFillColorPrimaryBrush</c> resolves to <c>SystemAccentColorLight3</c> on dark and
/// <c>SystemAccentColorDark2</c> on light. Those OS shades are the caller's starting point and are
/// returned untouched whenever they clear the contrast bar, so the user's accent is respected.
/// </para>
/// <para>
/// They are not, however, a guarantee. The shade-generation algorithm is unpublished, differs
/// between Windows 10 and 11, and was tuned against the stock WinUI surfaces — not this app's
/// dithered gradient mesh. A sufficiently dark user accent can still come out unreadable on the
/// dark mesh. So the shade is verified against the real background here and, only when it falls
/// short, nudged toward white (dark theme) or black (light theme) by the smallest amount that
/// clears the bar. That keeps the accent's hue while making readability a property of the code
/// rather than a hope about the user's colour choice.
/// </para>
/// </remarks>
internal static class AccentTextColorResolver
{
    /// <summary>WCAG 2.1 success criterion 1.4.3 (AA) for normal-size text.</summary>
    private const double MinimumContrastRatio = 4.5;

    // WCAG relative-luminance constants (https://www.w3.org/TR/WCAG21/#dfn-relative-luminance).
    private const double RedLuminanceWeight = 0.2126;
    private const double GreenLuminanceWeight = 0.7152;
    private const double BlueLuminanceWeight = 0.0722;
    private const double SrgbLinearThreshold = 0.03928;
    private const double SrgbLinearDivisor = 12.92;
    private const double SrgbGammaOffset = 0.055;
    private const double SrgbGammaDivisor = 1.055;
    private const double SrgbGammaExponent = 2.4;

    /// <summary>The 0.05 flare term in the WCAG contrast-ratio formula.</summary>
    private const double ContrastFlare = 0.05;

    private const double MaxChannel = 255.0;

    /// <summary>
    /// Iterations of the bisection that finds the blend factor. Twelve halvings resolve the factor
    /// to under 1/4096 — far finer than the 1/255 a colour channel can represent.
    /// </summary>
    private const int BlendSearchIterations = 12;

    private const double BlendFactorMin = 0.0;
    private const double BlendFactorMax = 1.0;

    /// <summary>
    /// Returns the colour to draw accent-coloured text in: <paramref name="preferred"/> when it
    /// already reads against <paramref name="background"/>, otherwise the least amount of
    /// <paramref name="reinforcement"/> blended into it that reaches the contrast floor.
    /// </summary>
    /// <param name="preferred">
    /// The OS accent shade for the current theme — <c>AccentLight3</c> on dark, <c>AccentDark2</c>
    /// on light, matching what <c>AccentTextFillColorPrimaryBrush</c> resolves to.
    /// </param>
    /// <param name="background">The surface the text sits on (the gradient mesh's flat mid-tone).</param>
    /// <param name="reinforcement">
    /// The extreme to blend toward when <paramref name="preferred"/> is too close to
    /// <paramref name="background"/>: opaque white on a dark theme, opaque black on a light one.
    /// </param>
    /// <returns>
    /// An opaque colour meeting <see cref="MinimumContrastRatio"/> against
    /// <paramref name="background"/>, or <paramref name="reinforcement"/> itself in the degenerate
    /// case where even that cannot reach the floor.
    /// </returns>
    public static Color Resolve(Color preferred, Color background, Color reinforcement)
    {
        if (ContrastRatio(preferred, background) >= MinimumContrastRatio)
        {
            return preferred;
        }

        // Nothing on this axis can clear the bar; the extreme is the best available.
        if (ContrastRatio(reinforcement, background) < MinimumContrastRatio)
        {
            return reinforcement;
        }

        // Blending toward the reinforcement moves luminance monotonically away from the
        // background's, so contrast rises monotonically with the factor and bisects cleanly.
        double low = BlendFactorMin;
        double high = BlendFactorMax;
        for (int i = 0; i < BlendSearchIterations; i++)
        {
            double middle = (low + high) / 2.0;
            if (ContrastRatio(Blend(preferred, reinforcement, middle), background) >= MinimumContrastRatio)
            {
                high = middle;
            }
            else
            {
                low = middle;
            }
        }

        return Blend(preferred, reinforcement, high);
    }

    /// <summary>
    /// The WCAG contrast ratio between two opaque colours, from 1.0 (identical) to 21.0
    /// (black against white). Order does not matter.
    /// </summary>
    public static double ContrastRatio(Color first, Color second)
    {
        double firstLuminance = RelativeLuminance(first);
        double secondLuminance = RelativeLuminance(second);
        double lighter = Math.Max(firstLuminance, secondLuminance);
        double darker = Math.Min(firstLuminance, secondLuminance);
        return (lighter + ContrastFlare) / (darker + ContrastFlare);
    }

    private static Color Blend(Color from, Color to, double factor)
    {
        return Color.FromArgb(
            0xFF,
            BlendChannel(from.R, to.R, factor),
            BlendChannel(from.G, to.G, factor),
            BlendChannel(from.B, to.B, factor));
    }

    private static byte BlendChannel(byte from, byte to, double factor)
    {
        return (byte)Math.Round(from + ((to - from) * factor));
    }

    private static double RelativeLuminance(Color color)
    {
        return (RedLuminanceWeight * Linearise(color.R))
            + (GreenLuminanceWeight * Linearise(color.G))
            + (BlueLuminanceWeight * Linearise(color.B));
    }

    private static double Linearise(byte channel)
    {
        double value = channel / MaxChannel;
        if (value <= SrgbLinearThreshold)
        {
            return value / SrgbLinearDivisor;
        }

        return Math.Pow((value + SrgbGammaOffset) / SrgbGammaDivisor, SrgbGammaExponent);
    }
}
