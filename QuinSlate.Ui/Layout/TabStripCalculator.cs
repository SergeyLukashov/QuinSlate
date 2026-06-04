namespace QuinSlate.Ui.Layout;

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
    /// Inter-tab gap baked into the left edge of each tab's <c>TabBackground</c> pill.
    /// Must stay in sync with the pill's left <c>Margin</c> in BufferPanelResources.xaml (0).
    /// </summary>
    internal const double InterTabGapLeft = 0;

    /// <summary>Fallback width of the title-bar icon area (TabStripHeader). Padding 9+6 + a 20px image = 35.</summary>
    internal const double TitleBarHeaderFallbackWidth = 35;

    /// <summary>Fallback width of the right-hand footer spacer (the <c>TitleBarButtonsClusterWidth</c> XAML resource).</summary>
    internal const double TitleBarFooterFallbackWidth = 92;

    /// <summary>Right margin applied to the emoji glyph inside each tab header.</summary>
    internal const double TabEmojiMarginRight = 5;

    /// <summary>
    /// Horizontal chrome consumed by a tab around its header content in Equal mode:
    /// the <c>TabViewItem</c> Padding (3 + 3) plus the <c>TabBackground</c> pill left Margin (0).
    /// </summary>
    internal const double TabHorizontalChrome = 6;

    /// <summary>
    /// Fallback emoji width used when the emoji TextBlock has not yet been measured
    /// (<c>ActualWidth</c> &lt;= 0): the measured advance width of a 16px color-emoji glyph.
    /// </summary>
    internal const double TabEmojiFallbackWidth = 22;

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
    /// Returns the explicit <c>Width</c> to assign to a tab's header content so the
    /// content-sized tab grows to its equal share. In <c>TabWidthMode="Equal"</c> the SDK
    /// sizes each tab to its header's desired width and ignores the item's own width, so the
    /// only way to make all tabs equal and fill the strip is to size the header itself. This
    /// is the per-tab share minus the surrounding tab chrome (<see cref="TabHorizontalChrome"/>).
    /// </summary>
    /// <param name="perTabWidth">Stable equal-mode per-tab width (from <see cref="ComputePerTabMaxWidth"/>).</param>
    internal static double ComputeHeaderWidth(double perTabWidth)
    {
        return System.Math.Max(0, perTabWidth - TabHorizontalChrome);
    }

    /// <summary>
    /// Returns the <c>MaxWidth</c> for the title TextBlock inside a tab header so it
    /// truncates cleanly without overflowing the right padding of the header container.
    /// The cap is derived from the stable equal-mode per-tab width (minus tab chrome)
    /// rather than the live header-container width: in <c>TabWidthMode="Equal"</c> the
    /// container width is itself influenced by the header's desired width, so deriving the
    /// title cap from the container creates a feedback spiral (shrinking the title shrinks
    /// the container, which shrinks the title again, collapsing to zero). The per-tab width
    /// does not depend on title width, so it cannot feed back.
    /// </summary>
    /// <param name="perTabWidth">Stable equal-mode per-tab width (from <see cref="ComputePerTabMaxWidth"/>).</param>
    /// <param name="emojiWidth">Measured width of the emoji TextBlock (may be 0 before layout).</param>
    internal static double ComputeTitleMaxWidth(double perTabWidth, double emojiWidth)
    {
        return System.Math.Max(0, perTabWidth - TabHorizontalChrome - emojiWidth - TabEmojiMarginRight - TabTitleSafetyPadding);
    }
}
