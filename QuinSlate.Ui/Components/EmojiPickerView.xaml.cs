using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using QuinSlate.Ui.Models;
using Serilog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Windows.System;

namespace QuinSlate.Ui.Components;

/// <summary>
/// The emoji-picker surface: a search box, a pooled recent strip, and a single
/// pre-built sprite sheet (pre-rasterized emoji Images on a Canvas inside a
/// ScrollViewer). The sheet is built exactly once and never rebuilt: searching
/// repositions the existing sprites synchronously per keystroke, and scrolling
/// the static sheet is pure composition work, so no user action pays element
/// creation, container realization, or font rasterization costs.
/// </summary>
public sealed partial class EmojiPickerView : UserControl
{
    private const double NoPendingScrollOffset = -1;

    private readonly EmojiSheetPresenter sheetPresenter;
    private readonly RecentEmojiStrip recentStrip;

    private bool isInSearchMode;
    private double savedBrowseScrollOffset;
    private double pendingScrollOffset = NoPendingScrollOffset;

    /// <summary>Raised when the user picks an emoji. The argument is the emoji string.</summary>
    public event EventHandler<string> EmojiClicked;

    /// <summary>Builds the picker surface, including the entire sprite sheet, up front.</summary>
    internal EmojiPickerView(EmojiSpriteAtlas atlas)
    {
        InitializeComponent();

        var headerStyle = (Style)Resources["CategoryHeaderStyle"];

        sheetPresenter = new EmojiSheetPresenter(SheetCanvas, SheetHoverHighlight, SheetPressedHighlight, headerStyle, atlas);
        sheetPresenter.EmojiChosen += OnEmojiChosen;

        recentStrip = new RecentEmojiStrip(RecentCanvas, RecentHoverHighlight, RecentPressedHighlight, atlas);
        recentStrip.EmojiChosen += OnEmojiChosen;

        sheetPresenter.Build();

        SearchBox.TextChanged += OnSearchTextChanged;
        SearchBox.KeyDown += OnSearchBoxKeyDown;
        SheetScroller.Loaded += OnSheetScrollerLoaded;
    }

    /// <summary>
    /// Resets the picker to its initial state and refreshes the recent strip
    /// from <paramref name="recentEmoji"/>.
    /// </summary>
    public void Reset(IReadOnlyList<string> recentEmoji)
    {
        SearchBox.Text = string.Empty;

        RecentContainer.Visibility = recentStrip.SetRecents(recentEmoji)
            ? Visibility.Visible
            : Visibility.Collapsed;

        Filter(string.Empty);
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        Filter(SearchBox.Text);
    }

    private void OnSearchBoxKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter)
        {
            return;
        }

        string firstMatch = sheetPresenter.FirstMatchEmoji;
        if (isInSearchMode && !string.IsNullOrEmpty(firstMatch))
        {
            e.Handled = true;
            EmojiClicked?.Invoke(this, firstMatch);
        }
    }

    private void Filter(string query)
    {
        var stopwatch = Stopwatch.StartNew();

        if (EmojiSearch.IsBrowseQuery(query))
        {
            sheetPresenter.ShowBrowse();
            MatchesHeader.Visibility = Visibility.Collapsed;

            if (isInSearchMode)
            {
                isInSearchMode = false;
                ScrollSheetTo(savedBrowseScrollOffset);
            }

            Log.ForContext<EmojiPickerView>().Debug(
                "Emoji sheet restored to browse in {ElapsedMs:F2} ms.",
                stopwatch.Elapsed.TotalMilliseconds);
            return;
        }

        if (!isInSearchMode)
        {
            isInSearchMode = true;
            savedBrowseScrollOffset = SheetScroller.VerticalOffset;
            ScrollSheetTo(0);
        }

        int matchCount = sheetPresenter.ShowMatches(query);
        MatchesHeader.Visibility = Visibility.Visible;

        Log.ForContext<EmojiPickerView>().Debug(
            "Emoji filter applied in {ElapsedMs:F2} ms: {MatchCount} matches.",
            stopwatch.Elapsed.TotalMilliseconds,
            matchCount);
    }

    /// <summary>
    /// Scrolls the sheet without animation. ChangeView is a no-op while the
    /// scroller is disconnected (the flyout is closed), so a failed request is
    /// remembered and replayed when the scroller next loads.
    /// </summary>
    private void ScrollSheetTo(double verticalOffset)
    {
        if (SheetScroller.ChangeView(null, verticalOffset, null, true))
        {
            pendingScrollOffset = NoPendingScrollOffset;
            return;
        }

        pendingScrollOffset = verticalOffset;
    }

    private void OnSheetScrollerLoaded(object sender, RoutedEventArgs e)
    {
        if (pendingScrollOffset == NoPendingScrollOffset)
        {
            return;
        }

        double offset = pendingScrollOffset;
        pendingScrollOffset = NoPendingScrollOffset;
        DispatcherQueue.TryEnqueue(() => SheetScroller.ChangeView(null, offset, null, true));
    }

    private void OnEmojiChosen(object sender, string emoji)
    {
        if (string.IsNullOrEmpty(emoji))
        {
            return;
        }

        EmojiClicked?.Invoke(this, emoji);
    }
}
