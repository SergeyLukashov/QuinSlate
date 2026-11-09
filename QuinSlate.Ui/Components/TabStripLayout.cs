using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using QuinSlate.Ui.Helpers;
using QuinSlate.Ui.Layout;
using System;

namespace QuinSlate.Ui.Components;

/// <summary>
/// Sizes the buffer panel's five tabs against the live tab strip and keeps the SDK <c>TabView</c>
/// template in line with that sizing: the equal per-tab share, the scroll buttons and scroll offset
/// while the tabs fit, the right-content column reserved for the title-bar buttons, and the first
/// tab's leading gap. See <c>Docs/Wiki/01-TABVIEW-DRAG-REORDER.md</c>.
/// </summary>
internal sealed class TabStripLayout
{
    /// <summary>
    /// Name of the floating pill <c>Border</c> template part inside the TabViewItem
    /// ControlTemplate whose left margin carries the inter-tab gap.
    /// </summary>
    private const string TabBackgroundPartName = "TabBackground";

    /// <summary>
    /// Name of the column container <c>Grid</c> in the SDK <c>TabView</c> template that hosts
    /// <c>LeftContentColumn</c>, <c>TabColumn</c>, <c>AddButtonColumn</c>, and
    /// <c>RightContentColumn</c>.
    /// </summary>
    private const string TabContainerGridPartName = "TabContainerGrid";

    /// <summary>
    /// Name of the <c>ScrollViewer</c> template part inside the SDK <c>TabView</c> template that
    /// scrolls the tab strip and whose <c>ComputedHorizontalScrollBarVisibility</c> the two scroll
    /// buttons' containers are template-bound to.
    /// </summary>
    private const string TabStripScrollViewerPartName = "ScrollViewer";

    private readonly TabView tabView;
    private readonly FrameworkElement titleBarHeader;
    private readonly FrameworkElement titleBarFooter;
    private readonly TabHeaderRegistry headers;

    private TabViewItem firstTabWithZeroedGap;

    /// <summary>
    /// The tab strip's <c>ScrollViewer</c> template part, resolved once and kept so its
    /// <c>ViewChanged</c> stays hooked for the panel's lifetime.
    /// </summary>
    private ScrollViewer tabStripScrollViewer;

    /// <summary>Creates the layout over the panel's tab strip.</summary>
    /// <param name="tabView">The buffer panel's tab strip.</param>
    /// <param name="titleBarHeader">The strip header holding the title-bar icon.</param>
    /// <param name="titleBarFooter">The strip footer spacer reserving the title-bar buttons.</param>
    /// <param name="headers">The tab headers whose widths this layout sets.</param>
    public TabStripLayout(TabView tabView, FrameworkElement titleBarHeader, FrameworkElement titleBarFooter, TabHeaderRegistry headers)
    {
        if (tabView == null)
        {
            throw new ArgumentNullException(nameof(tabView));
        }

        if (headers == null)
        {
            throw new ArgumentNullException(nameof(headers));
        }

        this.tabView = tabView;
        this.titleBarHeader = titleBarHeader;
        this.titleBarFooter = titleBarFooter;
        this.headers = headers;
    }

    /// <summary>
    /// Pins the SDK <c>TabView</c> template's right-content column to a fixed pixel width equal
    /// to the overlay button cluster, so the <c>TabStripFooter</c> reservation survives overflow
    /// mode. In the default template that column is <c>Width="*"</c> and collapses to ~0 once
    /// the tabs overflow and the Auto <c>TabColumn</c> consumes all available width; the SDK
    /// then stops reserving the cluster and the tabs + scroll-forward button render under the
    /// top-right pin/close overlay. Walking the live visual tree (rather than copying the SDK
    /// template) is intentional: a full template copy is pinned to whatever <c>WindowsAppSDK</c>
    /// ships and crashes the app when parts diverge. The right-content column is always the
    /// last column inside <c>TabContainerGrid</c>. The reservation is re-applied on every
    /// <c>SizeChanged</c> to survive the SDK's own column-width updates.
    /// </summary>
    public void PinRightContentColumnReservation()
    {
        Grid container = VisualTreeHelpers.FindVisualChild<Grid>(tabView, TabContainerGridPartName);
        if (container == null || container.ColumnDefinitions.Count == 0)
        {
            return;
        }

        ColumnDefinition rightColumn = container.ColumnDefinitions[container.ColumnDefinitions.Count - 1];
        var target = new GridLength(TabStripCalculator.TitleBarFooterFallbackWidth, GridUnitType.Pixel);
        if (rightColumn.Width.GridUnitType != target.GridUnitType || rightColumn.Width.Value != target.Value)
        {
            rightColumn.Width = target;
        }
    }

    /// <summary>
    /// Sizes every tab to the equal share of the live tab-strip width, so the tabs fill the row
    /// exactly and shrink as the window narrows. The share is applied to each tab's <em>header</em>
    /// (the tab sizes to its content under <c>TabWidthMode="SizeToContent"</c>), which is why the
    /// tabs come out equal without the SDK stamping a width on them — see the sizing note in
    /// BufferPanel.xaml for why Equal mode is not used. The share is floored at
    /// <see cref="TabStripCalculator.TabMinWidth"/> so that once the tabs can no longer fit at
    /// their minimum the strip overflows and surfaces its scroll buttons instead of clipping. The
    /// matching title <c>MaxWidth</c> is derived from the same stable share.
    /// </summary>
    public void UpdateEqualTabMaxWidth()
    {
        int count = tabView.TabItems.Count;
        if (count == 0)
        {
            return;
        }

        double totalWidth = tabView.ActualWidth;
        if (totalWidth <= 0)
        {
            return;
        }

        double headerWidth = TabStripCalculator.TitleBarHeaderFallbackWidth;
        if (titleBarHeader != null && titleBarHeader.ActualWidth > 0)
        {
            headerWidth = titleBarHeader.ActualWidth;
        }

        double footerWidth = TabStripCalculator.TitleBarFooterFallbackWidth;
        if (titleBarFooter != null && titleBarFooter.ActualWidth > 0)
        {
            footerWidth = titleBarFooter.ActualWidth;
        }

        double perTab = TabStripCalculator.ComputePerTabMaxWidth(totalWidth, headerWidth, footerWidth, count);
        SuppressScrollButtonsWhenTabsFit(perTab);

        foreach (var obj in tabView.TabItems)
        {
            if (obj is TabViewItem item)
            {
                item.MaxWidth = perTab;
            }
        }

        double headerWidthEach = TabStripCalculator.ComputeHeaderWidth(perTab);
        foreach (var entry in headers.HeaderContainers)
        {
            if (entry.Value != null)
            {
                entry.Value.Width = headerWidthEach;
            }
        }

        foreach (var entry in headers.TitleBlocks)
        {
            TextBlock titleBlock = entry.Value;
            if (titleBlock == null)
            {
                continue;
            }

            double emojiWidth = TabStripCalculator.TabEmojiFallbackWidth;
            if (headers.EmojiBlocks.TryGetValue(entry.Key, out TextBlock emojiBlock)
                && emojiBlock != null
                && emojiBlock.ActualWidth > 0)
            {
                emojiWidth = emojiBlock.ActualWidth;
            }

            titleBlock.MaxWidth = TabStripCalculator.ComputeTitleMaxWidth(perTab, emojiWidth, MeasureSearchBadge(entry.Key));
        }
    }

    /// <summary>
    /// Scrolls the tab strip back to its left edge whenever the five tabs are meant to fit, so the
    /// row always starts where the tab strip starts.
    /// </summary>
    /// <remarks>
    /// The tabs are sized to fill the strip, but each one lands a fraction of a pixel wider than
    /// its computed share once layout snaps it to whole device pixels, so the strip's content
    /// measures a handful of DIPs wider than its viewport and is technically scrollable at rest.
    /// Nothing scrolls it in normal use — but a drag-reorder does, and the strip then stays parked
    /// at that offset, drawing every tab a few pixels left of where it belongs. (It looks like it
    /// heals itself when you switch tabs only because the SDK scrolls the newly selected tab back
    /// into view.) This is the counterpart of <see cref="SuppressScrollButtonsWhenTabsFit"/>: in
    /// the regime where the strip presents itself as unscrollable, it must not sit scrolled either.
    /// Below the per-tab floor the strip genuinely scrolls and the offset is the user's to keep.
    ///
    /// Enforced from the strip's own <c>ViewChanged</c> rather than at the end of a reorder: the
    /// SDK scrolls the dropped tab into view after the tab collection has settled, so a one-shot
    /// reset driven off the collection change is overwritten a moment later.
    /// </remarks>
    public void ResetStripScrollWhenTabsFit()
    {
        ScrollViewer scrollViewer = ResolveStripScrollViewer();
        if (scrollViewer == null || scrollViewer.HorizontalScrollBarVisibility != ScrollBarVisibility.Hidden)
        {
            return;
        }

        if (scrollViewer.HorizontalOffset > 0)
        {
            scrollViewer.ChangeView(0.0, null, null, true);
        }
    }

    /// <summary>
    /// Removes the leading inter-tab gap from the FIRST tab only so the tab strip is
    /// symmetric: the first tab's pill must start flush at the left rather than leaving an
    /// <see cref="TabStripCalculator.InterTabGapLeft"/> dead strip after the left edge / scroll-back button,
    /// and because no tab carries a trailing margin the rightmost-visible tab always abuts
    /// the right edge / scroll-forward button. The gap stays intact between all other tabs.
    /// Restores the gap on whichever tab was previously first (the first item changes after
    /// a reorder), then zeroes the left margin on the new first tab's <c>TabBackground</c>
    /// pill. When the new first tab's template is not yet realized, the work is deferred
    /// until that item raises its <c>Loaded</c> event.
    /// </summary>
    public void RecomputeFirstTabLeadingGap()
    {
        int count = tabView.TabItems.Count;
        TabViewItem newFirst = null;
        if (count > 0 && tabView.TabItems[0] is TabViewItem candidate)
        {
            newFirst = candidate;
        }

        if (firstTabWithZeroedGap != null && !ReferenceEquals(firstTabWithZeroedGap, newFirst))
        {
            SetTabBackgroundLeftMargin(firstTabWithZeroedGap, TabStripCalculator.InterTabGapLeft);
            firstTabWithZeroedGap = null;
        }

        if (newFirst == null)
        {
            return;
        }

        if (SetTabBackgroundLeftMargin(newFirst, 0))
        {
            firstTabWithZeroedGap = newFirst;
            return;
        }

        RoutedEventHandler onLoaded = null;
        onLoaded = (s, e) =>
        {
            newFirst.Loaded -= onLoaded;
            if (ReferenceEquals(tabView.TabItems.Count > 0
                ? tabView.TabItems[0]
                : null, newFirst))
            {
                if (SetTabBackgroundLeftMargin(newFirst, 0))
                {
                    firstTabWithZeroedGap = newFirst;
                }
            }
        };
        newFirst.Loaded += onLoaded;
    }

    /// <summary>
    /// The room a tab's search badge takes after its title, gap and right inset included; 0 while
    /// it is hidden.
    /// </summary>
    private double MeasureSearchBadge(int bufferIndex)
    {
        if (headers.SearchBadges.TryGetValue(bufferIndex, out InfoBadge badge) == false
            || badge.Visibility != Visibility.Visible)
        {
            return 0;
        }

        return badge.ActualWidth + TabStripCalculator.TabSearchBadgeGap + TabStripCalculator.TabSearchBadgeMarginRight;
    }

    /// <summary>
    /// Hides the tab strip's scroll buttons whenever the five tabs fit at more than their minimum
    /// width, and hands visibility back to the SDK once they are squeezed to that floor and the
    /// strip must really scroll.
    /// </summary>
    /// <remarks>
    /// The buttons' containers are template-bound to the strip <c>ScrollViewer</c>'s
    /// <c>ComputedHorizontalScrollBarVisibility</c>, and the tabs are sized to fill the strip
    /// exactly. A drag-reorder lifts the dragged tab out of <c>TabItems</c> and puts it back a
    /// frame later; during that gap the strip measures as scrollable and the buttons appear. They
    /// then occupy a column each, which shrinks the viewport below the tabs' width — so the strip
    /// stays "scrollable" and the buttons stay up, shifting every tab sideways for the whole drag
    /// and snapping back on drop. Pinning the scroll-bar visibility to <c>Hidden</c> while the tabs
    /// fit denies that latch its first step. <c>Hidden</c> rather than <c>Disabled</c> keeps the
    /// strip scrollable by wheel and by keyboard.
    /// </remarks>
    private void SuppressScrollButtonsWhenTabsFit(double perTabWidth)
    {
        ScrollViewer scrollViewer = ResolveStripScrollViewer();
        if (scrollViewer == null)
        {
            return;
        }

        bool tabsFit = perTabWidth > TabStripCalculator.TabMinWidth;
        ScrollBarVisibility target = tabsFit ? ScrollBarVisibility.Hidden : ScrollBarVisibility.Auto;
        if (scrollViewer.HorizontalScrollBarVisibility != target)
        {
            scrollViewer.HorizontalScrollBarVisibility = target;
        }
    }

    /// <summary>
    /// Resolves the tab strip's <c>ScrollViewer</c> from the live template, hooking its
    /// <c>ViewChanged</c> the first time it is found.
    /// </summary>
    private ScrollViewer ResolveStripScrollViewer()
    {
        if (tabStripScrollViewer == null)
        {
            tabStripScrollViewer = VisualTreeHelpers.FindVisualChild<ScrollViewer>(tabView, TabStripScrollViewerPartName);
            if (tabStripScrollViewer != null)
            {
                tabStripScrollViewer.ViewChanged += OnStripViewChanged;
            }
        }

        return tabStripScrollViewer;
    }

    private void OnStripViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        if (e.IsIntermediate)
        {
            return;
        }

        ResetStripScrollWhenTabsFit();
    }

    /// <summary>
    /// Finds the <c>TabBackground</c> pill Border inside <paramref name="item"/>'s realized
    /// template and sets its left margin to <paramref name="leftMargin"/>, leaving top,
    /// right, and bottom untouched. Returns <c>false</c> when the template part is not yet
    /// realized so the caller can defer.
    /// </summary>
    private static bool SetTabBackgroundLeftMargin(TabViewItem item, double leftMargin)
    {
        if (item == null)
        {
            return false;
        }

        Border pill = VisualTreeHelpers.FindVisualChild<Border>(item, TabBackgroundPartName);
        if (pill == null)
        {
            return false;
        }

        Thickness current = pill.Margin;
        pill.Margin = new Thickness(leftMargin, current.Top, current.Right, current.Bottom);
        return true;
    }
}
