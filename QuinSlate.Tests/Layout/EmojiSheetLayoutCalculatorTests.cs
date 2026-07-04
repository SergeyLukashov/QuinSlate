using QuinSlate.Ui.Layout;

namespace QuinSlate.Tests.Layout;

public sealed class EmojiSheetLayoutCalculatorTests
{
    // Geometry cheat-sheet (all values in DIPs):
    //   header band = 4 gap + 15 header = 19; cell pitch = 38; item = 36 inset by 1.
    //   A group's rows start 19 below the previous group's bottom.

    // ── ComputeBrowseLayout ───────────────────────────────────────────────────

    [Fact]
    public void ComputeBrowseLayout_NullGroupCounts_ReturnsEmptyLayout()
    {
        EmojiSheetLayout layout = EmojiSheetLayoutCalculator.ComputeBrowseLayout(null);

        Assert.Empty(layout.Sections);
        Assert.Empty(layout.Cells);
        Assert.Equal(0, layout.TotalHeight);
    }

    [Fact]
    public void ComputeBrowseLayout_NoGroups_ReturnsEmptyLayout()
    {
        EmojiSheetLayout layout = EmojiSheetLayoutCalculator.ComputeBrowseLayout(new int[0]);

        Assert.Empty(layout.Sections);
        Assert.Empty(layout.Cells);
        Assert.Equal(0, layout.TotalHeight);
    }

    [Fact]
    public void ComputeBrowseLayout_SingleEntry_PlacesHeaderThenCell()
    {
        EmojiSheetLayout layout = EmojiSheetLayoutCalculator.ComputeBrowseLayout(new[] { 1 });

        EmojiSheetSection section = Assert.Single(layout.Sections);
        Assert.Equal(4, section.HeaderTop);
        Assert.Equal(19, section.RowsTop);
        Assert.Equal(0, section.FirstCellIndex);
        Assert.Equal(1, section.CellCount);
        Assert.Equal(1, section.RowCount);

        EmojiCellPosition cell = Assert.Single(layout.Cells);
        Assert.Equal(0, cell.X);
        Assert.Equal(19, cell.Y);
        Assert.Equal(57, layout.TotalHeight); // 19 + 38
    }

    [Fact]
    public void ComputeBrowseLayout_FullRow_LaysOutSevenColumnsInOneRow()
    {
        EmojiSheetLayout layout = EmojiSheetLayoutCalculator.ComputeBrowseLayout(new[] { 7 });

        Assert.Equal(7, layout.Cells.Count);
        for (int i = 0; i < 7; i++)
        {
            Assert.Equal(i * 38, layout.Cells[i].X);
            Assert.Equal(19, layout.Cells[i].Y);
        }

        Assert.Equal(1, layout.Sections[0].RowCount);
        Assert.Equal(57, layout.TotalHeight);
    }

    [Fact]
    public void ComputeBrowseLayout_EighthEntry_WrapsToSecondRow()
    {
        EmojiSheetLayout layout = EmojiSheetLayoutCalculator.ComputeBrowseLayout(new[] { 8 });

        Assert.Equal(0, layout.Cells[7].X);
        Assert.Equal(57, layout.Cells[7].Y); // 19 + 38
        Assert.Equal(2, layout.Sections[0].RowCount);
        Assert.Equal(95, layout.TotalHeight); // 19 + 2 * 38
    }

    [Fact]
    public void ComputeBrowseLayout_TwoGroups_StacksSecondGroupBelowFirst()
    {
        EmojiSheetLayout layout = EmojiSheetLayoutCalculator.ComputeBrowseLayout(new[] { 3, 4 });

        Assert.Equal(2, layout.Sections.Count);

        // Group 1: header 4, rows 19..57.
        Assert.Equal(4, layout.Sections[0].HeaderTop);
        Assert.Equal(57, layout.Sections[0].RowsBottom);

        // Group 2: header 61 (57 + 4), rows 76 (61 + 15).
        Assert.Equal(61, layout.Sections[1].HeaderTop);
        Assert.Equal(76, layout.Sections[1].RowsTop);
        Assert.Equal(3, layout.Sections[1].FirstCellIndex);
        Assert.Equal(4, layout.Sections[1].CellCount);

        Assert.Equal(0, layout.Cells[3].X);
        Assert.Equal(76, layout.Cells[3].Y);
        Assert.Equal(38, layout.Cells[4].X);
        Assert.Equal(76, layout.Cells[4].Y);

        Assert.Equal(114, layout.TotalHeight); // 76 + 38
    }

    [Fact]
    public void ComputeBrowseLayout_EmptyGroup_ContributesHeaderBandOnly()
    {
        EmojiSheetLayout layout = EmojiSheetLayoutCalculator.ComputeBrowseLayout(new[] { 2, 0, 3 });

        Assert.Equal(3, layout.Sections.Count);
        Assert.Equal(0, layout.Sections[1].RowCount);
        Assert.Equal(0, layout.Sections[1].CellCount);
        Assert.Equal(76, layout.Sections[1].RowsTop); // 57 + 4 + 15
        Assert.Equal(76, layout.Sections[1].RowsBottom);

        // Third group starts right after the empty group's header band.
        Assert.Equal(80, layout.Sections[2].HeaderTop);
        Assert.Equal(95, layout.Sections[2].RowsTop);
        Assert.Equal(2, layout.Sections[2].FirstCellIndex);
        Assert.Equal(95, layout.Cells[2].Y);
        Assert.Equal(133, layout.TotalHeight); // 95 + 38
    }

    [Fact]
    public void ComputeBrowseLayout_NegativeCount_TreatedAsEmptyGroup()
    {
        EmojiSheetLayout layout = EmojiSheetLayoutCalculator.ComputeBrowseLayout(new[] { -5 });

        EmojiSheetSection section = Assert.Single(layout.Sections);
        Assert.Equal(0, section.CellCount);
        Assert.Empty(layout.Cells);
        Assert.Equal(19, layout.TotalHeight);
    }

    [Fact]
    public void ComputeBrowseLayout_ExactColumnMultiple_HasNoTrailingRow()
    {
        EmojiSheetLayout layout = EmojiSheetLayoutCalculator.ComputeBrowseLayout(new[] { 14 });

        Assert.Equal(2, layout.Sections[0].RowCount);
        Assert.Equal(228, layout.Cells[13].X); // column 6
        Assert.Equal(57, layout.Cells[13].Y);  // row 1
        Assert.Equal(95, layout.TotalHeight);
    }

    // ── ComputeSearchLayout ───────────────────────────────────────────────────

    [Fact]
    public void ComputeSearchLayout_ZeroMatches_ReturnsEmptyLayout()
    {
        EmojiSheetLayout layout = EmojiSheetLayoutCalculator.ComputeSearchLayout(0);

        Assert.Empty(layout.Sections);
        Assert.Empty(layout.Cells);
        Assert.Equal(0, layout.TotalHeight);
    }

    [Fact]
    public void ComputeSearchLayout_NegativeCount_ReturnsEmptyLayout()
    {
        EmojiSheetLayout layout = EmojiSheetLayoutCalculator.ComputeSearchLayout(-3);

        Assert.Empty(layout.Sections);
        Assert.Empty(layout.Cells);
        Assert.Equal(0, layout.TotalHeight);
    }

    [Fact]
    public void ComputeSearchLayout_SingleMatch_StartsAtSheetTop()
    {
        EmojiSheetLayout layout = EmojiSheetLayoutCalculator.ComputeSearchLayout(1);

        EmojiSheetSection section = Assert.Single(layout.Sections);
        Assert.Equal(0, section.RowsTop);
        Assert.Equal(1, section.RowCount);

        Assert.Equal(0, layout.Cells[0].X);
        Assert.Equal(0, layout.Cells[0].Y);
        Assert.Equal(38, layout.TotalHeight);
    }

    [Fact]
    public void ComputeSearchLayout_EightMatches_WrapsToSecondRow()
    {
        EmojiSheetLayout layout = EmojiSheetLayoutCalculator.ComputeSearchLayout(8);

        Assert.Equal(228, layout.Cells[6].X);
        Assert.Equal(0, layout.Cells[6].Y);
        Assert.Equal(0, layout.Cells[7].X);
        Assert.Equal(38, layout.Cells[7].Y);
        Assert.Equal(76, layout.TotalHeight);
    }

    // ── HitTest ───────────────────────────────────────────────────────────────

    private static EmojiSheetLayout BrowseSevenAndThree()
    {
        // Group 1: 7 entries, rows 19..57. Group 2: 3 entries, header 61, rows 76..114.
        return EmojiSheetLayoutCalculator.ComputeBrowseLayout(new[] { 7, 3 });
    }

    [Fact]
    public void HitTest_NullLayout_ReturnsNoHit()
    {
        Assert.Equal(EmojiSheetLayoutCalculator.NoHit, EmojiSheetLayoutCalculator.HitTest(null, 5, 25));
    }

    [Fact]
    public void HitTest_FirstItem_ReturnsIndexZero()
    {
        Assert.Equal(0, EmojiSheetLayoutCalculator.HitTest(BrowseSevenAndThree(), 5, 25));
    }

    [Fact]
    public void HitTest_HeaderBand_ReturnsNoHit()
    {
        Assert.Equal(EmojiSheetLayoutCalculator.NoHit, EmojiSheetLayoutCalculator.HitTest(BrowseSevenAndThree(), 5, 10));
    }

    [Fact]
    public void HitTest_TopGapAboveFirstHeader_ReturnsNoHit()
    {
        Assert.Equal(EmojiSheetLayoutCalculator.NoHit, EmojiSheetLayoutCalculator.HitTest(BrowseSevenAndThree(), 5, 2));
    }

    [Fact]
    public void HitTest_GapBetweenGroups_ReturnsNoHit()
    {
        // y = 60 is between group 1's rows bottom (57) and group 2's rows top (76).
        Assert.Equal(EmojiSheetLayoutCalculator.NoHit, EmojiSheetLayoutCalculator.HitTest(BrowseSevenAndThree(), 5, 60));
    }

    [Fact]
    public void HitTest_SecondGroupFirstItem_ReturnsGlobalIndex()
    {
        Assert.Equal(7, EmojiSheetLayoutCalculator.HitTest(BrowseSevenAndThree(), 5, 80));
    }

    [Fact]
    public void HitTest_TrailingEmptyCellInLastRow_ReturnsNoHit()
    {
        // Group 2 has 3 entries; column 3 of its only row is empty.
        double x = 3 * 38 + 5;
        Assert.Equal(EmojiSheetLayoutCalculator.NoHit, EmojiSheetLayoutCalculator.HitTest(BrowseSevenAndThree(), x, 80));
    }

    [Fact]
    public void HitTest_LastPopulatedCellOfExactColumnMultipleGroup_Hits()
    {
        EmojiSheetLayout layout = EmojiSheetLayoutCalculator.ComputeBrowseLayout(new[] { 14 });
        Assert.Equal(13, EmojiSheetLayoutCalculator.HitTest(layout, 230, 59));
    }

    [Fact]
    public void HitTest_BelowSheetBottom_ReturnsNoHit()
    {
        Assert.Equal(EmojiSheetLayoutCalculator.NoHit, EmojiSheetLayoutCalculator.HitTest(BrowseSevenAndThree(), 5, 114));
    }

    [Theory]
    [InlineData(-1, 25)]
    [InlineData(5, -1)]
    [InlineData(266, 25)]
    [InlineData(500, 25)]
    public void HitTest_OutsideSheetBounds_ReturnsNoHit(double x, double y)
    {
        Assert.Equal(EmojiSheetLayoutCalculator.NoHit, EmojiSheetLayoutCalculator.HitTest(BrowseSevenAndThree(), x, y));
    }

    [Theory]
    [InlineData(0.5)]  // left margin of column 0
    [InlineData(37.0)] // right margin of column 0
    [InlineData(37.5)] // gap between columns 0 and 1
    [InlineData(38.0)] // left margin of column 1
    [InlineData(265.5)] // right margin of the last column
    public void HitTest_HorizontalCellMargins_ReturnNoHit(double x)
    {
        Assert.Equal(EmojiSheetLayoutCalculator.NoHit, EmojiSheetLayoutCalculator.HitTest(BrowseSevenAndThree(), x, 25));
    }

    [Fact]
    public void HitTest_JustInsideLeftItemEdge_Hits()
    {
        Assert.Equal(1, EmojiSheetLayoutCalculator.HitTest(BrowseSevenAndThree(), 39, 25));
    }

    [Theory]
    [InlineData(19.5)] // top margin of row 0
    [InlineData(56.0)] // bottom margin of row 0
    public void HitTest_VerticalCellMargins_ReturnNoHit(double y)
    {
        Assert.Equal(EmojiSheetLayoutCalculator.NoHit, EmojiSheetLayoutCalculator.HitTest(BrowseSevenAndThree(), 5, y));
    }

    [Fact]
    public void HitTest_EmptySearchLayout_ReturnsNoHitEverywhere()
    {
        EmojiSheetLayout layout = EmojiSheetLayoutCalculator.ComputeSearchLayout(0);
        Assert.Equal(EmojiSheetLayoutCalculator.NoHit, EmojiSheetLayoutCalculator.HitTest(layout, 5, 5));
    }

    [Fact]
    public void HitTest_SearchLayoutFirstCell_ReturnsMatchOrdinalZero()
    {
        EmojiSheetLayout layout = EmojiSheetLayoutCalculator.ComputeSearchLayout(8);
        Assert.Equal(0, EmojiSheetLayoutCalculator.HitTest(layout, 5, 5));
    }

    [Fact]
    public void HitTest_SearchLayoutSecondRow_ReturnsMatchOrdinalSeven()
    {
        EmojiSheetLayout layout = EmojiSheetLayoutCalculator.ComputeSearchLayout(8);
        Assert.Equal(7, EmojiSheetLayoutCalculator.HitTest(layout, 5, 43));
    }

    [Fact]
    public void HitTest_SearchLayoutTrailingEmptyCell_ReturnsNoHit()
    {
        EmojiSheetLayout layout = EmojiSheetLayoutCalculator.ComputeSearchLayout(8);
        Assert.Equal(EmojiSheetLayoutCalculator.NoHit, EmojiSheetLayoutCalculator.HitTest(layout, 43, 43));
    }

    // ── Constants ─────────────────────────────────────────────────────────────

    [Fact]
    public void SheetWidth_MatchesSevenColumnsOfCellSize()
    {
        Assert.Equal(266, EmojiSheetLayoutCalculator.SheetWidth);
        Assert.Equal(
            EmojiSheetLayoutCalculator.Columns * EmojiSheetLayoutCalculator.CellSize,
            EmojiSheetLayoutCalculator.SheetWidth);
    }

    [Fact]
    public void CellSize_EqualsItemPlusMarginOnBothSides()
    {
        Assert.Equal(
            EmojiSheetLayoutCalculator.ItemSize + 2 * EmojiSheetLayoutCalculator.ItemMargin,
            EmojiSheetLayoutCalculator.CellSize);
    }
}
