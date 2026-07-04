using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using QuinSlate.Ui.Layout;
using System;
using System.Collections.Generic;

namespace QuinSlate.Ui.Components;

/// <summary>
/// The picker's single-row "Recent" strip: a small pool of reused glyph
/// TextBlocks on a fixed-height canvas. Refreshing with an unchanged list is
/// a no-op; otherwise pooled blocks are retargeted by setting their text, so
/// at most a handful of TextBlocks ever exist. Display is capped at one row
/// (<see cref="EmojiSheetLayoutCalculator.Columns"/> items), matching the old
/// fixed-height GridView, which clipped any overflow.
/// </summary>
internal sealed class RecentEmojiStrip
{
    private readonly Canvas canvas;
    private readonly EmojiCanvasInteraction interaction;
    private readonly List<TextBlock> glyphPool = new List<TextBlock>();
    private readonly List<string> currentRecents = new List<string>();

    /// <summary>Raised when the user picks a recent emoji. The argument is the emoji string.</summary>
    internal event EventHandler<string> EmojiChosen;

    /// <summary>
    /// Prepares the strip over <paramref name="canvas"/>. The highlight
    /// borders must already be canvas children so pooled glyphs render above them.
    /// </summary>
    internal RecentEmojiStrip(Canvas canvas, Border hoverHighlight, Border pressedHighlight)
    {
        if (canvas == null)
        {
            throw new ArgumentNullException(nameof(canvas));
        }

        this.canvas = canvas;
        interaction = new EmojiCanvasInteraction(canvas, hoverHighlight, pressedHighlight);
        interaction.CellTapped += OnCellTapped;
    }

    /// <summary>
    /// Shows <paramref name="recentEmoji"/> in the strip, reusing the pooled
    /// glyph blocks. Skips all work when the shown list is unchanged. Returns
    /// true when at least one recent emoji is shown.
    /// </summary>
    internal bool SetRecents(IReadOnlyList<string> recentEmoji)
    {
        var shown = new List<string>();
        if (recentEmoji != null)
        {
            for (int i = 0; i < recentEmoji.Count && shown.Count < EmojiSheetLayoutCalculator.Columns; i++)
            {
                shown.Add(recentEmoji[i]);
            }
        }

        if (SameAsCurrent(shown))
        {
            return currentRecents.Count > 0;
        }

        currentRecents.Clear();
        currentRecents.AddRange(shown);

        EmojiSheetLayout layout = EmojiSheetLayoutCalculator.ComputeSearchLayout(shown.Count);

        for (int i = 0; i < shown.Count; i++)
        {
            TextBlock glyph;
            if (i < glyphPool.Count)
            {
                // A pooled block may still be collapsed from a previous shorter
                // list, and a collapsed element measures to zero: make it
                // visible before re-measuring.
                glyph = glyphPool[i];
                glyph.Visibility = Visibility.Visible;
                glyph.Text = shown[i];
                EmojiGlyphFactory.MeasureGlyph(glyph);
            }
            else
            {
                glyph = EmojiGlyphFactory.CreateGlyph(shown[i]);
                canvas.Children.Add(glyph);
                glyphPool.Add(glyph);
            }

            EmojiGlyphFactory.PlaceGlyph(glyph, layout.Cells[i], glyph.DesiredSize);
        }

        for (int i = shown.Count; i < glyphPool.Count; i++)
        {
            glyphPool[i].Visibility = Visibility.Collapsed;
        }

        interaction.SetLayout(layout);
        return shown.Count > 0;
    }

    private bool SameAsCurrent(List<string> shown)
    {
        if (shown.Count != currentRecents.Count)
        {
            return false;
        }

        for (int i = 0; i < shown.Count; i++)
        {
            if (!string.Equals(shown[i], currentRecents[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private void OnCellTapped(object sender, int cellIndex)
    {
        if (cellIndex < 0 || cellIndex >= currentRecents.Count)
        {
            return;
        }

        string emoji = currentRecents[cellIndex];
        if (string.IsNullOrEmpty(emoji))
        {
            return;
        }

        EmojiChosen?.Invoke(this, emoji);
    }
}
