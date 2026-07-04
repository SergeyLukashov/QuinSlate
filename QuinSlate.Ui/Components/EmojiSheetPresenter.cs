using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using QuinSlate.Ui.Layout;
using QuinSlate.Ui.Models;
using Serilog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Windows.Foundation;

namespace QuinSlate.Ui.Components;

/// <summary>
/// Owns the picker's static glyph sheet: one plain <see cref="TextBlock"/> per
/// emoji plus one per category header, all created exactly once as direct
/// canvas children and kept alive for the app's lifetime. Search reuses those
/// elements by repositioning matches and collapsing the rest, so no UI element
/// is ever created, destroyed, or rebound after the initial build; scrolling
/// the pre-built sheet is pure composition work.
///
/// Because a non-virtualized ScrollViewer re-renders its full content extent,
/// making hundreds of glyphs appear (or move) in one frame stalls the render
/// thread. Every transition — the initial reveal, each search keystroke, and
/// each return to browse — therefore applies glyph visibility in bounded
/// per-frame slices ordered viewport-window first: the visible region fills
/// within the first frames and the off-screen remainder streams in behind it.
/// With the glyph cache warmed (see <see cref="EmojiGlyphCacheWarmer"/>) a
/// slice draws in well under a frame, making transitions perceptually instant.
/// </summary>
internal sealed class EmojiSheetPresenter
{
    private const double MillisecondsPerSecond = 1000;

    private readonly Canvas canvas;
    private readonly Style headerStyle;
    private readonly EmojiCanvasInteraction interaction;
    private readonly IReadOnlyList<EmojiGroup> groups;
    private readonly IReadOnlyList<EmojiEntry> allEntries;

    private readonly List<TextBlock> glyphBlocks = new List<TextBlock>();
    private readonly List<TextBlock> headerBlocks = new List<TextBlock>();

    private EmojiSheetLayout browseLayout;
    private IReadOnlyList<int> currentMatchIndices;
    private bool isBuilt;
    private bool isBrowseFullyVisible;

    private Size[] glyphSizes;
    private List<int> pendingGlyphOrder;
    private int pendingApplied;
    private bool isPacing;
    private bool hasLoggedInitialReveal;
    private readonly Stopwatch pacingStopwatch = new Stopwatch();
    private double maxPacingFrameDeltaMs;
    private long lastPacingFrameTimestamp;
    private bool hasPacingFrameTimestamp;

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
    /// borders must already be canvas children so the glyphs added later by
    /// <see cref="Build"/> render above them.
    /// </summary>
    internal EmojiSheetPresenter(Canvas canvas, Border hoverHighlight, Border pressedHighlight, Style headerStyle)
    {
        if (canvas == null)
        {
            throw new ArgumentNullException(nameof(canvas));
        }

        this.canvas = canvas;
        this.headerStyle = headerStyle;
        groups = EmojiData.GetGroups();
        allEntries = EmojiData.GetAllEntries();

        interaction = new EmojiCanvasInteraction(canvas, hoverHighlight, pressedHighlight);
        interaction.CellTapped += OnCellTapped;

        canvas.Loaded += OnCanvasLoaded;
        canvas.Unloaded += OnCanvasUnloaded;
    }

    /// <summary>
    /// Builds the entire sheet once: creates and measures every header and
    /// glyph TextBlock. Each glyph's measured size is captured while it is
    /// still visible (collapsed elements measure to zero) and the glyph is
    /// then collapsed; glyphs become visible through the paced placement of
    /// the first <see cref="ShowBrowse"/>. Subsequent calls are no-ops, which
    /// makes prewarming and open-time building race-safe.
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

        glyphSizes = new Size[allEntries.Count];

        for (int i = 0; i < allEntries.Count; i++)
        {
            TextBlock glyph = EmojiGlyphFactory.CreateGlyph(allEntries[i].Emoji);
            glyphSizes[i] = glyph.DesiredSize;
            EmojiGlyphFactory.PlaceGlyph(glyph, browseLayout.Cells[i], glyphSizes[i]);
            glyph.Visibility = Visibility.Collapsed;
            canvas.Children.Add(glyph);
            glyphBlocks.Add(glyph);
        }

        canvas.Height = browseLayout.TotalHeight;
        interaction.SetLayout(browseLayout);
        currentMatchIndices = null;
        isBuilt = true;

        Log.ForContext<EmojiSheetPresenter>().Information(
            "Emoji sheet built in {ElapsedMs:F1} ms: {GlyphCount} glyphs, {HeaderCount} headers.",
            stopwatch.Elapsed.TotalMilliseconds,
            glyphBlocks.Count,
            headerBlocks.Count);
    }

    /// <summary>
    /// Shows the grouped browse layout. Cells intersecting the viewport
    /// window at <paramref name="viewportTop"/> become visible synchronously
    /// (no blank frame); the off-screen remainder streams in via the paced
    /// per-frame placement. A no-op when the sheet is already fully visible
    /// in browse mode, so reopening the picker never re-paces. Creates
    /// nothing.
    /// </summary>
    /// <param name="viewportTop">Current or restored scroll offset of the sheet.</param>
    internal void ShowBrowse(double viewportTop)
    {
        Build();

        if (isBrowseFullyVisible && currentMatchIndices == null)
        {
            return;
        }

        StopPacing();
        isBrowseFullyVisible = false;

        for (int i = 0; i < glyphBlocks.Count; i++)
        {
            EmojiGlyphFactory.PlaceGlyph(glyphBlocks[i], browseLayout.Cells[i], glyphSizes[i]);
            glyphBlocks[i].Visibility = Visibility.Collapsed;
        }

        foreach (TextBlock header in headerBlocks)
        {
            header.Visibility = Visibility.Visible;
        }

        canvas.Height = browseLayout.TotalHeight;
        interaction.SetLayout(browseLayout);
        currentMatchIndices = null;

        BeginPacedPlacement(browseLayout.Cells, null, viewportTop);
    }

    /// <summary>
    /// Applies a search query: matching glyphs are repositioned into a compact
    /// grid at the top of the sheet and everything else is collapsed,
    /// including the category headers. Matches become visible through the
    /// paced per-frame placement, top of the results first. Creates nothing.
    /// Returns the match count.
    /// </summary>
    internal int ShowMatches(string query)
    {
        Build();
        StopPacing();
        isBrowseFullyVisible = false;

        IReadOnlyList<int> matches = EmojiSearch.FindMatchIndices(allEntries, query);
        EmojiSheetLayout layout = EmojiSheetLayoutCalculator.ComputeSearchLayout(matches.Count);

        int nextMatch = 0;
        for (int i = 0; i < glyphBlocks.Count; i++)
        {
            if (nextMatch < matches.Count && matches[nextMatch] == i)
            {
                EmojiGlyphFactory.PlaceGlyph(glyphBlocks[i], layout.Cells[nextMatch], glyphSizes[i]);
                nextMatch++;
            }

            glyphBlocks[i].Visibility = Visibility.Collapsed;
        }

        foreach (TextBlock header in headerBlocks)
        {
            header.Visibility = Visibility.Collapsed;
        }

        canvas.Height = layout.TotalHeight;
        interaction.SetLayout(layout);
        currentMatchIndices = matches;

        BeginPacedPlacement(layout.Cells, matches, 0);
        return matches.Count;
    }

    /// <summary>
    /// Applies the current transition's visibility: cells inside the viewport
    /// window synchronously (so the visible region never blanks), the rest
    /// queued for the paced per-frame placement. <paramref name="cellToGlyph"/>
    /// maps cell index to glyph index (null for browse mode, where they are
    /// identical).
    /// </summary>
    private void BeginPacedPlacement(IReadOnlyList<EmojiCellPosition> cells, IReadOnlyList<int> cellToGlyph, double viewportTop)
    {
        IReadOnlyList<IReadOnlyList<int>> slices = EmojiSheetRevealPlanner.PlanSlices(
            cells,
            viewportTop,
            EmojiSheetLayoutCalculator.ScrollAreaHeight,
            EmojiSheetRevealPlanner.DefaultSliceSize);

        pendingGlyphOrder = new List<int>(cells.Count);
        foreach (IReadOnlyList<int> slice in slices)
        {
            foreach (int cellIndex in slice)
            {
                pendingGlyphOrder.Add(cellToGlyph == null ? cellIndex : cellToGlyph[cellIndex]);
            }
        }

        // Sync application is capped at one slice: applying the whole viewport
        // window in one frame costs a visible ~35 ms hitch per keystroke,
        // while the capped remainder lands one frame later — imperceptible.
        int syncCount = Math.Min(
            Math.Min(
                EmojiSheetRevealPlanner.CountWindowCells(cells, viewportTop, EmojiSheetLayoutCalculator.ScrollAreaHeight),
                EmojiSheetRevealPlanner.DefaultSliceSize),
            pendingGlyphOrder.Count);

        for (int i = 0; i < syncCount; i++)
        {
            glyphBlocks[pendingGlyphOrder[i]].Visibility = Visibility.Visible;
        }

        pendingApplied = syncCount;

        if (IsPlacementComplete)
        {
            CompletePlacement();
            return;
        }

        StartPacingIfNeeded();
    }

    private void CompletePlacement()
    {
        StopPacing();
        if (currentMatchIndices == null)
        {
            isBrowseFullyVisible = true;
        }

        LogPlacementComplete();
    }

    private bool IsPlacementComplete =>
        pendingGlyphOrder == null || pendingApplied >= pendingGlyphOrder.Count;

    private void OnCanvasLoaded(object sender, RoutedEventArgs e)
    {
        StartPacingIfNeeded();
    }

    private void OnCanvasUnloaded(object sender, RoutedEventArgs e)
    {
        StopPacing();
    }

    private void StartPacingIfNeeded()
    {
        if (!isBuilt || isPacing || IsPlacementComplete || !canvas.IsLoaded)
        {
            return;
        }

        isPacing = true;
        hasPacingFrameTimestamp = false;
        pacingStopwatch.Restart();
        maxPacingFrameDeltaMs = 0;
        CompositionTarget.Rendering += OnPacingFrameRendering;
    }

    private void StopPacing()
    {
        if (!isPacing)
        {
            return;
        }

        isPacing = false;
        pacingStopwatch.Stop();
        CompositionTarget.Rendering -= OnPacingFrameRendering;
    }

    /// <summary>
    /// Applies exactly one slice of pending visibility per presented frame so
    /// no single frame draws more than
    /// <see cref="EmojiSheetRevealPlanner.DefaultSliceSize"/> fresh glyphs.
    /// </summary>
    private void OnPacingFrameRendering(object sender, object e)
    {
        TrackPacingFrameDelta();

        if (IsPlacementComplete)
        {
            StopPacing();
            return;
        }

        int end = Math.Min(pendingApplied + EmojiSheetRevealPlanner.DefaultSliceSize, pendingGlyphOrder.Count);
        for (int i = pendingApplied; i < end; i++)
        {
            glyphBlocks[pendingGlyphOrder[i]].Visibility = Visibility.Visible;
        }

        pendingApplied = end;

        if (IsPlacementComplete)
        {
            CompletePlacement();
        }
    }

    private void LogPlacementComplete()
    {
        if (!hasLoggedInitialReveal)
        {
            hasLoggedInitialReveal = true;
            Log.ForContext<EmojiSheetPresenter>().Information(
                "Emoji sheet initial reveal completed in {ElapsedMs:F1} ms: {GlyphCount} glyphs, max frame delta {MaxFrameDeltaMs:F1} ms.",
                pacingStopwatch.Elapsed.TotalMilliseconds,
                pendingGlyphOrder.Count,
                maxPacingFrameDeltaMs);
            return;
        }

        Log.ForContext<EmojiSheetPresenter>().Debug(
            "Emoji sheet placement settled in {ElapsedMs:F1} ms: {GlyphCount} glyphs, max frame delta {MaxFrameDeltaMs:F1} ms.",
            pacingStopwatch.Elapsed.TotalMilliseconds,
            pendingGlyphOrder.Count,
            maxPacingFrameDeltaMs);
    }

    private void TrackPacingFrameDelta()
    {
        long timestamp = Stopwatch.GetTimestamp();
        if (hasPacingFrameTimestamp)
        {
            double deltaMs = (timestamp - lastPacingFrameTimestamp) * MillisecondsPerSecond / Stopwatch.Frequency;
            if (deltaMs > maxPacingFrameDeltaMs)
            {
                maxPacingFrameDeltaMs = deltaMs;
            }
        }

        lastPacingFrameTimestamp = timestamp;
        hasPacingFrameTimestamp = true;
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
