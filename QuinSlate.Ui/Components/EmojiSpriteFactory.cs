using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using QuinSlate.Ui.Layout;
using QuinSlate.Ui.Models;

namespace QuinSlate.Ui.Components;

/// <summary>
/// Creates and positions the pre-rasterized emoji sprite <see cref="Image"/>
/// elements used by the picker's canvas surfaces. Every sprite is a fixed
/// <see cref="EmojiAtlasFormat.SpriteLogicalSize"/> square whose source comes
/// from <see cref="EmojiSpriteAtlas"/>, so positioning is pure coordinate
/// arithmetic with no measuring.
/// </summary>
internal static class EmojiSpriteFactory
{
    /// <summary>
    /// Creates a non-hit-testable emoji sprite image. Pointer events are
    /// handled by the owning canvas, so sprites must never intercept them.
    /// The source is assigned separately once the atlas is decoded.
    /// </summary>
    internal static Image CreateSprite()
    {
        return new Image
        {
            Width = EmojiAtlasFormat.SpriteLogicalSize,
            Height = EmojiAtlasFormat.SpriteLogicalSize,
            Stretch = Stretch.Fill,
            IsHitTestVisible = false,
        };
    }

    /// <summary>
    /// Positions a sprite centred inside the item area of the given cell,
    /// replicating how the old TextBlock glyphs centred inside their cells.
    /// </summary>
    internal static void PlaceSprite(Image sprite, EmojiCellPosition cell)
    {
        double inset = EmojiSheetLayoutCalculator.ItemMargin
            + (EmojiSheetLayoutCalculator.ItemSize - EmojiAtlasFormat.SpriteLogicalSize) / 2;

        Canvas.SetLeft(sprite, cell.X + inset);
        Canvas.SetTop(sprite, cell.Y + inset);
    }
}
