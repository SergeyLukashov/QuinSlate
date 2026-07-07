using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using QuinSlate.Ui.Layout;
using QuinSlate.Ui.Models;
using Serilog;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace QuinSlate.Ui.Components;

/// <summary>
/// Owns the picker's static sprite sheet: one pre-rasterized emoji
/// <see cref="Image"/> per emoji plus one header <see cref="TextBlock"/> per
/// category, all created exactly once as direct canvas children and kept alive
/// for the app's lifetime. Search reuses those elements by repositioning
/// matches and collapsing the rest, so no UI element is ever created,
/// destroyed, or rebound after the initial build; scrolling the pre-built
/// sheet is pure composition work.
///
/// Sprite pixels come from <see cref="EmojiSpriteAtlas"/> (decoded once at
/// startup) and are assigned whenever the atlas (re)loads. Because drawing a
/// cached bitmap costs a fraction of first-time colour-glyph rasterization,
/// every transition — the initial reveal, each search keystroke, and each
/// return to browse — applies in a single frame with no paced placement.
/// </summary>
internal sealed class EmojiSheetPresenter
{
    private readonly Canvas canvas;
    private readonly Style headerStyle;
    private readonly EmojiCanvasInteraction interaction;
    private readonly EmojiSpriteAtlas atlas;
    private readonly IReadOnlyList<EmojiGroup> groups;
    private readonly IReadOnlyList<EmojiEntry> allEntries;

    private readonly List<Image> spriteImages = new List<Image>();
    private readonly List<TextBlock> headerBlocks = new List<TextBlock>();

    private EmojiSheetLayout browseLayout;
    private IReadOnlyList<int> currentMatchIndices;
    private bool isBuilt;
    private bool isShowingBrowse;

    /// <summary>Raised when the user picks an emoji from the sheet. The argument is the emoji string.</summary>
    internal event EventHandler<string> EmojiChosen;

    /// <summary>
    /// The emoji of the first current match, or null in browse mode or when
    /// nothing matches. Used for Enter-to-pick in the search box.
    /// </summary>
    internal string FirstMatchEmoji =>
        currentMatchIndices != null && currentMatchIndices.Count > 0
            ? allEntries[currentMatchIndices[0]].Emoji
            : null;

    /// <summary>
    /// Prepares the presenter over <paramref name="canvas"/>. The highlight
    /// borders must already be canvas children so the sprites added later by
    /// <see cref="Build"/> render above them.
    /// </summary>
    internal EmojiSheetPresenter(Canvas canvas, Border hoverHighlight, Border pressedHighlight, Style headerStyle, EmojiSpriteAtlas atlas)
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
        this.headerStyle = headerStyle;
        this.atlas = atlas;
        groups = EmojiData.GetGroups();
        allEntries = EmojiData.GetAllEntries();

        interaction = new EmojiCanvasInteraction(canvas, hoverHighlight, pressedHighlight);
        interaction.CellTapped += OnCellTapped;

        atlas.SpritesReady += OnSpritesReady;
    }

    /// <summary>
    /// Builds the entire sheet once: creates every header and sprite element
    /// in the browse layout, fully visible. Sprite sources are assigned from
    /// the atlas when available, or later via <see cref="EmojiSpriteAtlas.SpritesReady"/>.
    /// Subsequent calls are no-ops, which makes prewarming and open-time
    /// building race-safe.
    /// </summary>
    internal void Build()
    {
        if (isBuilt)
        {
            return;
        }

        var stopwatch = Stopwatch.StartNew();

        var groupCounts = new int[groups.Count];
        for (int i = 0; i < groups.Count; i++)
        {
            groupCounts[i] = groups[i].Entries.Count;
        }

        browseLayout = EmojiSheetLayoutCalculator.ComputeBrowseLayout(groupCounts);

        for (int i = 0; i < groups.Count; i++)
        {
            var header = new TextBlock
            {
                Text = groups[i].Name,
                Style = headerStyle,
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(header, 0);
            Canvas.SetTop(header, browseLayout.Sections[i].HeaderTop);
            canvas.Children.Add(header);
            headerBlocks.Add(header);
        }

        for (int i = 0; i < allEntries.Count; i++)
        {
            Image sprite = EmojiSpriteFactory.CreateSprite();
            sprite.Source = atlas.GetSprite(i);
            EmojiSpriteFactory.PlaceSprite(sprite, browseLayout.Cells[i]);
            canvas.Children.Add(sprite);
            spriteImages.Add(sprite);
        }

        canvas.Height = browseLayout.TotalHeight;
        interaction.SetLayout(browseLayout);
        currentMatchIndices = null;
        isShowingBrowse = true;
        isBuilt = true;

        Log.ForContext<EmojiSheetPresenter>().Information(
            "Emoji sheet built in {ElapsedMs:F1} ms: {SpriteCount} sprites, {HeaderCount} headers (atlas loaded: {AtlasLoaded}).",
            stopwatch.Elapsed.TotalMilliseconds,
            spriteImages.Count,
            headerBlocks.Count,
            atlas.IsLoaded);
    }

    /// <summary>
    /// Shows the grouped browse layout. A no-op when the sheet is already in
    /// browse mode, so reopening the picker does no work. Creates nothing.
    /// </summary>
    internal void ShowBrowse()
    {
        Build();

        if (isShowingBrowse)
        {
            return;
        }

        for (int i = 0; i < spriteImages.Count; i++)
        {
            EmojiSpriteFactory.PlaceSprite(spriteImages[i], browseLayout.Cells[i]);
            spriteImages[i].Visibility = Visibility.Visible;
        }

        foreach (TextBlock header in headerBlocks)
        {
            header.Visibility = Visibility.Visible;
        }

        canvas.Height = browseLayout.TotalHeight;
        interaction.SetLayout(browseLayout);
        currentMatchIndices = null;
        isShowingBrowse = true;
    }

    /// <summary>
    /// Applies a search query: matching sprites are repositioned into a
    /// compact grid at the top of the sheet and everything else is collapsed,
    /// including the category headers. Creates nothing. Returns the match count.
    /// </summary>
    internal int ShowMatches(string query)
    {
        Build();
        isShowingBrowse = false;

        IReadOnlyList<int> matches = EmojiSearch.FindMatchIndices(allEntries, query);
        EmojiSheetLayout layout = EmojiSheetLayoutCalculator.ComputeSearchLayout(matches.Count);

        int nextMatch = 0;
        for (int i = 0; i < spriteImages.Count; i++)
        {
            if (nextMatch < matches.Count && matches[nextMatch] == i)
            {
                EmojiSpriteFactory.PlaceSprite(spriteImages[i], layout.Cells[nextMatch]);
                spriteImages[i].Visibility = Visibility.Visible;
                nextMatch++;
            }
            else
            {
                spriteImages[i].Visibility = Visibility.Collapsed;
            }
        }

        foreach (TextBlock header in headerBlocks)
        {
            header.Visibility = Visibility.Collapsed;
        }

        canvas.Height = layout.TotalHeight;
        interaction.SetLayout(layout);
        currentMatchIndices = matches;
        return matches.Count;
    }

    private void OnSpritesReady(object sender, EventArgs e)
    {
        if (!isBuilt)
        {
            return;
        }

        for (int i = 0; i < spriteImages.Count; i++)
        {
            spriteImages[i].Source = atlas.GetSprite(i);
        }
    }

    private void OnCellTapped(object sender, int cellIndex)
    {
        int entryIndex = cellIndex;
        if (currentMatchIndices != null)
        {
            if (cellIndex >= currentMatchIndices.Count)
            {
                return;
            }

            entryIndex = currentMatchIndices[cellIndex];
        }

        if (entryIndex < 0 || entryIndex >= allEntries.Count)
        {
            return;
        }

        string emoji = allEntries[entryIndex].Emoji;
        if (string.IsNullOrEmpty(emoji))
        {
            return;
        }

        EmojiChosen?.Invoke(this, emoji);
    }
}
