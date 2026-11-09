using Microsoft.UI.Xaml.Controls;
using QuinSlate.Ui.Models;
using QuinSlate.Ui.Services;
using System;

namespace QuinSlate.Ui.Components;

/// <summary>
/// Renames tabs: opens the tab-edit flyout against a tab's header, and applies a saved emoji and
/// title to the header, the tab definitions, and the tab-strip layout.
/// </summary>
internal sealed class TabLabelEditor
{
    private readonly TabView tabView;
    private readonly TabHeaderRegistry headers;
    private readonly TabDefinitionStore tabStore;
    private readonly TabStripLayout layout;
    private readonly TabEditFlyout tabEditFlyout;

    /// <summary>Raised after a tab's emoji or title has been saved and applied.</summary>
    public event EventHandler LabelChanged;

    /// <summary>Creates the editor and its flyout.</summary>
    /// <param name="emojiPicker">The emoji picker the flyout hosts.</param>
    /// <param name="settingsService">The settings the flyout reads its recent emoji from.</param>
    /// <param name="tabView">The tab strip.</param>
    /// <param name="headers">The tab headers the flyout anchors to and the labels show in.</param>
    /// <param name="tabStore">The tab definitions a saved label is written to.</param>
    /// <param name="layout">Re-fits the tab titles after a label changes.</param>
    public TabLabelEditor(
        EmojiPicker emojiPicker,
        SettingsService settingsService,
        TabView tabView,
        TabHeaderRegistry headers,
        TabDefinitionStore tabStore,
        TabStripLayout layout)
    {
        if (tabView == null)
        {
            throw new ArgumentNullException(nameof(tabView));
        }

        if (headers == null)
        {
            throw new ArgumentNullException(nameof(headers));
        }

        if (tabStore == null)
        {
            throw new ArgumentNullException(nameof(tabStore));
        }

        if (layout == null)
        {
            throw new ArgumentNullException(nameof(layout));
        }

        this.tabView = tabView;
        this.headers = headers;
        this.tabStore = tabStore;
        this.layout = layout;

        tabEditFlyout = new TabEditFlyout(emojiPicker, settingsService);
        tabEditFlyout.Saved += OnTabEditSaved;
        tabEditFlyout.FlyoutClosed += OnTabEditFlyoutClosed;
    }

    /// <summary>Whether the flyout is open on buffer <paramref name="bufferIndex"/>'s tab.</summary>
    public bool IsEditing(int bufferIndex)
    {
        return tabEditFlyout.IsOpen && tabEditFlyout.EditingTabIndex == bufferIndex;
    }

    /// <summary>Opens the flyout on buffer <paramref name="bufferIndex"/>'s tab.</summary>
    public void Open(int bufferIndex)
    {
        TabDefinition currentTab = tabStore.Find(bufferIndex);
        if (currentTab == null)
        {
            return;
        }

        if (headers.HeaderContainers.TryGetValue(bufferIndex, out Grid headerContainer))
        {
            tabEditFlyout.Open(bufferIndex, currentTab, headerContainer);

            var tabItem = FindTabViewItem(bufferIndex);
            if (tabItem != null)
            {
                bool isActive = ReferenceEquals(tabView.SelectedItem, tabItem);
                if (!isActive)
                {
                    TabMenuOpenIndicator.SetVisibility(tabItem, true);
                }
            }
        }
    }

    private void OnTabEditSaved(object sender, TabEditSavedEventArgs e)
    {
        UpdateTabLabel(e.BufferIndex, e.Emoji, e.Title);
        LabelChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnTabEditFlyoutClosed(object sender, EventArgs e)
    {
        foreach (var obj in tabView.TabItems)
        {
            if (obj is TabViewItem tabItem)
            {
                TabMenuOpenIndicator.SetVisibility(tabItem, false);
            }
        }
    }

    private TabViewItem FindTabViewItem(int bufferIndex)
    {
        foreach (var obj in tabView.TabItems)
        {
            if (obj is TabViewItem tabItem && tabItem.Tag is int index && index == bufferIndex)
            {
                return tabItem;
            }
        }

        return null;
    }

    private void UpdateTabLabel(int bufferIndex, string emoji, string title)
    {
        if (headers.EmojiBlocks.TryGetValue(bufferIndex, out TextBlock emojiBlock))
        {
            emojiBlock.Text = emoji;
        }

        if (headers.TitleBlocks.TryGetValue(bufferIndex, out TextBlock titleBlock))
        {
            titleBlock.Text = title;
        }

        tabStore.UpdateLabel(bufferIndex, emoji, title);
        layout.UpdateEqualTabMaxWidth();
    }
}
