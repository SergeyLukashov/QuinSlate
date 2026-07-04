using System.Collections.Generic;

namespace QuinSlate.Ui.Layout;

/// <summary>
/// Pure planning for the glyph sheet's paced placement. Rendering many glyph
/// visuals in one presented frame stalls the render thread (a non-virtualized
/// ScrollViewer re-renders its full content extent), so visibility changes are
/// applied in bounded per-frame slices instead. This planner decides the order
/// (cells intersecting the given viewport window first, then the remainder in
/// layout order) and partitions it into fixed-size slices. Stateless and free
/// of UI types so it can be exercised in unit tests without a running UI.
/// </summary>
internal static class EmojiSheetRevealPlanner
{
    /// <summary>
    /// Glyphs revealed per presented frame: four full rows. Small enough that
    /// one slice's drawing fits comfortably in a frame budget once the glyph
    /// cache is warm, large enough that a full transition settles in well
    /// under a second at 60 Hz.
    /// </summary>
    internal const int DefaultSliceSize = 28;

    /// <summary>
    /// Plans a paced placement: cell indices whose vertical extent intersects
    /// the window <c>[viewportTop, viewportTop + viewportHeight)</c> come
    /// first so the visible region fills immediately, then all remaining
    /// indices; both partitions preserve layout order. The order is
    /// partitioned into slices of <paramref name="sliceSize"/> (the last slice
    /// may be shorter). A null or empty cell list yields no slices; a
    /// non-positive slice size is treated as one glyph per slice.
    /// </summary>
    /// <param name="cells">Cell origins of every glyph, in layout order.</param>
    /// <param name="viewportTop">Top of the currently visible sheet region.</param>
    /// <param name="viewportHeight">Height of the visible sheet region.</param>
    /// <param name="sliceSize">Maximum glyphs revealed per frame.</param>
    internal static IReadOnlyList<IReadOnlyList<int>> PlanSlices(
        IReadOnlyList<EmojiCellPosition> cells, double viewportTop, double viewportHeight, int sliceSize)
    {
        var slices = new List<IReadOnlyList<int>>();
        if (cells == null || cells.Count == 0)
        {
            return slices;
        }

        int boundedSliceSize = sliceSize < 1 ? 1 : sliceSize;

        var order = new List<int>(cells.Count);
        for (int i = 0; i < cells.Count; i++)
        {
            if (IsInWindow(cells[i], viewportTop, viewportHeight))
            {
                order.Add(i);
            }
        }

        for (int i = 0; i < cells.Count; i++)
        {
            if (!IsInWindow(cells[i], viewportTop, viewportHeight))
            {
                order.Add(i);
            }
        }

        for (int start = 0; start < order.Count; start += boundedSliceSize)
        {
            int length = order.Count - start < boundedSliceSize ? order.Count - start : boundedSliceSize;
            var slice = new int[length];
            order.CopyTo(start, slice, 0, length);
            slices.Add(slice);
        }

        return slices;
    }

    /// <summary>
    /// Counts the cells whose vertical extent intersects the window
    /// <c>[viewportTop, viewportTop + viewportHeight)</c> — the cells
    /// <see cref="PlanSlices"/> orders first. Callers apply exactly these
    /// synchronously so the visible region never shows a blank frame, and
    /// pace the rest.
    /// </summary>
    internal static int CountWindowCells(IReadOnlyList<EmojiCellPosition> cells, double viewportTop, double viewportHeight)
    {
        if (cells == null)
        {
            return 0;
        }

        int count = 0;
        for (int i = 0; i < cells.Count; i++)
        {
            if (IsInWindow(cells[i], viewportTop, viewportHeight))
            {
                count++;
            }
        }

        return count;
    }

    private static bool IsInWindow(EmojiCellPosition cell, double viewportTop, double viewportHeight)
    {
        return cell.Y + EmojiSheetLayoutCalculator.CellSize > viewportTop
            && cell.Y < viewportTop + viewportHeight;
    }
}
