using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using QuinSlate.Ui.Models;
using QuinSlate.Ui.Services;
using System;
using System.Collections.Generic;
using Buffer = QuinSlate.Ui.Models.Buffer;

namespace QuinSlate.Ui.Components;

/// <summary>
/// The buffer panel's five tabs as <see cref="TabViewItem"/>s: builds them in the persisted
/// left-to-right order, reads which buffer is selected, and hands a finished drag-reorder to the
/// tab store. A tab's buffer identity travels with it in <see cref="FrameworkElement.Tag"/>.
/// </summary>
internal sealed class BufferTabStrip
{
    private const string FallbackTabEmoji = "📋";

    private readonly TabView tabView;
    private readonly TabHeaderRegistry headers;
    private readonly TabStripLayout layout;
    private readonly TabLabelEditor labelEditor;
    private readonly TabContextMenu contextMenu;
    private readonly TabDefinitionStore tabStore;
    private readonly Style searchBadgeStyle;

    /// <summary>Creates the strip over <paramref name="tabView"/>.</summary>
    /// <param name="tabView">The panel's tab strip.</param>
    /// <param name="headers">Where each tab's header parts are registered.</param>
    /// <param name="layout">Re-fits the tab titles once a search badge has been measured.</param>
    /// <param name="labelEditor">Opens the rename flyout on a header double-tap.</param>
    /// <param name="contextMenu">Builds each tab's context menu.</param>
    /// <param name="tabStore">The tab definitions: labels, and the order a reorder is written to.</param>
    /// <param name="searchBadgeStyle">The style of each tab's search match-count badge.</param>
    public BufferTabStrip(
        TabView tabView,
        TabHeaderRegistry headers,
        TabStripLayout layout,
        TabLabelEditor labelEditor,
        TabContextMenu contextMenu,
        TabDefinitionStore tabStore,
        Style searchBadgeStyle)
    {
        if (tabView == null)
        {
            throw new ArgumentNullException(nameof(tabView));
        }

        if (headers == null)
        {
            throw new ArgumentNullException(nameof(headers));
        }

        if (layout == null)
        {
            throw new ArgumentNullException(nameof(layout));
        }

        if (labelEditor == null)
        {
            throw new ArgumentNullException(nameof(labelEditor));
        }

        if (contextMenu == null)
        {
            throw new ArgumentNullException(nameof(contextMenu));
        }

        if (tabStore == null)
        {
            throw new ArgumentNullException(nameof(tabStore));
        }

        this.tabView = tabView;
        this.headers = headers;
        this.layout = layout;
        this.labelEditor = labelEditor;
        this.contextMenu = contextMenu;
        this.tabStore = tabStore;
        this.searchBadgeStyle = searchBadgeStyle;
    }

    /// <summary>
    /// Number of tabs once the strip is fully populated. A drag-reorder momentarily removes the
    /// dragged item from <c>TabItems</c> before re-inserting it at its new slot, so a count that
    /// differs from this marks a transient mid-reorder state that no handler should act on.
    /// </summary>
    public int ExpectedTabCount { get; private set; }

    /// <summary>
    /// Replaces the strip's tabs with one per buffer, in the order <paramref name="tabDefinitions"/>
    /// describes, and selects the first.
    /// </summary>
    public void Populate(IReadOnlyList<Buffer> buffers, IReadOnlyList<TabDefinition> tabDefinitions)
    {
        ClearAllDictionaries();

        foreach (Buffer buffer in TabOrder.OrderBuffers(buffers, tabDefinitions))
        {
            TabDefinition tab = tabStore.Find(buffer.Index) ?? new TabDefinition
            {
                Id = buffer.Index,
                Emoji = FallbackTabEmoji,
                Title = buffer.Index.ToString(),
            };

            var item = BuildTabViewItem(buffer, tab);
            tabView.TabItems.Add(item);
        }

        ExpectedTabCount = tabView.TabItems.Count;

        if (tabView.TabItems.Count > 0)
        {
            tabView.SelectedIndex = 0;
        }
    }

    /// <summary>
    /// Hands the strip's live left-to-right order to the tab store, which persists it when it
    /// differs from the order already held. Returns whether it did.
    /// </summary>
    public bool TryPersistOrder()
    {
        var stripBufferIndices = new List<int>(tabView.TabItems.Count);
        foreach (var obj in tabView.TabItems)
        {
            if (obj is TabViewItem tabItem && tabItem.Tag is int bufferIndex)
            {
                stripBufferIndices.Add(bufferIndex);
            }
        }

        return tabStore.TryApplyStripOrder(stripBufferIndices, ExpectedTabCount);
    }

    /// <summary>The selected tab's buffer index.</summary>
    public int GetActiveBufferIndex()
    {
        if (tabView.SelectedItem is TabViewItem item && item.Tag is int index)
        {
            return index;
        }

        return tabView.SelectedIndex + 1;
    }

    private void ClearAllDictionaries()
    {
        tabView.TabItems.Clear();
        headers.Clear();
        contextMenu.Clear();
    }

    private TabViewItem BuildTabViewItem(Buffer buffer, TabDefinition tab)
    {
        var header = TabHeaderBuilder.Build(
            buffer,
            tab,
            onDoubleTapped: (s, e) => labelEditor.Open(buffer.Index),
            searchBadgeStyle: searchBadgeStyle);

        headers.Register(buffer.Index, header);

        // The badge's width is only known once it has been measured; the title makes room then.
        header.SearchBadge.SizeChanged += (s, e) => layout.UpdateEqualTabMaxWidth();

        // The editor is the shared WebView2 overlay, not per-tab content. A lightweight transparent
        // placeholder keeps the content presenter sized; the overlay paints over it.
        var placeholder = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        var tabItem = new TabViewItem
        {
            Header = header.HeaderContainer,
            Content = placeholder,
            IsClosable = false,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            Tag = buffer.Index,
        };
        tabItem.GettingFocus += OnTabItemGettingFocus;

        contextMenu.Attach(tabItem, buffer);
        return tabItem;
    }

    private void OnTabItemGettingFocus(UIElement sender, GettingFocusEventArgs e)
    {
        if (ReferenceEquals(e.NewFocusedElement, sender))
        {
            e.TryCancel();
        }
    }
}
