using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using QuinSlate.Ui.Layout;
using QuinSlate.Ui.Models;
using Buffer = QuinSlate.Ui.Models.Buffer;

namespace QuinSlate.Ui.Components;

/// <summary>
/// Builds the header UI for a single <see cref="TabViewItem"/>.
/// Returns a <see cref="TabHeaderView"/> that carries all named sub-elements
/// so that the owning panel can index them without depending on the
/// internal layout of the header.
/// </summary>
internal static class TabHeaderBuilder
{
    private const double TabEmojiSize = 16;
    private const double TabTitleFontSize = 13;

    // Without this the emoji inherits the UI font and DirectWrite's fallback resolves
    // enclosed-alphanumeric emoji (U+1F170..U+1F19A, e.g. 🅰️🅱️🅾️) to the monochrome
    // Segoe UI Symbol glyph rather than the colour one. TrayPeekPanel.xaml pins the same font.
    private const string EmojiFontFamily = "Segoe UI Emoji";

    private const int EmojiColumn = 0;
    private const int TitleColumn = 1;
    private const int SearchBadgeColumn = 2;

    // Segoe UI Emoji draws its ink ~1 DIP below the centre of its 16px line box, so a badge
    // centred on that box rides high. A 2 DIP top margin moves the badge's centre down 1 DIP.
    private const double SearchBadgeTopMargin = 2;

    /// <summary>
    /// Builds the header UI for the given <paramref name="buffer"/> and its
    /// <paramref name="tab"/> metadata.
    /// </summary>
    /// <param name="buffer">Buffer whose index is used to tag interactive controls.</param>
    /// <param name="tab">Current label state (emoji, title) for the tab.</param>
    /// <param name="onDoubleTapped">Invoked when the header is double-tapped.</param>
    /// <param name="searchBadgeStyle">The style of the search match-count badge.</param>
    /// <returns>A <see cref="TabHeaderView"/> with all named sub-elements populated.</returns>
    public static TabHeaderView Build(
        Buffer buffer,
        TabDefinition tab,
        DoubleTappedEventHandler onDoubleTapped,
        Style searchBadgeStyle)
    {
        var emojiBlock = new TextBlock
        {
            Text = tab.Emoji,
            FontSize = TabEmojiSize,
            FontFamily = new FontFamily(EmojiFontFamily),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, TabStripCalculator.TabEmojiMarginRight, 0),
        };

        var titleBlock = new TextBlock
        {
            Text = tab.Title,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            TextTrimming = TextTrimming.CharacterEllipsis,
            FontSize = TabTitleFontSize,
        };

        // Pinned to the tab's right edge, never truncated: the title's MaxWidth leaves room for
        // it, so the title ellipsizes first.
        var searchBadge = new InfoBadge
        {
            Style = searchBadgeStyle,
            Visibility = Visibility.Collapsed,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(TabStripCalculator.TabSearchBadgeGap, SearchBadgeTopMargin, TabStripCalculator.TabSearchBadgeMarginRight, 0),
        };
        SearchBadgeMotion.Attach(searchBadge);

        var normalPanel = new Grid
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        normalPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        normalPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        normalPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        Grid.SetColumn(emojiBlock, EmojiColumn);
        Grid.SetColumn(titleBlock, TitleColumn);
        Grid.SetColumn(searchBadge, SearchBadgeColumn);

        normalPanel.Children.Add(emojiBlock);
        normalPanel.Children.Add(titleBlock);
        normalPanel.Children.Add(searchBadge);

        var headerContainer = new Grid
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
        };
        headerContainer.Children.Add(normalPanel);
        headerContainer.DoubleTapped += onDoubleTapped;

        return new TabHeaderView
        {
            HeaderContainer = headerContainer,
            EmojiBlock = emojiBlock,
            TitleBlock = titleBlock,
            SearchBadge = searchBadge,
        };
    }
}
