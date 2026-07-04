using System.Collections.Generic;

namespace QuinSlate.Ui.Layout;

/// <summary>
/// Pure layout calculations for the emoji picker's static glyph sheet.
/// Computes where every category header and emoji cell sits (browse mode),
/// where every match sits (search mode), and reverse hit-tests a point back
/// to a cell index. All methods are stateless and use plain numeric types so
/// they can be exercised in unit tests without a running UI.
/// </summary>
internal static class EmojiSheetLayoutCalculator
{
    /// <summary>Number of emoji columns (mirrors the 266px picker width in EmojiPickerView.xaml).</summary>
    internal const int Columns = 7;

    /// <summary>Full cell pitch: the 36px item plus a 1px margin on every side.</summary>
    internal const double CellSize = 38;

    /// <summary>Side length of the clickable/highlighted item area inside a cell.</summary>
    internal const double ItemSize = 36;

    /// <summary>Dead margin around each item inside its cell.</summary>
    internal const double ItemMargin = 1;

    /// <summary>Height of a category header text row (mirrors the old GridViewHeaderItem height).</summary>
    internal const double HeaderHeight = 15;

    /// <summary>Gap above each category header (mirrors the old header's top margin).</summary>
    internal const double HeaderTopGap = 4;

    /// <summary>Total sheet width: <see cref="Columns"/> × <see cref="CellSize"/> = 266.</summary>
    internal const double SheetWidth = Columns * CellSize;

    /// <summary>Height of the scrollable sheet viewport (mirrors the SheetScroller height in EmojiPickerView.xaml).</summary>
    internal const double ScrollAreaHeight = 240;

    /// <summary>Return value of <see cref="HitTest"/> when the point hits no emoji cell.</summary>
    internal const int NoHit = -1;

    /// <summary>
    /// Computes the browse-mode layout: one section per group, each with a
    /// header band followed by its entries in rows of <see cref="Columns"/>.
    /// A zero-count group still contributes its header band, matching the old
    /// grouped GridView, which showed headers for empty groups.
    /// </summary>
    /// <param name="groupCounts">Entry count of each group in display order.</param>
    internal static EmojiSheetLayout ComputeBrowseLayout(IReadOnlyList<int> groupCounts)
    {
        var sections = new List<EmojiSheetSection>();
        var cells = new List<EmojiCellPosition>();
        double y = 0;

        if (groupCounts == null)
        {
            return new EmojiSheetLayout(sections, cells, y);
        }

        int firstCellIndex = 0;
        foreach (int count in groupCounts)
        {
            int cellCount = count < 0 ? 0 : count;
            double headerTop = y + HeaderTopGap;
            double rowsTop = headerTop + HeaderHeight;
            int rowCount = (cellCount + Columns - 1) / Columns;

            for (int i = 0; i < cellCount; i++)
            {
                cells.Add(new EmojiCellPosition(i % Columns * CellSize, rowsTop + i / Columns * CellSize));
            }

            sections.Add(new EmojiSheetSection(headerTop, rowsTop, firstCellIndex, cellCount, rowCount));
            firstCellIndex += cellCount;
            y = rowsTop + rowCount * CellSize;
        }

        return new EmojiSheetLayout(sections, cells, y);
    }

    /// <summary>
    /// Computes the search-mode layout: a single header-less section whose
    /// rows start at the top of the sheet. The "Matches" caption lives outside
    /// the scrollable sheet, so no header band is reserved. A zero or negative
    /// count produces an empty layout.
    /// </summary>
    /// <param name="cellCount">Number of matches to lay out.</param>
    internal static EmojiSheetLayout ComputeSearchLayout(int cellCount)
    {
        var sections = new List<EmojiSheetSection>();
        var cells = new List<EmojiCellPosition>();

        if (cellCount <= 0)
        {
            return new EmojiSheetLayout(sections, cells, 0);
        }

        int rowCount = (cellCount + Columns - 1) / Columns;
        for (int i = 0; i < cellCount; i++)
        {
            cells.Add(new EmojiCellPosition(i % Columns * CellSize, i / Columns * CellSize));
        }

        sections.Add(new EmojiSheetSection(0, 0, 0, cellCount, rowCount));
        return new EmojiSheetLayout(sections, cells, rowCount * CellSize);
    }

    /// <summary>
    /// Maps a point in sheet coordinates back to the cell index it hits, or
    /// <see cref="NoHit"/> for header bands, inter-cell margins, trailing
    /// empty cells in a section's last row, and anything outside the sheet.
    /// Only the inset <see cref="ItemSize"/> square of a populated cell hits,
    /// matching the old GridViewItem bounds.
    /// </summary>
    /// <param name="layout">The layout currently applied to the sheet.</param>
    /// <param name="x">Point X in sheet coordinates.</param>
    /// <param name="y">Point Y in sheet coordinates.</param>
    internal static int HitTest(EmojiSheetLayout layout, double x, double y)
    {
        if (layout == null || x < 0 || x >= SheetWidth || y < 0)
        {
            return NoHit;
        }

        int column = (int)(x / CellSize);
        double xInCell = x - column * CellSize;
        if (xInCell < ItemMargin || xInCell >= CellSize - ItemMargin)
        {
            return NoHit;
        }

        foreach (EmojiSheetSection section in layout.Sections)
        {
            if (y < section.RowsTop || y >= section.RowsBottom)
            {
                continue;
            }

            int row = (int)((y - section.RowsTop) / CellSize);
            double yInCell = y - section.RowsTop - row * CellSize;
            if (yInCell < ItemMargin || yInCell >= CellSize - ItemMargin)
            {
                return NoHit;
            }

            int cellInSection = row * Columns + column;
            if (cellInSection >= section.CellCount)
            {
                return NoHit;
            }

            return section.FirstCellIndex + cellInSection;
        }

        return NoHit;
    }
}
