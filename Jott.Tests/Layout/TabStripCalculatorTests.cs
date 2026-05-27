using Jott.Ui.Layout;

namespace Jott.Tests.Layout;

public sealed class TabStripCalculatorTests
{
    // ── ComputePerTabMaxWidth ─────────────────────────────────────────────────

    [Fact]
    public void ComputePerTabMaxWidth_TypicalWidth_DistributesEvenlyAcrossTabs()
    {
        // 800 - 32 - 92 = 676 / 5 = 135.2
        double result = TabStripCalculator.ComputePerTabMaxWidth(800, 32, 92, 5);
        Assert.Equal(135.2, result);
    }

    [Fact]
    public void ComputePerTabMaxWidth_DefaultWindowWidth_FloorsAtTabMinWidth()
    {
        // 560 - 32 - 92 = 436 / 5 = 87.2 < 100 → floor to 100
        double result = TabStripCalculator.ComputePerTabMaxWidth(
            560,
            TabStripCalculator.TitleBarHeaderFallbackWidth,
            TabStripCalculator.TitleBarFooterFallbackWidth,
            5);
        Assert.Equal(TabStripCalculator.TabMinWidth, result);
    }

    [Fact]
    public void ComputePerTabMaxWidth_NarrowWindow_FloorsAtTabMinWidth()
    {
        // 300 - 32 - 92 = 176 / 5 = 35.2 → floor to 100
        double result = TabStripCalculator.ComputePerTabMaxWidth(300, 32, 92, 5);
        Assert.Equal(TabStripCalculator.TabMinWidth, result);
    }

    [Fact]
    public void ComputePerTabMaxWidth_ExactlyAtMinWidth_ReturnsMinWidthWithoutFlooring()
    {
        // 624 - 32 - 92 = 500 / 5 = 100 exactly — not floored, just at threshold
        double result = TabStripCalculator.ComputePerTabMaxWidth(624, 32, 92, 5);
        Assert.Equal(100, result);
    }

    [Fact]
    public void ComputePerTabMaxWidth_OneTabAboveMin_ReturnsFullAvailableWidth()
    {
        // 560 - 32 - 92 = 436 / 1 = 436
        double result = TabStripCalculator.ComputePerTabMaxWidth(560, 32, 92, 1);
        Assert.Equal(436, result);
    }

    [Fact]
    public void ComputePerTabMaxWidth_ZeroTabCount_ReturnsTabMinWidth()
    {
        double result = TabStripCalculator.ComputePerTabMaxWidth(560, 32, 92, 0);
        Assert.Equal(TabStripCalculator.TabMinWidth, result);
    }

    [Fact]
    public void ComputePerTabMaxWidth_NegativeTabCount_ReturnsTabMinWidth()
    {
        double result = TabStripCalculator.ComputePerTabMaxWidth(560, 32, 92, -1);
        Assert.Equal(TabStripCalculator.TabMinWidth, result);
    }

    [Fact]
    public void ComputePerTabMaxWidth_ZeroTotalWidth_ReturnsTabMinWidth()
    {
        double result = TabStripCalculator.ComputePerTabMaxWidth(0, 32, 92, 5);
        Assert.Equal(TabStripCalculator.TabMinWidth, result);
    }

    [Fact]
    public void ComputePerTabMaxWidth_NegativeTotalWidth_ReturnsTabMinWidth()
    {
        double result = TabStripCalculator.ComputePerTabMaxWidth(-100, 32, 92, 5);
        Assert.Equal(TabStripCalculator.TabMinWidth, result);
    }

    [Fact]
    public void ComputePerTabMaxWidth_HeaderPlusFooterExceedsTotalWidth_FloorsAtTabMinWidth()
    {
        // available = 100 - 80 - 80 = -60 / 3 = -20 → floor to 100
        double result = TabStripCalculator.ComputePerTabMaxWidth(100, 80, 80, 3);
        Assert.Equal(TabStripCalculator.TabMinWidth, result);
    }

    [Fact]
    public void ComputePerTabMaxWidth_ZeroHeaderAndFooter_UsesFullTotalWidth()
    {
        // 500 - 0 - 0 = 500 / 5 = 100
        double result = TabStripCalculator.ComputePerTabMaxWidth(500, 0, 0, 5);
        Assert.Equal(100, result);
    }

    [Theory]
    [InlineData(1000, 32, 92, 1, 876)]   // single tab gets full available
    [InlineData(1000, 32, 92, 2, 438)]   // two tabs split evenly
    [InlineData(1000, 32, 92, 4, 219)]   // four tabs split evenly
    [InlineData(1000, 32, 92, 5, 175.2)] // five tabs split evenly
    public void ComputePerTabMaxWidth_WideWindow_SplitsAvailableWidthAcrossAllTabs(
        double total, double header, double footer, int count, double expected)
    {
        double result = TabStripCalculator.ComputePerTabMaxWidth(total, header, footer, count);
        Assert.Equal(expected, result, precision: 10);
    }

    // ── ComputeTitleMaxWidth ──────────────────────────────────────────────────

    [Fact]
    public void ComputeTitleMaxWidth_NormalCase_SubtractsEmojiMarginAndSafetyPadding()
    {
        // 120 - 16 - 5 (TabEmojiMarginRight) - 6 (safety) = 93
        double result = TabStripCalculator.ComputeTitleMaxWidth(120, 16);
        Assert.Equal(93, result);
    }

    [Fact]
    public void ComputeTitleMaxWidth_ContainerTooNarrow_ClampsToZero()
    {
        // 20 - 16 - 5 - 6 = -7 → clamped to 0
        double result = TabStripCalculator.ComputeTitleMaxWidth(20, 16);
        Assert.Equal(0, result);
    }

    [Fact]
    public void ComputeTitleMaxWidth_ContainerExactlyAtThreshold_ReturnsZero()
    {
        // 27 - 16 - 5 - 6 = 0
        double result = TabStripCalculator.ComputeTitleMaxWidth(27, 16);
        Assert.Equal(0, result);
    }

    [Fact]
    public void ComputeTitleMaxWidth_WideContainer_LeavesAmpleSpaceForTitle()
    {
        // 200 - 16 - 5 - 6 = 173
        double result = TabStripCalculator.ComputeTitleMaxWidth(200, 16);
        Assert.Equal(173, result);
    }

    [Fact]
    public void ComputeTitleMaxWidth_ZeroEmojiWidth_SubtractsOnlyConstantOverhead()
    {
        // 100 - 0 - 5 - 6 = 89
        double result = TabStripCalculator.ComputeTitleMaxWidth(100, 0);
        Assert.Equal(89, result);
    }

    [Fact]
    public void ComputeTitleMaxWidth_ZeroContainerWidth_ReturnsZero()
    {
        double result = TabStripCalculator.ComputeTitleMaxWidth(0, 0);
        Assert.Equal(0, result);
    }

    [Fact]
    public void ComputeTitleMaxWidth_RespectsMeasuredEmojiWidthFromConstants()
    {
        // TabEmojiSize = 16; confirm the formula uses TabEmojiMarginRight (5) not some other value
        double emojiWidth = 16;
        double container = 100;
        double expected = container - emojiWidth - TabStripCalculator.TabEmojiMarginRight - 6;
        double result = TabStripCalculator.ComputeTitleMaxWidth(container, emojiWidth);
        Assert.Equal(expected, result);
    }
}
