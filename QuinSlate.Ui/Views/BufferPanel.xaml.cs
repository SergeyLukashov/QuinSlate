using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using QuinSlate.Ui.Components;
using QuinSlate.Ui.Models;
using QuinSlate.Ui.Services;
using System;
using System.Collections.Generic;
using Windows.Foundation.Collections;
using Buffer = QuinSlate.Ui.Models.Buffer;

namespace QuinSlate.Ui.Views;

/// <summary>
/// The 5-tab buffer UI surface. Each tab has a user-editable emoji + title header; all five buffers
/// are edited in a single <see cref="EditorHost"/> (a WebView2 hosting CodeMirror 6, one CM6 state
/// per buffer). The web editor is a fixed overlay covering the TabView content area; tab switches
/// tell the page which buffer state to activate.
///
/// Title-bar layout: the pin, minimize, maximize/restore, and close buttons are a top-level
/// overlay anchored to the window's top-right corner, so they can never be clipped by the
/// TabView's internal columns. The TabView's TabStripFooter holds a fixed-width transparent
/// spacer reserving exactly the overlay cluster's width so the tabs stop where the buttons begin.
/// </summary>
/// <remarks>
/// The panel composes the pieces and routes between them: <see cref="BufferTabStrip"/> and
/// <see cref="TabStripLayout"/> own the tabs, <see cref="EditorPane"/> owns the editor, and
/// <see cref="SearchController"/> owns search.
/// </remarks>
public sealed partial class BufferPanel : UserControl
{
    private const string PinGlyph = "";
    private const string PinnedGlyph = "";
    private const string PinTooltip = "Keep window always on top";
    private const string UnpinTooltip = "Stop keeping window on top";

    // ChromeMaximize / ChromeRestore in Segoe Fluent Icons, the glyphs the system caption
    // buttons use, so the custom title bar reads as the standard one.
    private const string MaximizeGlyph = "";
    private const string RestoreGlyph = "";
    private const string MaximizeTooltip = "Maximize";
    private const string RestoreTooltip = "Restore down";
    private const string SearchBadgeStyleKey = "SearchCountBadgeStyle";

    private readonly TabHeaderRegistry tabHeaders = new TabHeaderRegistry();
    private readonly EmojiPicker emojiPicker = new EmojiPicker();
    private readonly EditorPane editorPane;

    private BufferTabStrip tabStrip;
    private TabStripLayout tabStripLayout;
    private EditorOverlayPositioner editorOverlay;
    private TabLabelEditor labelEditor;
    private TabContextMenu tabContextMenu;
    private BufferKeyboardController keyboardController;
    private EditorKeyCommandRouter keyCommandRouter;

    /// <summary>Search across the five slates: the pill, the tab badges, and tab switching.</summary>
    private SearchController searchController;

    /// <summary>
    /// Holds keyboard focus in the editor when a click lands on chrome that has nothing focusable
    /// under it.
    /// </summary>
    private EditorFocusGuard editorFocusGuard;

    /// <summary>
    /// Logs where each press lands on the editor element, to be read against the page's own click
    /// log (see <c>Docs/Investigations/07-EDITOR-CLICK-CARET-LINE-OFFSET.md</c>). Diagnostic only.
    /// </summary>
    private EditorPointerProbe editorPointerProbe;

    /// <summary>
    /// Raised when the user clicks the pin button. The caller should toggle
    /// the pinned state and call <see cref="SetPinned"/> to update the icon.
    /// </summary>
    public event EventHandler PinToggleRequested;

    /// <summary>
    /// Raised when the user clicks the minimize button. The caller should minimize the window
    /// to the taskbar — deliberately not the same as <see cref="CloseRequested"/>, which hides
    /// it to the tray.
    /// </summary>
    public event EventHandler MinimizeRequested;

    /// <summary>
    /// Raised when the user clicks the maximize/restore button. The caller should toggle the
    /// window between maximized and restored and call <see cref="SetMaximized"/> to update the
    /// glyph.
    /// </summary>
    public event EventHandler MaximizeRestoreRequested;

    /// <summary>
    /// Raised when the user clicks the close button. The caller should hide the
    /// window (QuinSlate hides to the tray rather than terminating).
    /// </summary>
    public event EventHandler CloseRequested;

    /// <summary>
    /// Raised when a tab label (emoji or title) is saved by the user. The caller
    /// should refresh the tray tooltip when this event fires.
    /// </summary>
    public event EventHandler TabLabelChanged;

    /// <summary>
    /// Raised once, when the editor page reports its first composited frame. The window is
    /// still cloaked (or, past the reveal deadline, showing the flat startup cover); the caller
    /// should uncloak and then call <see cref="BeginStartupEntrance"/> to run the visible
    /// hand-off.
    /// </summary>
    public event EventHandler EditorFirstPainted;

    /// <summary>
    /// Raised once, when the startup cover drops: the editor's reveal entrance has started (or
    /// the bounded fallback fired, including on a WebView2 creation failure). This is the point
    /// the launch-critical rendering is done, so deferred warm-up work that would otherwise
    /// compete with it for the UI thread and GPU is scheduled from here.
    /// </summary>
    public event EventHandler StartupRenderSettled;

    /// <summary>
    /// Asks the editor page to run the startup reveal entrance (fade + slide from opacity
    /// zero, caret-blink hold released). Called by the window right after it has uncloaked;
    /// the page confirms on a frame presented while visible and the startup cover then drops
    /// onto the animation's flat first frames — the sequencing that keeps Chromium's
    /// present-while-cloaked throttling from popping the text in after the reveal.
    /// </summary>
    public void BeginStartupEntrance()
    {
        editorPane.BeginEntrance();
    }

    /// <summary>
    /// The element to pass to <c>Window.SetTitleBar</c> as the drag region.
    /// </summary>
    public FrameworkElement TitleBarDragArea => TitleBarIconDragArea;

    /// <summary>
    /// Creates the panel. <see cref="Initialise"/> must be called before the
    /// panel is shown.
    /// </summary>
    public BufferPanel()
    {
        InitializeComponent();
        editorPane = new EditorPane(EditorWebView, EditorStartupCover, RootGrid, this, LimitNoticeSurface);
    }

    /// <summary>
    /// Updates the pin button checked state and icon to reflect <paramref name="isPinned"/>.
    /// </summary>
    public void SetPinned(bool isPinned)
    {
        PinIcon.Glyph = isPinned ? PinnedGlyph : PinGlyph;
        ToolTipService.SetToolTip(PinButton, isPinned ? UnpinTooltip : PinTooltip);
    }

    /// <summary>
    /// Updates the maximize/restore button glyph and tooltip to reflect
    /// <paramref name="isMaximized"/>. Driven by the window rather than by the button's own
    /// click, because the state also changes without the button — snap layouts, Win+Up, and a
    /// double-click on the title-bar drag area all maximize or restore behind the panel's back.
    /// </summary>
    public void SetMaximized(bool isMaximized)
    {
        MaximizeIcon.Glyph = isMaximized ? RestoreGlyph : MaximizeGlyph;
        ToolTipService.SetToolTip(MaximizeButton, isMaximized ? RestoreTooltip : MaximizeTooltip);
    }

    /// <summary>
    /// Places keyboard focus into the active buffer's editor so the user can type immediately after
    /// the panel is shown. When the editor page is not yet ready, focus is deferred until it reports
    /// <c>ready</c> (only the latest request wins, since there is a single shared editor).
    /// </summary>
    public void FocusActiveEditor()
    {
        editorPane.Focus();
    }

    /// <summary>
    /// Wires the panel to the given services and populates the five tabs.
    /// </summary>
    public void Initialise(
        BufferService bufferService,
        IReadOnlyList<Buffer> buffers,
        SettingsService settingsService,
        IReadOnlyList<TabDefinition> tabDefinitions)
    {
        if (bufferService == null)
        {
            throw new ArgumentNullException(nameof(bufferService));
        }

        if (buffers == null)
        {
            throw new ArgumentNullException(nameof(buffers));
        }

        if (settingsService == null)
        {
            throw new ArgumentNullException(nameof(settingsService));
        }

        if (tabDefinitions == null)
        {
            throw new ArgumentNullException(nameof(tabDefinitions));
        }

        var tabStore = new TabDefinitionStore(settingsService, tabDefinitions);
        tabStripLayout = new TabStripLayout(BufferTabView, TitleBarIconDragArea, TitleBarFooterSpacer, tabHeaders);
        editorOverlay = new EditorOverlayPositioner(BufferTabView, RootGrid, EditorHostLayer);

        labelEditor = new TabLabelEditor(emojiPicker, settingsService, BufferTabView, tabHeaders, tabStore, tabStripLayout);
        labelEditor.LabelChanged += (s, e) => TabLabelChanged?.Invoke(this, EventArgs.Empty);

        keyboardController = new BufferKeyboardController(BufferTabView);
        keyboardController.EditFlyoutRequested += (s, bufferIndex) => labelEditor.Open(bufferIndex);
        keyboardController.SearchOpenRequested += (s, e) => searchController?.Open(null);
        keyboardController.SearchStepRequested += (s, forward) => searchController?.Step(forward);

        tabContextMenu = new TabContextMenu(BufferTabView, labelEditor, editorPane.ClearBuffer);
        tabStrip = new BufferTabStrip(
            BufferTabView,
            tabHeaders,
            tabStripLayout,
            labelEditor,
            tabContextMenu,
            tabStore,
            Resources[SearchBadgeStyleKey] as Style);
        tabStrip.Populate(buffers, tabDefinitions);

        BufferTabView.TabItemsChanged += OnTabItemsChanged;
        BufferTabView.SelectionChanged += OnBufferTabSelectionChanged;
        BufferTabView.SizeChanged += OnBufferTabViewSizeChanged;
        BufferTabView.Loaded += OnBufferTabViewLoaded;
        RootGrid.PreviewKeyDown += OnPanelPreviewKeyDown;

        editorPane.CreateHost(bufferService, buffers, tabStrip.GetActiveBufferIndex, tabContextMenu.UpdateClearButtonState);
        editorPane.KeyCommandReceived += OnEditorKeyCommand;
        editorPane.FirstPainted += (s, e) => EditorFirstPainted?.Invoke(this, EventArgs.Empty);
        editorPane.RenderSettled += OnStartupRenderSettled;
        editorPane.Host.SearchOpenRequested += (s, seed) => searchController.Open(seed);

        searchController = new SearchController(
            editorPane.Host,
            SearchPill,
            BufferTabView,
            tabHeaders.SearchBadges,
            FocusActiveEditor,
            EditorWebView.Margin.Top);
        searchController.BadgesChanged += (s, e) => tabStripLayout.UpdateEqualTabMaxWidth();
        keyCommandRouter = new EditorKeyCommandRouter(keyboardController, searchController);
        editorFocusGuard = new EditorFocusGuard(EditorWebView, BufferTabView);
        editorPointerProbe = new EditorPointerProbe(EditorWebView);

        // A flat mid-tone stands in on every surface until the dithered mesh swaps in (the window
        // via the fallback brush here; the editor via the WebView2 DefaultBackgroundColor set on
        // the editor host below). Never the XAML linear gradient, which bands on this field.
        editorPane.ApplyFallbackBackground();
        Loaded += OnPanelLoaded;
        ActualThemeChanged += OnPanelActualThemeChanged;
        editorPane.TrackSystemColorChanges();
        RootGrid.SizeChanged += OnRootGridSizeChanged;

        editorPane.Start();

        tabStripLayout.UpdateEqualTabMaxWidth();
        tabStripLayout.RecomputeFirstTabLeadingGap();
    }

    /// <summary>
    /// Closes any open search without moving focus. Called when the panel hides, so a re-summoned
    /// panel never opens on a stale search; the last query is kept for the next Ctrl+F.
    /// </summary>
    public void CloseSearch()
    {
        searchController?.Dismiss();
    }

    /// <summary>
    /// Drops the character-limit notice and re-arms it, so the next clamp is acknowledged
    /// immediately. Called when the notice stops being about what the user is looking at: a tab
    /// switch, or the panel hiding.
    /// </summary>
    public void ResetLimitNotice()
    {
        editorPane.ResetLimitNotice();
    }

    /// <summary>
    /// Asks the editor page to push any pending content immediately. Called on shutdown, before the
    /// buffer service's own flush. The host-side mirror (fed by the debounced/blur/hide content sync)
    /// is the authoritative flush source; this is a best-effort nudge.
    /// </summary>
    public void FlushPendingContent()
    {
        editorPane.RequestFlush();
    }

    private void OnBufferTabViewSizeChanged(object sender, SizeChangedEventArgs e)
    {
        tabStripLayout.UpdateEqualTabMaxWidth();
        tabStripLayout.PinRightContentColumnReservation();
        tabStripLayout.ResetStripScrollWhenTabsFit();
        editorOverlay.Position();
    }

    private void OnBufferTabViewLoaded(object sender, RoutedEventArgs e)
    {
        tabStripLayout.PinRightContentColumnReservation();
        editorOverlay.Position();
    }

    private void OnBufferTabSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SyncActiveBuffer();
    }

    /// <summary>
    /// Points the shared editor at the selected tab's buffer and focuses it.
    /// </summary>
    /// <remarks>
    /// Ignored while the strip is mid-reorder: pulling the dragged item out of <c>TabItems</c>
    /// moves the selection onto whichever tab shifted into its slot, and activating that buffer
    /// would swap the editor's text — and replay its entrance animation — under the user's pointer,
    /// only to swap back on drop. A drop that lands the same buffer back under the selection is
    /// likewise a no-op rather than a re-activation.
    /// </remarks>
    private void SyncActiveBuffer()
    {
        if (BufferTabView.TabItems.Count != tabStrip.ExpectedTabCount)
        {
            return;
        }

        if (!(BufferTabView.SelectedItem is TabViewItem selected) || !(selected.Tag is int bufferIndex))
        {
            return;
        }

        editorPane.ActivateBuffer(bufferIndex);

        // While the search box has focus a tab switch leaves it there, so Enter keeps stepping and
        // Ctrl+1-5 leaves the pill in charge.
        if (searchController == null || searchController.IsFocusWithin == false)
        {
            FocusActiveEditor();
        }
    }

    /// <summary>
    /// Runs when the tab strip's item vector changes, which for QuinSlate only ever means a
    /// drag-reorder finished (tabs are never added or removed). Fires twice per reorder — once for
    /// the dragged item's removal, once for its re-insertion — so the half-populated first pass is
    /// skipped rather than persisted as a four-tab strip.
    /// </summary>
    private void OnTabItemsChanged(TabView sender, IVectorChangedEventArgs args)
    {
        if (tabStrip.ExpectedTabCount == 0 || BufferTabView.TabItems.Count != tabStrip.ExpectedTabCount)
        {
            return;
        }

        // The vector also fills in as the strip is first realized, raising a Reset plus one
        // insertion per tab; only an actual change of order is worth reacting to.
        if (!tabStrip.TryPersistOrder())
        {
            return;
        }

        tabStripLayout.RecomputeFirstTabLeadingGap();
        SyncActiveBuffer();
        tabStripLayout.ResetStripScrollWhenTabsFit();
        searchController?.OnTabOrderChanged();
    }

    private void OnPanelLoaded(object sender, RoutedEventArgs e)
    {
        editorPane.ApplyDitheredBackground();
        editorOverlay.Position();
    }

    private void OnPanelActualThemeChanged(FrameworkElement sender, object args)
    {
        editorPane.ApplyThemeChange();
    }

    private void OnRootGridSizeChanged(object sender, SizeChangedEventArgs e)
    {
        editorOverlay.Position();
        editorPane.ScheduleBackgroundRebuild();
    }

    private void OnStartupRenderSettled(object sender, EventArgs e)
    {
        // The launch-critical rendering is done; warm-ups may now use the idle UI thread. The
        // emoji picker prewarm runs here (not on Loaded) for the same reason the peek warm-up
        // waits for StartupRenderSettled: at Low priority it still interleaves with the
        // WebView2/CodeMirror cold start and stretches the time to a usable editor.
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => emojiPicker.Prewarm(RootGrid.XamlRoot));
        StartupRenderSettled?.Invoke(this, EventArgs.Empty);
    }

    private void OnPinButtonClicked(object sender, RoutedEventArgs e)
    {
        PinToggleRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnMinimizeButtonClicked(object sender, RoutedEventArgs e)
    {
        MinimizeRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnMaximizeButtonClicked(object sender, RoutedEventArgs e)
    {
        MaximizeRestoreRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnCloseButtonClicked(object sender, RoutedEventArgs e)
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnEditorKeyCommand(object sender, string command)
    {
        if (keyCommandRouter == null)
        {
            return;
        }

        keyCommandRouter.Route(command);
    }

    private void OnPanelPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        keyboardController.HandlePanelPreviewKey(e);
    }
}
