namespace Jott.Ui.Layout;

/// <summary>
/// Pure layout calculations for the tab strip and tab header controls.
/// All methods are stateless and accept fully-resolved widths so they
/// can be exercised in unit tests without a running UI.
/// </summary>
internal static class TabStripCalculator
{
    /// <summary>Per-tab floor in Equal mode (mirrors the <c>TabViewItemMinWidth</c> XAML resource).</summary>
    internal const double TabMinWidth = 100;

    /// <summary>
    /// Inter-tab gap baked into the right edge of each tab's <c>TabBackground</c> pill.
    /// Must stay in sync with the pill's right <c>Margin</c> in BufferPanel.xaml (6).
    /// </summary>
    internal const double InterTabGapRight = 6;

    /// <summary>Fallback width of the title-bar icon area (TabStripHeader). Padding 10+6 + a 16px image = 32.</summary>
    internal const double TitleBarHeaderFallbackWidth = 32;

    /// <summary>Fallback width of the right-hand footer spacer (the <c>TitleBarButtonsClusterWidth</c> XAML resource).</summary>
    internal const double TitleBarFooterFallbackWidth = 92;

    /// <summary>Right margin applied to the emoji glyph inside each tab header.</summary>
    internal const double TabEmojiMarginRight = 5;

    /// <summary>
    /// Safety padding subtracted from the title TextBlock's MaxWidth to prevent the
    /// ellipsis from overflowing the right edge of the tab header's padding area.
    /// </summary>
    private const double TabTitleSafetyPadding = 6;

    /// <summary>
    /// Returns the <c>MaxWidth</c> to apply to every tab so that equal-mode tabs fill
    /// the row. The result is floored at <see cref="TabMinWidth"/> so that once the
    /// strip is too narrow to fit every tab at its minimum the SDK overflows and surfaces
    /// scroll buttons rather than clipping silently.
    /// </summary>
    /// <param name="totalWidth">Measured width of the TabView control.</param>
    /// <param name="headerWidth">Measured width of the TabStripHeader (icon drag area).</param>
    /// <param name="footerWidth">Measured width of the TabStripFooter spacer (button cluster reservation).</param>
    /// <param name="tabCount">Number of tab items currently in the strip.</param>
    internal static double ComputePerTabMaxWidth(double totalWidth, double headerWidth, double footerWidth, int tabCount)
    {
        if (tabCount <= 0 || totalWidth <= 0)
        {
            return TabMinWidth;
        }

        double available = totalWidth - headerWidth - footerWidth;
        double perTab = available / tabCount;
        return perTab < TabMinWidth ? TabMinWidth : perTab;
    }

    /// <summary>
    /// Returns the <c>MaxWidth</c> for the title TextBlock inside a tab header so it
    /// truncates cleanly without overflowing the right padding of the header container.
    /// </summary>
    /// <param name="containerWidth">Current width of the tab header container.</param>
    /// <param name="emojiWidth">Measured width of the emoji TextBlock (may be 0 before layout).</param>
    internal static double ComputeTitleMaxWidth(double containerWidth, double emojiWidth)
    {
        return System.Math.Max(0, containerWidth - emojiWidth - TabEmojiMarginRight - TabTitleSafetyPadding);
    }
}
