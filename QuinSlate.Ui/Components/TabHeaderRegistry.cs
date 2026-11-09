using Microsoft.UI.Xaml.Controls;
using System.Collections.Generic;

namespace QuinSlate.Ui.Components;

/// <summary>
/// Indexes the parts of each tab's header by 1-based buffer index, so the panel's collaborators can
/// reach a tab's title, emoji, or search badge without walking the tab strip.
/// </summary>
internal sealed class TabHeaderRegistry
{
    private readonly Dictionary<int, Grid> headerContainersByIndex = new Dictionary<int, Grid>();
    private readonly Dictionary<int, TextBlock> tabEmojiBlocksByIndex = new Dictionary<int, TextBlock>();
    private readonly Dictionary<int, TextBlock> tabTitleBlocksByIndex = new Dictionary<int, TextBlock>();
    private readonly Dictionary<int, InfoBadge> tabSearchBadgesByIndex = new Dictionary<int, InfoBadge>();

    /// <summary>Each tab's header container, the element set as <c>TabViewItem.Header</c>.</summary>
    public IReadOnlyDictionary<int, Grid> HeaderContainers => headerContainersByIndex;

    /// <summary>Each tab's emoji block.</summary>
    public IReadOnlyDictionary<int, TextBlock> EmojiBlocks => tabEmojiBlocksByIndex;

    /// <summary>Each tab's title block.</summary>
    public IReadOnlyDictionary<int, TextBlock> TitleBlocks => tabTitleBlocksByIndex;

    /// <summary>Each tab's search match-count badge.</summary>
    public IReadOnlyDictionary<int, InfoBadge> SearchBadges => tabSearchBadgesByIndex;

    /// <summary>Records the parts of <paramref name="header"/> under <paramref name="bufferIndex"/>.</summary>
    public void Register(int bufferIndex, TabHeaderView header)
    {
        headerContainersByIndex[bufferIndex] = header.HeaderContainer;
        tabEmojiBlocksByIndex[bufferIndex] = header.EmojiBlock;
        tabTitleBlocksByIndex[bufferIndex] = header.TitleBlock;
        tabSearchBadgesByIndex[bufferIndex] = header.SearchBadge;
    }

    /// <summary>Forgets every registered header.</summary>
    public void Clear()
    {
        headerContainersByIndex.Clear();
        tabEmojiBlocksByIndex.Clear();
        tabTitleBlocksByIndex.Clear();
        tabSearchBadgesByIndex.Clear();
    }
}
