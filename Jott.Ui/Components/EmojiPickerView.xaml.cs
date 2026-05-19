using Jott.Ui.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Jott.Ui.Components;

/// <summary>
/// The emoji-picker surface: a search box, a virtualizing grouped category
/// grid, a recent strip, and a search-results grid. The category grid is
/// driven by a grouped <see cref="CollectionViewSource"/> so only the emoji
/// in view are realized.
/// </summary>
public sealed partial class EmojiPickerView : UserControl
{
    private readonly ObservableCollection<EmojiEntry> recentItems = new ObservableCollection<EmojiEntry>();
    private readonly ObservableCollection<EmojiEntry> searchResultItems = new ObservableCollection<EmojiEntry>();

    /// <summary>Raised when the user clicks an emoji. The argument is the emoji string.</summary>
    public event EventHandler<string> EmojiClicked;

    /// <summary>Builds the picker surface and binds the category, recent, and search grids.</summary>
    public EmojiPickerView()
    {
        InitializeComponent();

        EmojiGroupsSource.Source = EmojiData.GetGroups();
        CategoryView.ItemsSource = EmojiGroupsSource.View;
        RecentView.ItemsSource = recentItems;
        SearchResultsView.ItemsSource = searchResultItems;

        SearchBox.TextChanged += (s, e) => Filter(SearchBox.Text);
    }

    /// <summary>
    /// Resets the picker to its initial state and refreshes the recent strip
    /// from <paramref name="recentEmoji"/>.
    /// </summary>
    public void Reset(IReadOnlyList<string> recentEmoji)
    {
        if (recentEmoji == null)
        {
            recentEmoji = new List<string>();
        }

        SearchBox.Text = string.Empty;

        if (!RecentMatches(recentEmoji))
        {
            recentItems.Clear();
            foreach (var emoji in recentEmoji)
            {
                recentItems.Add(new EmojiEntry(emoji, emoji));
            }
        }

        RecentContainer.Visibility = recentItems.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        Filter(string.Empty);
    }

    /// <summary>
    /// Returns true when the current recent strip already shows exactly
    /// <paramref name="recentEmoji"/> in the same order, so it does not need
    /// to be cleared and rebuilt (which would retrigger entrance animations).
    /// </summary>
    private bool RecentMatches(IReadOnlyList<string> recentEmoji)
    {
        if (recentItems.Count != recentEmoji.Count)
        {
            return false;
        }

        for (int i = 0; i < recentItems.Count; i++)
        {
            if (!string.Equals(recentItems[i].Emoji, recentEmoji[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private void Filter(string query)
    {
        bool showAll = string.IsNullOrWhiteSpace(query);

        if (showAll)
        {
            CategoryView.Visibility = Visibility.Visible;
            SearchResultsContainer.Visibility = Visibility.Collapsed;
            searchResultItems.Clear();
            return;
        }

        CategoryView.Visibility = Visibility.Collapsed;
        SearchResultsContainer.Visibility = Visibility.Visible;

        searchResultItems.Clear();
        foreach (var entry in EmojiData.GetAllEntries())
        {
            bool match = (entry.Keywords != null && entry.Keywords.Contains(query, StringComparison.OrdinalIgnoreCase))
                      || (entry.Emoji != null && entry.Emoji.Contains(query, StringComparison.OrdinalIgnoreCase));

            if (match)
            {
                searchResultItems.Add(entry);
            }
        }
    }

    private void OnEmojiItemClick(object sender, ItemClickEventArgs e)
    {
        var entry = e.ClickedItem as EmojiEntry;
        if (entry == null || string.IsNullOrEmpty(entry.Emoji))
        {
            return;
        }

        EmojiClicked?.Invoke(this, entry.Emoji);
    }
}
