using QuinSlate.Ui.Layout;

namespace QuinSlate.Tests.Layout;

public sealed class EmojiSheetRevealPlannerTests
{
    private static IReadOnlyList<EmojiCellPosition> CellsWithTops(params double[] tops)
    {
        var cells = new EmojiCellPosition[tops.Length];
        for (int i = 0; i < tops.Length; i++)
        {
            cells[i] = new EmojiCellPosition(0, tops[i]);
        }

        return cells;
    }

    private static int[] Flatten(IReadOnlyList<IReadOnlyList<int>> slices)
    {
        var flat = new List<int>();
        foreach (IReadOnlyList<int> slice in slices)
        {
            flat.AddRange(slice);
        }

        return flat.ToArray();
    }

    [Fact]
    public void PlanSlices_NullCells_ReturnsNoSlices()
    {
        Assert.Empty(EmojiSheetRevealPlanner.PlanSlices(null, 0, 240, 28));
    }

    [Fact]
    public void PlanSlices_EmptyCells_ReturnsNoSlices()
    {
        Assert.Empty(EmojiSheetRevealPlanner.PlanSlices(CellsWithTops(), 0, 240, 28));
    }

    [Fact]
    public void PlanSlices_CountBelowSliceSize_ReturnsSinglePartialSlice()
    {
        var slices = EmojiSheetRevealPlanner.PlanSlices(CellsWithTops(0, 38, 76, 114, 152), 0, 240, 28);

        IReadOnlyList<int> slice = Assert.Single(slices);
        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, slice);
    }

    [Fact]
    public void PlanSlices_ExactSliceMultiple_ReturnsOnlyFullSlices()
    {
        var tops = new double[6];
        var slices = EmojiSheetRevealPlanner.PlanSlices(CellsWithTops(tops), 0, 240, 3);

        Assert.Equal(2, slices.Count);
        Assert.Equal(new[] { 0, 1, 2 }, slices[0]);
        Assert.Equal(new[] { 3, 4, 5 }, slices[1]);
    }

    [Fact]
    public void PlanSlices_Remainder_LastSliceIsShorter()
    {
        var tops = new double[7];
        var slices = EmojiSheetRevealPlanner.PlanSlices(CellsWithTops(tops), 0, 240, 3);

        Assert.Equal(3, slices.Count);
        Assert.Equal(3, slices[0].Count);
        Assert.Equal(3, slices[1].Count);
        Assert.Equal(new[] { 6 }, slices[2]);
    }

    [Fact]
    public void PlanSlices_WindowCellsComeFirstInLayoutOrder()
    {
        var slices = EmojiSheetRevealPlanner.PlanSlices(CellsWithTops(300, 10, 300, 20), 0, 240, 28);

        IReadOnlyList<int> slice = Assert.Single(slices);
        Assert.Equal(new[] { 1, 3, 0, 2 }, slice);
    }

    [Fact]
    public void PlanSlices_BothPartitionsPreserveLayoutOrder()
    {
        var slices = EmojiSheetRevealPlanner.PlanSlices(CellsWithTops(250, 5, 260, 15, 270), 0, 240, 28);

        IReadOnlyList<int> slice = Assert.Single(slices);
        Assert.Equal(new[] { 1, 3, 0, 2, 4 }, slice);
    }

    [Fact]
    public void PlanSlices_CellTopExactlyAtViewportBottom_IsNotPriority()
    {
        var slices = EmojiSheetRevealPlanner.PlanSlices(CellsWithTops(240, 239.5), 0, 240, 28);

        IReadOnlyList<int> slice = Assert.Single(slices);
        Assert.Equal(new[] { 1, 0 }, slice);
    }

    [Fact]
    public void PlanSlices_ScrolledWindow_PrioritizesCellsAroundOffset()
    {
        // Window [380, 620): cell at 350 straddles into it (350 + 38 > 380),
        // 400 and 600 are inside, 10 and 700 are outside.
        var slices = EmojiSheetRevealPlanner.PlanSlices(CellsWithTops(10, 350, 400, 600, 700), 380, 240, 28);

        IReadOnlyList<int> slice = Assert.Single(slices);
        Assert.Equal(new[] { 1, 2, 3, 0, 4 }, slice);
    }

    [Fact]
    public void PlanSlices_CellEndingExactlyAtWindowTop_IsNotPriority()
    {
        // Cell at Y=342 ends at 380 == window top: not intersecting.
        var slices = EmojiSheetRevealPlanner.PlanSlices(CellsWithTops(342, 342.5), 380, 240, 28);

        IReadOnlyList<int> slice = Assert.Single(slices);
        Assert.Equal(new[] { 1, 0 }, slice);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void PlanSlices_NonPositiveSliceSize_RevealsOneGlyphPerSlice(int sliceSize)
    {
        var slices = EmojiSheetRevealPlanner.PlanSlices(CellsWithTops(0, 38, 76), 0, 240, sliceSize);

        Assert.Equal(3, slices.Count);
        Assert.Equal(new[] { 0 }, slices[0]);
        Assert.Equal(new[] { 1 }, slices[1]);
        Assert.Equal(new[] { 2 }, slices[2]);
    }

    [Fact]
    public void PlanSlices_ZeroViewportHeight_UsesPureLayoutOrder()
    {
        var slices = EmojiSheetRevealPlanner.PlanSlices(CellsWithTops(10, 20), 0, 0, 28);

        IReadOnlyList<int> slice = Assert.Single(slices);
        Assert.Equal(new[] { 0, 1 }, slice);
    }

    [Fact]
    public void PlanSlices_EveryCellAppearsExactlyOnce()
    {
        var slices = EmojiSheetRevealPlanner.PlanSlices(CellsWithTops(300, 10, 300, 20), 0, 240, 3);

        int[] flat = Flatten(slices);
        Array.Sort(flat);
        Assert.Equal(new[] { 0, 1, 2, 3 }, flat);
    }

    [Fact]
    public void PlanSlices_RealBrowseLayout_ViewportPrefixThenRemainder()
    {
        // 50 entries: rows 0..5 start at Y 19..209 (< 240, 42 cells); row 6 at
        // 247 straddles nothing (window bottom is 240), so 42 window cells.
        EmojiSheetLayout layout = EmojiSheetLayoutCalculator.ComputeBrowseLayout(new[] { 50 });
        var slices = EmojiSheetRevealPlanner.PlanSlices(
            layout.Cells, 0, EmojiSheetLayoutCalculator.ScrollAreaHeight, EmojiSheetRevealPlanner.DefaultSliceSize);

        Assert.Equal(2, slices.Count);
        Assert.Equal(28, slices[0].Count);
        Assert.Equal(22, slices[1].Count);

        int[] flat = Flatten(slices);
        for (int i = 0; i < flat.Length; i++)
        {
            Assert.Equal(i, flat[i]);
        }
    }

    [Fact]
    public void CountWindowCells_NullCells_ReturnsZero()
    {
        Assert.Equal(0, EmojiSheetRevealPlanner.CountWindowCells(null, 0, 240));
    }

    [Fact]
    public void CountWindowCells_CountsIntersectingCellsOnly()
    {
        // Window [380, 620): straddler at 350, inside at 400 and 600,
        // outside at 10 and 700.
        int count = EmojiSheetRevealPlanner.CountWindowCells(CellsWithTops(10, 350, 400, 600, 700), 380, 240);

        Assert.Equal(3, count);
    }

    [Fact]
    public void CountWindowCells_MatchesPlanSlicesWindowPrefix()
    {
        EmojiSheetLayout layout = EmojiSheetLayoutCalculator.ComputeBrowseLayout(new[] { 50 });

        int count = EmojiSheetRevealPlanner.CountWindowCells(
            layout.Cells, 0, EmojiSheetLayoutCalculator.ScrollAreaHeight);

        Assert.Equal(42, count);
    }

    [Fact]
    public void DefaultSliceSize_IsFourFullRows()
    {
        Assert.Equal(4 * EmojiSheetLayoutCalculator.Columns, EmojiSheetRevealPlanner.DefaultSliceSize);
    }
}
