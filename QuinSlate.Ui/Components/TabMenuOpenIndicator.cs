using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using QuinSlate.Ui.Helpers;

namespace QuinSlate.Ui.Components;

/// <summary>
/// Shows or hides the marker a tab carries while its context menu or edit flyout is open, in place
/// of its active indicator.
/// </summary>
internal static class TabMenuOpenIndicator
{
    private const string MenuOpenIndicatorPartName = "MenuOpenIndicator";
    private const string ActiveIndicatorPartName = "ActiveIndicator";
    private const double ShownOpacity = 1;
    private const double HiddenOpacity = 0;

    /// <summary>
    /// Shows the menu-open marker on <paramref name="tabItem"/> and hides its active indicator when
    /// <paramref name="visible"/>; the reverse otherwise.
    /// </summary>
    public static void SetVisibility(TabViewItem tabItem, bool visible)
    {
        if (tabItem == null)
        {
            return;
        }

        Border indicator = VisualTreeHelpers.FindVisualChild<Border>(tabItem, MenuOpenIndicatorPartName);
        if (indicator != null)
        {
            indicator.Opacity = visible ? ShownOpacity : HiddenOpacity;
        }

        Border activeIndicator = VisualTreeHelpers.FindVisualChild<Border>(tabItem, ActiveIndicatorPartName);
        if (activeIndicator != null)
        {
            activeIndicator.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
        }
    }
}
