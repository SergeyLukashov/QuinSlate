using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using QuinSlate.Ui.Helpers;
using System;
using Windows.Foundation;

namespace QuinSlate.Ui.Components;

/// <summary>
/// Keeps the editor host overlay (the layer carrying the shared WebView2) over exactly the
/// <c>TabView</c>'s content presenter, so the tab strip stays interactive above it.
/// </summary>
internal sealed class EditorOverlayPositioner
{
    private const string TabContentPresenterPartName = "TabContentPresenter";

    private readonly TabView tabView;
    private readonly FrameworkElement root;
    private readonly FrameworkElement overlay;

    /// <summary>Creates the positioner.</summary>
    /// <param name="tabView">The tab strip whose content presenter the overlay covers.</param>
    /// <param name="root">The element the overlay's margins are relative to.</param>
    /// <param name="overlay">The editor host overlay.</param>
    public EditorOverlayPositioner(TabView tabView, FrameworkElement root, FrameworkElement overlay)
    {
        if (tabView == null)
        {
            throw new ArgumentNullException(nameof(tabView));
        }

        if (root == null)
        {
            throw new ArgumentNullException(nameof(root));
        }

        this.tabView = tabView;
        this.root = root;
        this.overlay = overlay;
    }

    /// <summary>
    /// Positions the editor host overlay to cover exactly the TabView's content presenter, so the
    /// tab strip stays interactive above it. Walks the live template (matching the panel's other
    /// template-part lookups) and sets margins from the presenter's bounds relative to the root.
    /// </summary>
    public void Position()
    {
        if (overlay == null)
        {
            return;
        }

        ContentPresenter presenter = VisualTreeHelpers.FindVisualChild<ContentPresenter>(tabView, TabContentPresenterPartName);
        if (presenter == null || presenter.ActualWidth <= 0 || presenter.ActualHeight <= 0)
        {
            return;
        }

        GeneralTransform transform = presenter.TransformToVisual(root);
        Point origin = transform.TransformPoint(new Point(0, 0));

        double right = root.ActualWidth - origin.X - presenter.ActualWidth;
        double bottom = root.ActualHeight - origin.Y - presenter.ActualHeight;
        overlay.Margin = new Thickness(origin.X, origin.Y, Math.Max(0, right), Math.Max(0, bottom));
    }
}
