using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using QuinSlate.Ui.Layout;
using Windows.Foundation;

namespace QuinSlate.Ui.Components;

/// <summary>
/// Creates and positions the plain emoji <see cref="TextBlock"/> glyphs used
/// by the picker's canvas surfaces. Glyphs are measured once so repositioning
/// them later is pure coordinate arithmetic with no text re-layout.
/// </summary>
internal static class EmojiGlyphFactory
{
    /// <summary>Emoji glyph font size (mirrors the old EmojiFontSize XAML resource).</summary>
    internal const double GlyphFontSize = 19.8;

    /// <summary>
    /// Creates a measured, non-hit-testable emoji glyph. Pointer events are
    /// handled by the owning canvas, so glyphs must never intercept them.
    /// </summary>
    internal static TextBlock CreateGlyph(string emoji)
    {
        var glyph = new TextBlock
        {
            Text = emoji,
            FontSize = GlyphFontSize,
            TextLineBounds = TextLineBounds.Tight,
            IsHitTestVisible = false,
        };

        MeasureGlyph(glyph);
        return glyph;
    }

    /// <summary>
    /// Forces text layout so <see cref="UIElement.DesiredSize"/> is valid for
    /// centering. Must be called again whenever the glyph's text changes, and
    /// only while the glyph is visible: measuring a collapsed element yields a
    /// zero desired size. Callers must capture the resulting size immediately
    /// if the glyph may be collapsed later.
    /// </summary>
    internal static void MeasureGlyph(TextBlock glyph)
    {
        glyph.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
    }

    /// <summary>
    /// Positions a glyph centered inside the item area of the given cell,
    /// replicating the old GridView item's centered content alignment.
    /// <paramref name="measuredSize"/> is the size captured from a measure
    /// taken while the glyph was visible; live <see cref="UIElement.DesiredSize"/>
    /// reads are unreliable here because collapsed glyphs report zero.
    /// </summary>
    internal static void PlaceGlyph(TextBlock glyph, EmojiCellPosition cell, Size measuredSize)
    {
        double left = cell.X + EmojiSheetLayoutCalculator.ItemMargin
            + (EmojiSheetLayoutCalculator.ItemSize - measuredSize.Width) / 2;
        double top = cell.Y + EmojiSheetLayoutCalculator.ItemMargin
            + (EmojiSheetLayoutCalculator.ItemSize - measuredSize.Height) / 2;

        Canvas.SetLeft(glyph, left);
        Canvas.SetTop(glyph, top);
    }
}
