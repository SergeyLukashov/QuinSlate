namespace QuinSlate.Ui.Layout;

/// <summary>
/// One vertical band of the glyph sheet: an optional category header followed
/// by a block of emoji rows. Browse layouts have one section per emoji group;
/// search layouts have a single header-less section (its <see cref="HeaderTop"/>
/// equals <see cref="RowsTop"/> and is unused).
/// </summary>
internal sealed class EmojiSheetSection
{
    /// <summary>The Y coordinate at which the section's header text is placed.</summary>
    internal double HeaderTop { get; }

    /// <summary>The Y coordinate of the first emoji row's cell top.</summary>
    internal double RowsTop { get; }

    /// <summary>Index into <see cref="EmojiSheetLayout.Cells"/> of this section's first cell.</summary>
    internal int FirstCellIndex { get; }

    /// <summary>Number of populated cells in this section.</summary>
    internal int CellCount { get; }

    /// <summary>Number of rows the cells occupy.</summary>
    internal int RowCount { get; }

    /// <summary>The Y coordinate just below the section's last row.</summary>
    internal double RowsBottom => RowsTop + RowCount * EmojiSheetLayoutCalculator.CellSize;

    /// <summary>Creates a section descriptor with fully-resolved coordinates.</summary>
    internal EmojiSheetSection(double headerTop, double rowsTop, int firstCellIndex, int cellCount, int rowCount)
    {
        HeaderTop = headerTop;
        RowsTop = rowsTop;
        FirstCellIndex = firstCellIndex;
        CellCount = cellCount;
        RowCount = rowCount;
    }
}
