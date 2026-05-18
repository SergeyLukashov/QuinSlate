using Jott.Ui.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Jott.Ui.Components;

/// <summary>
/// Builds and caches the emoji-picker flyout and raises
/// <see cref="EmojiSelected"/> when the user picks an emoji.
/// </summary>
public sealed class EmojiPicker
{
    private const double ItemSize = 36;
    private const double SearchHeight = 32;
    private const double ScrollHeight = 240;
    private const int EmojiColumns = 7;
    private const double PickerWidth = EmojiColumns * (ItemSize + 2);
    private const double LabelFontSize = 11;
    private const double EmojiFontSize = ItemSize * 0.55;
    private const string SecondaryTextBrushKey = "TextFillColorSecondaryBrush";
    private const string SearchPlaceholder = "Search emoji…";
    private const string RecentLabel = "Recent";
    private const int WarmRenderOpacity = 0;

    private static readonly DataTemplate emojiItemTemplate = BuildEmojiItemTemplate();
    private static readonly Style emojiItemContainerStyle = BuildItemContainerStyle();

    private Flyout cachedFlyout;
    private TextBox cachedSearchBox;
    private GridView cachedCategoryView;
    private GridView cachedSearchResultsView;
    private GridView cachedRecentView;
    private StackPanel cachedRecentContainer;

    private readonly ObservableCollection<EmojiEntry> recentItems = new ObservableCollection<EmojiEntry>();
    private readonly ObservableCollection<EmojiEntry> searchResultItems = new ObservableCollection<EmojiEntry>();

    private bool isWarmRendered;

    /// <summary>Raised when the user selects an emoji. The argument is the selected emoji string.</summary>
    public event EventHandler<string> EmojiSelected;

    /// <summary>
    /// Builds the picker flyout and its grid views eagerly, then performs a
    /// one-time invisible warm render so the first real <see cref="Open"/> does
    /// not pay the flyout-presenter and grid-container realization cost on the
    /// click path. Safe to call multiple times; only the first call does work.
    /// </summary>
    /// <param name="warmAnchor">
    /// A zero-size element already attached to the visual tree, used solely as
    /// the anchor for the invisible warm-render pass. The flyout presenter is
    /// rendered fully transparent during this pass so the user never sees it.
    /// </param>
    public void Prewarm(FrameworkElement warmAnchor)
    {
        EnsurePickerBuilt();

        if (isWarmRendered || warmAnchor == null)
        {
            return;
        }

        isWarmRendered = true;

        EventHandler<object> onOpened = null;
        onOpened = (s, e) =>
        {
            cachedFlyout.Opened -= onOpened;
            cachedFlyout.FlyoutPresenterStyle = null;
            cachedFlyout.Hide();
        };
        cachedFlyout.Opened += onOpened;

        cachedFlyout.FlyoutPresenterStyle = BuildTransparentPresenterStyle();
        FlyoutBase.SetAttachedFlyout(warmAnchor, cachedFlyout);
        FlyoutBase.ShowAttachedFlyout(warmAnchor);
    }

    private static Style BuildTransparentPresenterStyle()
    {
        var style = new Style(typeof(FlyoutPresenter));
        style.Setters.Add(new Setter(UIElement.OpacityProperty, (double)WarmRenderOpacity));
        style.Setters.Add(new Setter(UIElement.IsHitTestVisibleProperty, false));
        return style;
    }

    /// <summary>
    /// Opens the emoji picker anchored to <paramref name="anchor"/>.
    /// The recent list is refreshed from <paramref name="recentEmoji"/> on every open.
    /// </summary>
    public void Open(Button anchor, IReadOnlyList<string> recentEmoji)
    {
        if (anchor == null)
        {
            return;
        }

        if (recentEmoji == null)
        {
            recentEmoji = new List<string>();
        }

        EnsurePickerBuilt();

        cachedFlyout.FlyoutPresenterStyle = null;
        cachedSearchBox.Text = string.Empty;

        recentItems.Clear();
        foreach (var em in recentEmoji)
        {
            recentItems.Add(new EmojiEntry(em, em));
        }

        cachedRecentContainer.Visibility = recentItems.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        FilterPicker(string.Empty);

        FlyoutBase.SetAttachedFlyout(anchor, cachedFlyout);
        FlyoutBase.ShowAttachedFlyout(anchor);
    }

    private void EnsurePickerBuilt()
    {
        if (cachedFlyout != null)
        {
            return;
        }

        var searchBox = new TextBox
        {
            PlaceholderText = SearchPlaceholder,
            Height = SearchHeight,
            Margin = new Thickness(0, 0, 0, 4),
        };
        cachedSearchBox = searchBox;

        cachedCategoryView = BuildEmojiGridView();
        cachedCategoryView.Height = ScrollHeight;
        var categorySource = new CollectionViewSource
        {
            IsSourceGrouped = true,
            Source = EmojiData.GetGroups(),
        };
        categorySource.ItemsPath = new PropertyPath(nameof(EmojiGroup.Entries));
        cachedCategoryView.ItemsSource = categorySource.View;
        cachedCategoryView.GroupStyle.Add(BuildGroupStyle());

        cachedSearchResultsView = BuildEmojiGridView();
        cachedSearchResultsView.Height = ScrollHeight;
        cachedSearchResultsView.ItemsSource = searchResultItems;
        cachedSearchResultsView.Visibility = Visibility.Collapsed;

        cachedRecentView = BuildEmojiGridView();
        cachedRecentView.ItemsSource = recentItems;

        var recentLabel = new TextBlock
        {
            Text = RecentLabel,
            FontSize = LabelFontSize,
            Foreground = (Brush)Application.Current.Resources[SecondaryTextBrushKey],
        };

        var recentContainer = new StackPanel
        {
            Spacing = 4,
            Margin = new Thickness(0, 0, 0, 4),
        };
        recentContainer.Children.Add(recentLabel);
        recentContainer.Children.Add(cachedRecentView);
        cachedRecentContainer = recentContainer;

        var pickerRoot = new StackPanel
        {
            Width = PickerWidth,
            Spacing = 4,
        };
        pickerRoot.Children.Add(searchBox);
        pickerRoot.Children.Add(recentContainer);
        pickerRoot.Children.Add(cachedCategoryView);
        pickerRoot.Children.Add(cachedSearchResultsView);

        cachedFlyout = new Flyout
        {
            Content = pickerRoot,
            Placement = FlyoutPlacementMode.Bottom,
        };

        searchBox.TextChanged += (s, e) => FilterPicker(cachedSearchBox.Text);
    }

    private GridView BuildEmojiGridView()
    {
        var view = new GridView
        {
            ItemTemplate = emojiItemTemplate,
            ItemContainerStyle = emojiItemContainerStyle,
            IsItemClickEnabled = true,
            SelectionMode = ListViewSelectionMode.None,
            Padding = new Thickness(0),
        };
        ScrollViewer.SetHorizontalScrollMode(view, ScrollMode.Disabled);
        ScrollViewer.SetHorizontalScrollBarVisibility(view, ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(view, ScrollBarVisibility.Auto);
        view.ItemClick += OnEmojiItemClick;
        return view;
    }

    private static GroupStyle BuildGroupStyle()
    {
        const string headerTemplateXaml =
            "<DataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">" +
            "<TextBlock Text=\"{Binding Name}\" FontSize=\"11\" " +
            "Foreground=\"{ThemeResource TextFillColorSecondaryBrush}\" " +
            "Margin=\"0,4,0,2\"/></DataTemplate>";

        return new GroupStyle
        {
            HeaderTemplate = (DataTemplate)XamlReader.Load(headerTemplateXaml),
            HidesIfEmpty = true,
        };
    }

    private static DataTemplate BuildEmojiItemTemplate()
    {
        string templateXaml =
            "<DataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">" +
            "<Border Width=\"" + ItemSize + "\" Height=\"" + ItemSize + "\">" +
            "<TextBlock Text=\"{Binding Emoji}\" FontSize=\"" + EmojiFontSize + "\" " +
            "HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" " +
            "AutomationProperties.Name=\"{Binding Keywords}\"/></Border></DataTemplate>";

        return (DataTemplate)XamlReader.Load(templateXaml);
    }

    private static Style BuildItemContainerStyle()
    {
        var style = new Style(typeof(GridViewItem));
        style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(1)));
        style.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, 0d));
        style.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 0d));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        return style;
    }

    private void OnEmojiItemClick(object sender, ItemClickEventArgs e)
    {
        var entry = e.ClickedItem as EmojiEntry;
        if (entry == null || string.IsNullOrEmpty(entry.Emoji))
        {
            return;
        }

        if (cachedFlyout != null)
        {
            cachedFlyout.Hide();
        }

        EmojiSelected?.Invoke(this, entry.Emoji);
    }

    private void FilterPicker(string query)
    {
        bool showAll = string.IsNullOrWhiteSpace(query);

        if (showAll)
        {
            cachedCategoryView.Visibility = Visibility.Visible;
            cachedSearchResultsView.Visibility = Visibility.Collapsed;
            searchResultItems.Clear();
            return;
        }

        cachedCategoryView.Visibility = Visibility.Collapsed;
        cachedSearchResultsView.Visibility = Visibility.Visible;

        searchResultItems.Clear();
        foreach (var group in EmojiData.GetGroups())
        {
            foreach (var entry in group.Entries)
            {
                bool match = (entry.Keywords != null && entry.Keywords.Contains(query, StringComparison.OrdinalIgnoreCase))
                          || (entry.Emoji != null && entry.Emoji.Contains(query, StringComparison.OrdinalIgnoreCase));

                if (match)
                {
                    searchResultItems.Add(entry);
                }
            }
        }
    }
}
