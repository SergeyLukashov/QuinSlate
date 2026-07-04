using System.Collections.Generic;

namespace QuinSlate.Ui.Layout;

/// <summary>
/// A fully-resolved layout of the emoji glyph sheet for one mode (browse or
/// search): section bands, per-cell origins, and the total sheet height.
/// Produced by <see cref="EmojiSheetLayoutCalculator"/>; contains no UI types
/// so it can be exercised in unit tests without a running UI.
/// </summary>
internal sealed class EmojiSheetLayout
{
    /// <summary>The section bands in top-to-bottom order.</summary>
    internal IReadOnlyList<EmojiSheetSection> Sections { get; }

    /// <summary>
    /// Cell origins in display order. In a browse layout the index is the
    /// global emoji entry index; in a search layout it is the match ordinal.
    /// </summary>
    internal IReadOnlyList<EmojiCellPosition> Cells { get; }

    /// <summary>The total height of the sheet content.</summary>
    internal double TotalHeight { get; }

    /// <summary>Creates a layout from computed sections, cells, and height.</summary>
    internal EmojiSheetLayout(IReadOnlyList<EmojiSheetSection> sections, IReadOnlyList<EmojiCellPosition> cells, double totalHeight)
    {
        Sections = sections;
        Cells = cells;
        TotalHeight = totalHeight;
    }
}
