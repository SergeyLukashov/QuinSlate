namespace QuinSlate.Ui.Layout;

/// <summary>
/// The top-left corner of one emoji cell on the glyph sheet, in DIPs.
/// The cell spans <see cref="EmojiSheetLayoutCalculator.CellSize"/> square;
/// the clickable item area is inset by <see cref="EmojiSheetLayoutCalculator.ItemMargin"/>.
/// </summary>
internal readonly struct EmojiCellPosition
{
    /// <summary>The X coordinate of the cell's left edge.</summary>
    internal double X { get; }

    /// <summary>The Y coordinate of the cell's top edge.</summary>
    internal double Y { get; }

    /// <summary>Creates a cell position at the given sheet coordinates.</summary>
    internal EmojiCellPosition(double x, double y)
    {
        X = x;
        Y = y;
    }
}
