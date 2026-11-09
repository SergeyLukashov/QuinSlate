using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Serilog;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace QuinSlate.Ui.Components;

/// <summary>
/// Runs search across the five slates (<c>Docs/Specs/24-SEARCH.md</c>) from the host's side: opening
/// and closing the pill, seeding the query, routing steps, switching tabs when the walk crosses into
/// another slate, and the tab badges. The matching itself happens in the editor page.
/// </summary>
/// <remarks>
/// The query is treated as buffer text: it is never logged. The one log line a search leaves, on
/// close, carries its length, tier, counts, and how far the user walked — nothing else.
/// </remarks>
internal sealed class SearchController
{
    private const int BadgeCap = 99;
    private const string BadgeCapText = "99+";
    private const string MatchesStatusFormat = "{0} matches";
    private const string OneMatchStatus = "1 match";

    // The SearchBadgeStates group in SearchCountBadgeStyle (Themes/BufferPanelResources.xaml).
    private const string MatchBadgeState = "Match";
    private const string CurrentBadgeState = "Current";

    // Space between the pill's bottom edge and a line scrolled into view beneath it, in DIPs.
    private const double ScrollMarginGap = 8;

    private readonly EditorHost editorHost;
    private readonly SearchPillView pill;
    private readonly TabView tabView;
    private readonly IReadOnlyDictionary<int, InfoBadge> badgesByIndex;
    private readonly Action focusEditor;
    private readonly double editorTopInset;

    private string lastQuery = string.Empty;
    private EditorSearchResultsEventArgs lastResults;
    private int steps;
    private int tabsCrossed;

    /// <summary>Raised after badges appear, change, or clear, so the tab titles can make room.</summary>
    public event EventHandler BadgesChanged;

    /// <summary>Creates the controller.</summary>
    /// <param name="editorHost">The editor bridge the search runs through.</param>
    /// <param name="pill">The search pill over the editor.</param>
    /// <param name="tabView">The tab strip, for walk order and tab switching.</param>
    /// <param name="badgesByIndex">Each tab's match-count badge, by 1-based buffer index.</param>
    /// <param name="focusEditor">Puts keyboard focus back in the editor.</param>
    /// <param name="editorTopInset">How far below the pill's parent the editor page's top edge sits, in DIPs.</param>
    public SearchController(
        EditorHost editorHost,
        SearchPillView pill,
        TabView tabView,
        IReadOnlyDictionary<int, InfoBadge> badgesByIndex,
        Action focusEditor,
        double editorTopInset)
    {
        this.editorHost = editorHost ?? throw new ArgumentNullException(nameof(editorHost));
        this.pill = pill ?? throw new ArgumentNullException(nameof(pill));
        this.tabView = tabView ?? throw new ArgumentNullException(nameof(tabView));
        this.badgesByIndex = badgesByIndex ?? throw new ArgumentNullException(nameof(badgesByIndex));
        this.focusEditor = focusEditor ?? throw new ArgumentNullException(nameof(focusEditor));
        this.editorTopInset = editorTopInset;

        pill.QueryChanged += OnQueryChanged;
        pill.StepRequested += (s, forward) => Step(forward);
        pill.CloseRequested += (s, selectCurrent) => Close(selectCurrent);
        pill.BottomEdgeChanged += (s, e) => ResendQuery();
        editorHost.Search.ResultsReceived += OnResultsReceived;
        editorHost.Search.NavigateRequested += OnNavigateRequested;

        // A reload after an editor-process failure starts the page with no search; hand it the
        // open one again.
        editorHost.Ready += (s, e) => ResendQuery();
    }

    /// <summary>Whether the pill is open.</summary>
    public bool IsOpen => pill.IsOpen;

    /// <summary>
    /// Whether keyboard focus is in the pill. A tab switch then leaves focus where it is instead of
    /// moving it into the editor, so Enter can keep stepping.
    /// </summary>
    public bool IsFocusWithin => pill.IsOpen && pill.IsFocusWithin;

    /// <summary>
    /// Ctrl+F: opens the pill (or re-focuses it) on <paramref name="seed"/> when there is one, else
    /// on the session's last query.
    /// </summary>
    public void Open(string seed)
    {
        if (string.IsNullOrEmpty(seed) == false)
        {
            lastQuery = seed;
        }

        if (IsOpen == false)
        {
            steps = 0;
            tabsCrossed = 0;
            lastResults = null;
        }

        pill.Open(lastQuery);
        SendQuery();
    }

    /// <summary>
    /// Steps to the next or previous match. With the pill closed (F3 in the editor), reopens it on
    /// the last query and steps once from the caret.
    /// </summary>
    public void Step(bool forward)
    {
        bool fromCaret = IsOpen == false;
        if (fromCaret)
        {
            Open(null);
        }

        steps++;
        editorHost.Search.SendStep(forward, fromCaret);
    }

    /// <summary>
    /// Closes the pill and returns focus to the editor. <paramref name="selectCurrent"/> (Esc) turns
    /// the current match into the selection.
    /// </summary>
    public void Close(bool selectCurrent)
    {
        if (End(selectCurrent))
        {
            focusEditor();
        }
    }

    /// <summary>
    /// Closes the pill without touching focus or the selection. The panel hiding calls this, so a
    /// re-summoned panel never opens on a stale search; the query is kept for the next Ctrl+F.
    /// </summary>
    public void Dismiss()
    {
        End(false);
    }

    /// <summary>Resends the query after a drag-reorder, so the walk follows the strip's new order.</summary>
    public void OnTabOrderChanged()
    {
        ResendQuery();
    }

    private bool End(bool selectCurrent)
    {
        if (IsOpen == false)
        {
            return false;
        }

        pill.Close();
        ShowBadges(null);
        editorHost.Search.SendClose(selectCurrent);

        Log.ForContext<SearchController>().Information(
            "Search closed: query length {QueryLength}, tier {Tier}, {Total} results, {Steps} steps, {TabsCrossed} tabs crossed.",
            lastQuery.Length,
            lastResults == null ? 0 : lastResults.Tier,
            lastResults == null ? 0 : lastResults.Total,
            steps,
            tabsCrossed);
        return true;
    }

    private void OnQueryChanged(object sender, string query)
    {
        lastQuery = query ?? string.Empty;
        SendQuery();
    }

    private void ResendQuery()
    {
        if (IsOpen)
        {
            SendQuery();
        }
    }

    private void SendQuery()
    {
        double scrollMarginTop = Math.Max(0, pill.BottomEdge + ScrollMarginGap - editorTopInset);
        editorHost.Search.SendQuery(lastQuery, ReadTabOrder(), scrollMarginTop);
    }

    private List<int> ReadTabOrder()
    {
        var order = new List<int>(tabView.TabItems.Count);
        foreach (object item in tabView.TabItems)
        {
            if (item is TabViewItem tab && tab.Tag is int bufferIndex)
            {
                order.Add(bufferIndex);
            }
        }

        return order;
    }

    private void OnResultsReceived(object sender, EditorSearchResultsEventArgs e)
    {
        // Results can land after a close; they describe a search that is already gone.
        if (IsOpen == false)
        {
            return;
        }

        lastResults = e;
        pill.ShowCounter(string.IsNullOrWhiteSpace(lastQuery)
            ? null
            : SearchCounterFormatter.Format(e.Total, e.AnyCapped, e.Tier, e.CurrentOrdinal));
        ShowBadges(e);
    }

    // Enter is the only thing that switches tabs: the walk crossed into another slate, so select it
    // through the normal tab-switch path. The page lands the view on the match once it activates.
    private void OnNavigateRequested(object sender, int bufferIndex)
    {
        TabViewItem tab = FindTab(bufferIndex);
        if (IsOpen == false || tab == null)
        {
            return;
        }

        tabsCrossed++;
        tabView.SelectedItem = tab;
    }

    private void ShowBadges(EditorSearchResultsEventArgs results)
    {
        foreach (KeyValuePair<int, InfoBadge> entry in badgesByIndex)
        {
            int count = results == null ? 0 : results.CountFor(entry.Key);
            InfoBadge badge = entry.Value;
            badge.Tag = count > BadgeCap ? BadgeCapText : count.ToString(CultureInfo.CurrentCulture);
            badge.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;

            // A hiding badge keeps its look, so the current one fades out solid on close.
            if (count > 0)
            {
                ShowBadgeState(badge, entry.Key == results.CurrentIndex);
            }

            TabViewItem tab = FindTab(entry.Key);
            if (tab != null)
            {
                AutomationProperties.SetItemStatus(tab, DescribeCount(count));
            }
        }

        BadgesChanged?.Invoke(this, EventArgs.Empty);
    }

    // A badge that has never been visible has not been measured, so its template, and the states in
    // it, do not exist yet; GoToState would fail and the state would be lost once layout applied it.
    private static void ShowBadgeState(InfoBadge badge, bool holdsCurrentMatch)
    {
        badge.ApplyTemplate();
        VisualStateManager.GoToState(badge, holdsCurrentMatch ? CurrentBadgeState : MatchBadgeState, false);
    }

    private static string DescribeCount(int count)
    {
        if (count <= 0)
        {
            return string.Empty;
        }

        return count == 1 ? OneMatchStatus : string.Format(CultureInfo.CurrentCulture, MatchesStatusFormat, count);
    }

    private TabViewItem FindTab(int bufferIndex)
    {
        foreach (object item in tabView.TabItems)
        {
            if (item is TabViewItem tab && tab.Tag is int index && index == bufferIndex)
            {
                return tab;
            }
        }

        return null;
    }
}
