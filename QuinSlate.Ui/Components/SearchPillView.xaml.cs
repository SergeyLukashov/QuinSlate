using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using QuinSlate.Ui.Helpers;
using System;
using Windows.System;
using Windows.UI.Core;

namespace QuinSlate.Ui.Components;

/// <summary>
/// The search pill: a floating bar at the top-right of the editor holding the query box, the
/// counter, and the previous/next/close buttons. Presentation only — it raises what the user asked
/// for and shows what it is told. <see cref="SearchController"/> decides what happens.
/// </summary>
public sealed partial class SearchPillView : UserControl
{
    private const double SideGutter = 15;

    // The editor is always narrower than the window (by its frame), so at the minimum panel width
    // the pill is clamped by the gutters on both sides rather than capped by its own width: the
    // left and right margins come out equal whatever the frame thickness.
    private const double PillWidth = MainWindow.PanelMinWidth - (2 * SideGutter);
    private const string CounterNormalState = "CounterNormal";
    private const string CounterMutedState = "CounterMuted";
    private const string DeleteButtonPartName = "DeleteButton";

    // Held so the shadow's SizeChanged subscription lives as long as the pill.
    private readonly PillDropShadow shadow;

    private string reportedQuery = string.Empty;
    private string announcedCounter;

    /// <summary>Raised as the user edits the query.</summary>
    public event EventHandler<string> QueryChanged;

    /// <summary>Raised for a step: <c>true</c> for the next match, <c>false</c> for the previous.</summary>
    public event EventHandler<bool> StepRequested;

    /// <summary>
    /// Raised when the user closes the pill: <c>true</c> from Esc, which turns the current match
    /// into the selection; <c>false</c> from the close button.
    /// </summary>
    public event EventHandler<bool> CloseRequested;

    /// <summary>Raised when the pill's bottom edge moves, so the editor's scroll margin can follow.</summary>
    public event EventHandler BottomEdgeChanged;

    /// <summary>Builds the pill, hidden.</summary>
    public SearchPillView()
    {
        InitializeComponent();

        shadow = new PillDropShadow(ShadowHost, PillHost);
        SetHiddenState();

        SizeChanged += OnSizeChanged;
        PillHost.SizeChanged += (s, e) => BottomEdgeChanged?.Invoke(this, EventArgs.Empty);
        QueryBox.TextChanged += OnQueryTextChanged;
        QueryBox.PreviewKeyDown += OnQueryPreviewKeyDown;
        QueryBox.Loaded += OnQueryBoxLoaded;
        PreviousButton.Click += (s, e) => StepRequested?.Invoke(this, false);
        NextButton.Click += (s, e) => StepRequested?.Invoke(this, true);
        CloseButton.Click += (s, e) => CloseRequested?.Invoke(this, false);
    }

    /// <summary>Whether the pill is up (or on its way up).</summary>
    public bool IsOpen { get; private set; }

    /// <summary>The query as typed.</summary>
    public string Query => QueryBox.Text;

    /// <summary>
    /// The pill's bottom edge, in DIPs below the top of the editor area: where scrolled-to text must
    /// land below to stay clear of it.
    /// </summary>
    public double BottomEdge => PillRoot.Margin.Top + PillHost.ActualHeight;

    /// <summary>Whether keyboard focus is in the pill (the box, or a button reached by Tab).</summary>
    public bool IsFocusWithin
    {
        get
        {
            if (XamlRoot == null)
            {
                return false;
            }

            var focused = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
            while (focused != null)
            {
                if (ReferenceEquals(focused, PillHost))
                {
                    return true;
                }

                focused = VisualTreeHelper.GetParent(focused);
            }

            return false;
        }
    }

    /// <summary>
    /// Shows the pill with <paramref name="query"/> in the box, focused and selected so typing
    /// replaces it. Re-focuses and re-selects when the pill is already open.
    /// </summary>
    public void Open(string query)
    {
        SetQueryFromHost(query);
        if (IsOpen == false)
        {
            IsOpen = true;
            PillRoot.Visibility = Visibility.Visible;
            PillMotion.SlideAndFade(PillSlide, ShadowSlide, PillHost, ShadowHost, -PillMotion.SlideDistance, 0, 1.0, EasingMode.EaseOut).Begin();
        }

        FocusQuery();
    }

    /// <summary>Focuses the box and selects its text.</summary>
    public void FocusQuery()
    {
        QueryBox.Focus(FocusState.Programmatic);
        QueryBox.SelectAll();
    }

    /// <summary>Hides the pill at once. The counter is cleared so a reopen never flashes stale results.</summary>
    public void Close()
    {
        if (IsOpen == false)
        {
            return;
        }

        IsOpen = false;
        PillRoot.Visibility = Visibility.Collapsed;
        SetHiddenState();
        ShowCounter(null);
    }

    /// <summary>Shows the counter, muting it when nothing matched, and announces a change.</summary>
    internal void ShowCounter(SearchCounterText counter)
    {
        if (counter == null)
        {
            CounterText.Text = string.Empty;
            AutomationProperties.SetName(CounterText, string.Empty);
            ToolTipService.SetToolTip(CounterText, null);
            VisualStateManager.GoToState(this, CounterNormalState, false);
            PreviousButton.IsEnabled = false;
            NextButton.IsEnabled = false;
            announcedCounter = null;
            return;
        }

        CounterText.Text = counter.Display;
        AutomationProperties.SetName(CounterText, counter.Accessible);
        ToolTipService.SetToolTip(CounterText, counter.ToolTip);
        VisualStateManager.GoToState(this, counter.IsMuted ? CounterMutedState : CounterNormalState, false);
        PreviousButton.IsEnabled = counter.CanStep;
        NextButton.IsEnabled = counter.CanStep;

        if (counter.Accessible != announcedCounter)
        {
            announcedCounter = counter.Accessible;
            LiveRegionAnnouncer.Announce(CounterText);
        }
    }

    private void SetQueryFromHost(string query)
    {
        reportedQuery = query ?? string.Empty;
        QueryBox.Text = reportedQuery;
    }

    // TextChanged is raised asynchronously, after a host-side set has already returned, so a host
    // update is recognised by its text rather than by a flag around the set.
    private void OnQueryTextChanged(object sender, TextChangedEventArgs e)
    {
        ReportQueryIfChanged();
    }

    private void ReportQueryIfChanged()
    {
        if (QueryBox.Text != reportedQuery)
        {
            reportedQuery = QueryBox.Text;
            QueryChanged?.Invoke(this, reportedQuery);
        }
    }

    // Enter and Esc belong to the pill while the box has focus. F3, Ctrl+F and the tab shortcuts
    // never get here: the panel's PreviewKeyDown sees them first and routes them itself.
    private void OnQueryPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            bool shift = (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift) & CoreVirtualKeyStates.Down) != 0;
            ReportQueryIfChanged();
            StepRequested?.Invoke(this, shift == false);
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Escape)
        {
            CloseRequested?.Invoke(this, true);
            e.Handled = true;
        }
    }

    // The box's own clear button would sit right beside the pill's close button, two ✕ glyphs a few
    // pixels apart that do different things. The template's visual states toggle its Visibility, so
    // it is zeroed in width instead, which no state touches. The pill starts collapsed, so the
    // template has not been measured (or applied) yet when the box loads; it is applied here first.
    private void OnQueryBoxLoaded(object sender, RoutedEventArgs e)
    {
        QueryBox.ApplyTemplate();
        Button deleteButton = VisualTreeHelpers.FindVisualChild<Button>(QueryBox, DeleteButtonPartName);
        if (deleteButton != null)
        {
            deleteButton.MaxWidth = 0;
            deleteButton.IsTabStop = false;
        }
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        PillRoot.Width = Math.Max(0, Math.Min(PillWidth, e.NewSize.Width - (2 * SideGutter)));
    }

    private void SetHiddenState()
    {
        PillHost.Opacity = 0;
        ShadowHost.Opacity = 0;
        PillSlide.Y = -PillMotion.SlideDistance;
        ShadowSlide.Y = -PillMotion.SlideDistance;
    }
}
