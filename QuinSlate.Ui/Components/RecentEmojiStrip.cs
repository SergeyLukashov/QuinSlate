using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using QuinSlate.Ui.Layout;
using System;
using System.Collections.Generic;

namespace QuinSlate.Ui.Components;

/// <summary>
/// The picker's single-row "Recent" strip: a small pool of reused sprite
/// <see cref="Image"/> elements on a fixed-height canvas. Refreshing with an
/// unchanged list is a no-op; otherwise pooled sprites are retargeted by
/// swapping their source, so at most a handful of elements ever exist.
/// Display is capped at one row (<see cref="EmojiSheetLayoutCalculator.Columns"/>
/// items), matching the old fixed-height GridView, which clipped any overflow.
/// A persisted recent emoji that is no longer in <see cref="Models.EmojiData"/>
/// has no sprite and is skipped.
/// </summary>
internal sealed class RecentEmojiStrip
{
    private readonly Canvas canvas;
    private readonly EmojiCanvasInteraction interaction;
    private readonly EmojiSpriteAtlas atlas;
    private readonly List<Image> spritePool = new List<Image>();
    private readonly List<string> currentRecents = new List<string>();
    private readonly List<int> currentSpriteIndices = new List<int>();

    /// <summary>Raised when the user picks a recent emoji. The argument is the emoji string.</summary>
    internal event EventHandler<string> EmojiChosen;

    /// <summary>
    /// Prepares the strip over <paramref name="canvas"/>. The highlight
    /// borders must already be canvas children so pooled sprites render above them.
    /// </summary>
    internal RecentEmojiStrip(Canvas canvas, Border hoverHighlight, Border pressedHighlight, EmojiSpriteAtlas atlas)
    {
        if (canvas == null)
        {
            throw new ArgumentNullException(nameof(canvas));
        }

        if (atlas == null)
        {
            throw new ArgumentNullException(nameof(atlas));
        }

        this.canvas = canvas;
        this.atlas = atlas;
        interaction = new EmojiCanvasInteraction(canvas, hoverHighlight, pressedHighlight);
        interaction.CellTapped += OnCellTapped;

        atlas.SpritesReady += OnSpritesReady;
    }

    /// <summary>
    /// Shows <paramref name="recentEmoji"/> in the strip, reusing the pooled
    /// sprite elements. Skips all work when the shown list is unchanged.
    /// Returns true when at least one recent emoji is shown.
    /// </summary>
    internal bool SetRecents(IReadOnlyList<string> recentEmoji)
    {
        var shown = new List<string>();
        var spriteIndices = new List<int>();
        if (recentEmoji != null)
        {
            for (int i = 0; i < recentEmoji.Count && shown.Count < EmojiSheetLayoutCalculator.Columns; i++)
            {
                int spriteIndex = atlas.IndexOf(recentEmoji[i]);
                if (spriteIndex >= 0)
                {
                    shown.Add(recentEmoji[i]);
                    spriteIndices.Add(spriteIndex);
                }
            }
        }

        if (SameAsCurrent(shown))
        {
            return currentRecents.Count > 0;
        }

        currentRecents.Clear();
        currentRecents.AddRange(shown);
        currentSpriteIndices.Clear();
        currentSpriteIndices.AddRange(spriteIndices);

        EmojiSheetLayout layout = EmojiSheetLayoutCalculator.ComputeSearchLayout(shown.Count);

        for (int i = 0; i < shown.Count; i++)
        {
            Image sprite;
            if (i < spritePool.Count)
            {
                sprite = spritePool[i];
                sprite.Visibility = Visibility.Visible;
            }
            else
            {
                sprite = EmojiSpriteFactory.CreateSprite();
                canvas.Children.Add(sprite);
                spritePool.Add(sprite);
            }

            sprite.Source = atlas.GetSprite(spriteIndices[i]);
            EmojiSpriteFactory.PlaceSprite(sprite, layout.Cells[i]);
        }

        for (int i = shown.Count; i < spritePool.Count; i++)
        {
            spritePool[i].Visibility = Visibility.Collapsed;
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

    private void OnSpritesReady(object sender, EventArgs e)
    {
        for (int i = 0; i < currentSpriteIndices.Count && i < spritePool.Count; i++)
        {
            spritePool[i].Source = atlas.GetSprite(currentSpriteIndices[i]);
        }
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
