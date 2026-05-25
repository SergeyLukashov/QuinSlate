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
    private const double TabEmojiMarginRight = 1;
    private const double TabTitleFontSize = 13;

    /// <summary>
    /// Builds the header UI for the given <paramref name="buffer"/> and its
    /// <paramref name="tab"/> metadata.
    /// </summary>
    /// <param name="buffer">Buffer whose index is used to tag interactive controls.</param>
    /// <param name="tab">Current label state (emoji, title) for the tab.</param>
    /// <param name="onDoubleTapped">Invoked when the header is double-tapped.</param>
    /// <returns>A <see cref="TabHeaderView"/> with all named sub-elements populated.</returns>
    public static TabHeaderView Build(
        Buffer buffer,
        TabDefinition tab,
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
            FontSize = TabTitleFontSize,
        };

        var normalPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        normalPanel.Children.Add(emojiBlock);
        normalPanel.Children.Add(titleBlock);

        var headerContainer = new Grid
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
        };
        headerContainer.Children.Add(normalPanel);
        headerContainer.DoubleTapped += onDoubleTapped;

        headerContainer.SizeChanged += (s, e) =>
        {
            double newWidth = e.NewSize.Width;
            double emojiWidth = emojiBlock.ActualWidth;

            // Subtract the actual emoji width, the margin, and an additional 6px
            // of safety padding to ensure the ellipsis never overflows the right padding.
            titleBlock.MaxWidth = System.Math.Max(0, newWidth - emojiWidth - TabEmojiMarginRight - 6);
        };

        return new TabHeaderView
        {
            HeaderContainer = headerContainer,
            NormalPanel = normalPanel,
            EmojiBlock = emojiBlock,
            TitleBlock = titleBlock,
        };
    }
}
