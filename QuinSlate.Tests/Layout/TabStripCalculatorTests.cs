using QuinSlate.Ui.Layout;

namespace QuinSlate.Tests.Layout;

public sealed class TabStripCalculatorTests
{
    // ── ComputePerTabMaxWidth ─────────────────────────────────────────────────

    [Fact]
    public void ComputePerTabMaxWidth_TypicalWidth_DistributesEvenlyAcrossTabs()
    {
        // 800 - 32 - 92 - 12 (strip chrome) = 664 / 5 = 132.8 → floor 132
        double result = TabStripCalculator.ComputePerTabMaxWidth(800, 32, 92, 5);
        Assert.Equal(132, result);
    }

    [Fact]
    public void ComputePerTabMaxWidth_TabRunNeverExceedsTheStripViewport()
    {
        // The whole point of TabStripChrome: tabCount * perTab, plus the chrome the strip itself
        // consumes, must stay within the region left over after the header and footer. If it does
        // not, the strip is permanently scrollable and a drag-reorder parks it off its left edge.
        //
        // The sweep runs from below the panel minimum to a maximized 4K window at 100% scaling,
        // in sub-DIP steps, so every width a resize or a maximize can land on is covered.
        const double header = 35.2;
        const double footer = TabStripCalculator.TitleBarFooterFallbackWidth;

        for (double total = 320; total <= 3840; total += 0.4)
        {
            double perTab = TabStripCalculator.ComputePerTabMaxWidth(total, header, footer, 5);
            if (perTab <= TabStripCalculator.TabMinWidth)
            {
                continue; // genuine overflow regime: the strip is meant to scroll
            }

            double tabRun = perTab * 5;
            double available = total - header - footer;
            Assert.True(
                tabRun + TabStripCalculator.TabStripChrome <= available,
                $"tab run {tabRun} + chrome overflows available {available} at total {total}");
        }
    }

    [Fact]
    public void ComputePerTabMaxWidth_AlwaysReturnsWholeDips()
    {
        double result = TabStripCalculator.ComputePerTabMaxWidth(887.2, 35.2, 92, 5);
        Assert.Equal(Math.Floor(result), result);
    }

    [Fact]
    public void ComputePerTabMaxWidth_BelowTheOverflowThreshold_FloorsAtTabMinWidth()
    {
        // 560 - 35 (icon) - 172 (button cluster) - 12 (strip chrome) = 341 / 5 = 68.2 < 100
        // → floor to 100, and the strip overflows into its scroll buttons.
        double result = TabStripCalculator.ComputePerTabMaxWidth(
            560,
            TabStripCalculator.TitleBarHeaderFallbackWidth,
            TabStripCalculator.TitleBarFooterFallbackWidth,
            5);
        Assert.Equal(TabStripCalculator.TabMinWidth, result);
    }

    [Fact]
    public void ComputePerTabMaxWidth_DefaultWindowWidth_FitsFiveTabsAboveTheMinimum()
    {
        // MainWindow.PanelDefaultWidth. The four-button title-bar cluster reserves 172 DIP, so
        // the overflow threshold for five tabs sits at 35 + 172 + 12 + 5 * 100 = 719: a default
        // window narrower than that would open showing scroll chevrons. Guards the pairing
        // between PanelDefaultWidth and TitleBarFooterFallbackWidth — changing either without
        // the other regresses the default window.
        const double panelDefaultWidth = 740;

        double result = TabStripCalculator.ComputePerTabMaxWidth(
            panelDefaultWidth,
            TabStripCalculator.TitleBarHeaderFallbackWidth,
            TabStripCalculator.TitleBarFooterFallbackWidth,
            5);

        Assert.True(
            result > TabStripCalculator.TabMinWidth,
            $"five tabs at the default window width fall back to the {TabStripCalculator.TabMinWidth} DIP floor (got {result}), so the strip overflows");
    }

    [Fact]
    public void ComputePerTabMaxWidth_NarrowWindow_FloorsAtTabMinWidth()
    {
        // 300 - 32 - 92 - 12 = 164 / 5 = 32.8 → floor to 100
        double result = TabStripCalculator.ComputePerTabMaxWidth(300, 32, 92, 5);
        Assert.Equal(TabStripCalculator.TabMinWidth, result);
    }

    [Fact]
    public void ComputePerTabMaxWidth_ExactlyAtMinWidth_ReturnsMinWidthWithoutFlooring()
    {
        // 636 - 32 - 92 - 12 = 500 / 5 = 100 exactly — not floored, just at threshold
        double result = TabStripCalculator.ComputePerTabMaxWidth(636, 32, 92, 5);
        Assert.Equal(100, result);
    }

    [Fact]
    public void ComputePerTabMaxWidth_OneTabAboveMin_ReturnsFullAvailableWidth()
    {
        // 560 - 32 - 92 - 12 = 424 / 1 = 424
        double result = TabStripCalculator.ComputePerTabMaxWidth(560, 32, 92, 1);
        Assert.Equal(424, result);
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
    public void ComputePerTabMaxWidth_ZeroHeaderAndFooter_UsesFullTotalWidthLessStripChrome()
    {
        // 712 - 0 - 0 - 12 = 700 / 5 = 140
        double result = TabStripCalculator.ComputePerTabMaxWidth(712, 0, 0, 5);
        Assert.Equal(140, result);
    }

    [Theory]
    [InlineData(1000, 32, 92, 1, 864)] // single tab gets full available
    [InlineData(1000, 32, 92, 2, 432)] // two tabs split evenly
    [InlineData(1000, 32, 92, 4, 216)] // four tabs split evenly
    [InlineData(1000, 32, 92, 5, 172)] // five tabs split evenly (172.8 truncated to whole DIPs)
    public void ComputePerTabMaxWidth_WideWindow_SplitsAvailableWidthAcrossAllTabs(
        double total, double header, double footer, int count, double expected)
    {
        double result = TabStripCalculator.ComputePerTabMaxWidth(total, header, footer, count);
        Assert.Equal(expected, result, precision: 10);
    }

    // ── ComputeHeaderWidth ────────────────────────────────────────────────────

    [Fact]
    public void ComputeHeaderWidth_NormalCase_SubtractsTabChromeFromPerTab()
    {
        // perTab 100.5 - 6 (chrome) = 94.5 — the header width that fills the tab to its share.
        double result = TabStripCalculator.ComputeHeaderWidth(100.5);
        Assert.Equal(94.5, result);
    }

    [Fact]
    public void ComputeHeaderWidth_PerTabBelowChrome_ClampsToZero()
    {
        double result = TabStripCalculator.ComputeHeaderWidth(8);
        Assert.Equal(2, result); // 8 - 6 = 2
    }

    // ── ComputeTitleMaxWidth ──────────────────────────────────────────────────

    [Fact]
    public void ComputeTitleMaxWidth_NormalCase_SubtractsChromeEmojiMarginAndSafetyPadding()
    {
        // perTab 120 - 6 (chrome) - 16 - 5 (TabEmojiMarginRight) - 6 (safety) = 87
        double result = TabStripCalculator.ComputeTitleMaxWidth(120, 16);
        Assert.Equal(87, result);
    }

    [Fact]
    public void ComputeTitleMaxWidth_StableEndStateMatchesObservedFix()
    {
        // The stable end-state from the logged spiral fix:
        // perTab 100 - 6 (chrome) - 22 (emoji) - 5 (margin) - 6 (safety) = 61.
        // Because perTab does not depend on title width, this cannot feed back to zero.
        double result = TabStripCalculator.ComputeTitleMaxWidth(100, 22);
        Assert.Equal(61, result);
    }

    [Fact]
    public void ComputeTitleMaxWidth_PerTabTooNarrow_ClampsToZero()
    {
        // 20 - 6 - 16 - 5 - 6 = -13 → clamped to 0
        double result = TabStripCalculator.ComputeTitleMaxWidth(20, 16);
        Assert.Equal(0, result);
    }

    [Fact]
    public void ComputeTitleMaxWidth_PerTabExactlyAtThreshold_ReturnsZero()
    {
        // 33 - 6 - 16 - 5 - 6 = 0
        double result = TabStripCalculator.ComputeTitleMaxWidth(33, 16);
        Assert.Equal(0, result);
    }

    [Fact]
    public void ComputeTitleMaxWidth_WidePerTab_LeavesAmpleSpaceForTitle()
    {
        // 200 - 6 - 16 - 5 - 6 = 167
        double result = TabStripCalculator.ComputeTitleMaxWidth(200, 16);
        Assert.Equal(167, result);
    }

    [Fact]
    public void ComputeTitleMaxWidth_ZeroEmojiWidth_SubtractsOnlyConstantOverhead()
    {
        // 100 - 6 - 0 - 5 - 6 = 83
        double result = TabStripCalculator.ComputeTitleMaxWidth(100, 0);
        Assert.Equal(83, result);
    }

    [Fact]
    public void ComputeTitleMaxWidth_ZeroPerTabWidth_ReturnsZero()
    {
        double result = TabStripCalculator.ComputeTitleMaxWidth(0, 0);
        Assert.Equal(0, result);
    }

    [Fact]
    public void ComputeTitleMaxWidth_RespectsMeasuredEmojiWidthFromConstants()
    {
        // confirm the formula subtracts TabHorizontalChrome (6) and TabEmojiMarginRight (5)
        double emojiWidth = 16;
        double perTab = 100;
        double expected = perTab - TabStripCalculator.TabHorizontalChrome - emojiWidth - TabStripCalculator.TabEmojiMarginRight - 6;
        double result = TabStripCalculator.ComputeTitleMaxWidth(perTab, emojiWidth);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ComputeTitleMaxWidth_VisibleSearchBadge_TitleGivesWayToIt()
    {
        // 120 - 6 - 16 - 5 - 6 = 87, less a 24-wide badge (18 + the 6 gap) = 63
        double badgeWidth = 18 + TabStripCalculator.TabSearchBadgeGap;
        double result = TabStripCalculator.ComputeTitleMaxWidth(120, 16, badgeWidth);
        Assert.Equal(63, result);
    }

    [Fact]
    public void ComputeTitleMaxWidth_BadgeWiderThanTheRoom_ClampsTitleToZero()
    {
        double result = TabStripCalculator.ComputeTitleMaxWidth(100, 22, 80);
        Assert.Equal(0, result);
    }
}
