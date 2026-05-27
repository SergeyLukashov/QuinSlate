using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Jott.Ui.Components;

/// <summary>
/// Holds references to all named UI sub-elements that make up a single tab header.
/// Produced by <see cref="TabHeaderBuilder"/> and consumed by the buffer panel.
/// </summary>
internal sealed class TabHeaderView
{
    /// <summary>The outermost container placed as the <c>TabViewItem.Header</c>.</summary>
    public Grid HeaderContainer { get; set; }

    /// <summary>The panel shown in the normal (non-confirming) state.</summary>
    public FrameworkElement NormalPanel { get; set; }

    /// <summary>The <see cref="TextBlock"/> that displays the tab emoji.</summary>
    public TextBlock EmojiBlock { get; set; }

    /// <summary>The <see cref="TextBlock"/> that displays the tab title.</summary>
    public TextBlock TitleBlock { get; set; }
}
