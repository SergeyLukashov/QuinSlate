using Jott.Ui.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Buffer = Jott.Ui.Models.Buffer;

namespace Jott.Ui.Components;

/// <summary>
/// Builds the header UI for a single <see cref="TabViewItem"/>.
/// Returns a <see cref="TabHeaderView"/> that carries all named sub-elements
/// so that the owning panel can index them without depending on the
/// internal layout of the header.
/// </summary>
internal static class TabHeaderBuilder
{
    private const double TabEmojiSize = 16;
    private const double TabEmojiMarginRight = 4;
    private const double TabTitleMaxWidth = 80;
    private const double EditButtonSize = 20;
    private const double EditGlyphSize = 11;
    private const string EditGlyph = "";

    /// <summary>
    /// Builds the header UI for the given <paramref name="buffer"/> and its
    /// <paramref name="tab"/> metadata.
    /// </summary>
    /// <param name="buffer">Buffer whose index is used to tag interactive controls.</param>
    /// <param name="tab">Current label state (emoji, title) for the tab.</param>
    /// <param name="onEditClicked">Invoked when the edit (pencil) button is clicked.</param>
    /// <param name="onDoubleTapped">Invoked when the header is double-tapped.</param>
    /// <returns>A <see cref="TabHeaderView"/> with all named sub-elements populated.</returns>
    public static TabHeaderView Build(
        Buffer buffer,
        TabDefinition tab,
        RoutedEventHandler onEditClicked,
        DoubleTappedEventHandler onDoubleTapped)
    {
        var emojiBlock = new TextBlock
        {
            Text = tab.Emoji,
            FontSize = TabEmojiSize,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, TabEmojiMarginRight, 0),
        };

        var titleBlock = new TextBlock
        {
            Text = tab.Title,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = TabTitleMaxWidth,
        };

        var editButton = new Button
        {
            Width = EditButtonSize,
            Height = EditButtonSize,
            Padding = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Tag = buffer.Index,
            Visibility = Visibility.Collapsed,
            Content = new FontIcon { Glyph = EditGlyph, FontSize = EditGlyphSize },
        };
        editButton.Click += onEditClicked;
        var leftPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        leftPanel.Children.Add(emojiBlock);
        leftPanel.Children.Add(titleBlock);

        var normalPanel = new Grid
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        normalPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        normalPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(leftPanel, 0);
        Grid.SetColumn(editButton, 1);
        normalPanel.Children.Add(leftPanel);
        normalPanel.Children.Add(editButton);

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
            NormalPanel = normalPanel,
            EditButton = editButton,
            EmojiBlock = emojiBlock,
            TitleBlock = titleBlock,
        };
    }
}
