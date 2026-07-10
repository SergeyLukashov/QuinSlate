using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using QuinSlate.Ui.Components;
using QuinSlate.Ui.Helpers;
using QuinSlate.Ui.Interop;
using QuinSlate.Ui.Layout;
using QuinSlate.Ui.Models;
using QuinSlate.Ui.Services;
using System;
using System.Collections.Generic;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI;
using Windows.UI.ViewManagement;
using Buffer = QuinSlate.Ui.Models.Buffer;

namespace QuinSlate.Ui.Views;

/// <summary>
/// The 5-tab buffer UI surface. Each tab has a user-editable emoji + title header; all five buffers
/// are edited in a single <see cref="EditorHost"/> (a WebView2 hosting CodeMirror 6, one CM6 state
/// per buffer). The web editor is a fixed overlay covering the TabView content area; tab switches
/// tell the page which buffer state to activate.
///
/// Title-bar layout: the pin and close buttons are a top-level overlay anchored to the
/// window's top-right corner, so they can never be clipped by the TabView's internal
/// columns. The TabView's TabStripFooter holds a fixed-width transparent spacer reserving
/// exactly the overlay cluster's width so the tabs stop where the buttons begin.
/// </summary>
public sealed partial class BufferPanel : UserControl
{
    private const string PinGlyph = "";
    private const string PinnedGlyph = "";
    private const string PinTooltip = "Keep window always on top";
    private const string UnpinTooltip = "Stop keeping window on top";
    private const string RenameTabMenuText = "Rename tab";
    private const string RenameTabIconGlyph = "";
    private const string FluentIconFontFamily = "Segoe Fluent Icons";
    private const string ClearTabMenuText = "Clear tab";
    private const string ClearTabIconGlyph = "";
    private const string TabContentPresenterPartName = "TabContentPresenter";

    // Panel-shortcut command names forwarded from the CodeMirror keymap over the bridge.
    private const string KeyCommandCycleNext = "cycle-next";
    private const string KeyCommandCyclePrev = "cycle-prev";
    private const string KeyCommandSelectPrefix = "select-";
    private const string KeyCommandEditFlyout = "edit-flyout";

    // Context-menu command names sent to the editor page.
    private const string EditorCommandUndo = "undo";
    private const string EditorCommandRedo = "redo";
    private const string EditorCommandCut = "cut";
    private const string EditorCommandCopy = "copy";
    private const string EditorCommandSelectAll = "selectAll";

    // WinUI's TextControlForeground resolves to TextFillColorPrimaryBrush, the foreground the retired
    // RichEditBox inherited. Dark is opaque white; light is black at 0xE4 alpha, *not* an opaque near-
    // black. The alpha matters: it lets the warm gradient mesh tint the glyphs instead of flattening
    // them to a neutral grey, and it must survive the trip across the bridge as rgba().
    private static readonly Color EditorTextColorDark = Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);
    private static readonly Color EditorTextColorLight = Color.FromArgb(0xE4, 0x00, 0x00, 0x00);

    // Must stay rooted in a field: UISettings raises ColorValuesChanged off a weak reference, so a
    // collected instance silently stops delivering accent changes.
    private readonly UISettings uiSettings = new UISettings();

    /// <summary>
    /// Name of the floating pill <c>Border</c> template part inside the TabViewItem
    /// ControlTemplate whose left margin carries the inter-tab gap.
    /// </summary>
    private const string TabBackgroundPartName = "TabBackground";

    /// <summary>
    /// Name of the column container <c>Grid</c> in the SDK <c>TabView</c> template that hosts
    /// <c>LeftContentColumn</c>, <c>TabColumn</c>, <c>AddButtonColumn</c>, and
    /// <c>RightContentColumn</c>.
    /// </summary>
    private const string TabContainerGridPartName = "TabContainerGrid";

    /// <summary>
    /// Name of the <c>ScrollViewer</c> template part inside the SDK <c>TabView</c> template that
    /// scrolls the tab strip and whose <c>ComputedHorizontalScrollBarVisibility</c> the two scroll
    /// buttons' containers are template-bound to.
    /// </summary>
    private const string TabStripScrollViewerPartName = "ScrollViewer";

    private TabViewItem firstTabWithZeroedGap;

    /// <summary>
    /// The tab strip's <c>ScrollViewer</c> template part, resolved once and kept so its
    /// <c>ViewChanged</c> stays hooked for the panel's lifetime.
    /// </summary>
    private ScrollViewer tabStripScrollViewer;

    /// <summary>
    /// Number of tabs once the strip is fully populated. A drag-reorder momentarily removes the
    /// dragged item from <c>TabItems</c> before re-inserting it at its new slot, so a count that
    /// differs from this marks a transient mid-reorder state that no handler should act on.
    /// </summary>
    private int expectedTabCount;

    /// <summary>
    /// Buffer index last handed to <see cref="EditorHost.Activate"/>, so a selection change that
    /// resolves to the same buffer does not re-activate it (the page replays the tab-content
    /// entrance animation on every activation).
    /// </summary>
    private int lastActivatedBufferIndex = -1;

    private BufferService bufferService;
    private SettingsService settingsService;
    private IReadOnlyList<TabDefinition> tabDefinitions;
    private IReadOnlyList<Buffer> initialBuffers;

    private readonly Dictionary<int, Grid> headerContainersByIndex = new Dictionary<int, Grid>();
    private readonly Dictionary<int, MenuFlyoutItem> clearMenuItemsByIndex = new Dictionary<int, MenuFlyoutItem>();
    private readonly Dictionary<int, TextBlock> tabEmojiBlocksByIndex = new Dictionary<int, TextBlock>();
    private readonly Dictionary<int, TextBlock> tabTitleBlocksByIndex = new Dictionary<int, TextBlock>();

    private readonly EmojiPicker emojiPicker = new EmojiPicker();
    private EditorHost editorHost;
    private EditorContextMenu editorContextMenu;
    private bool wantFocusOnReady;

    private TabEditFlyout tabEditFlyout;
    private BufferKeyboardController keyboardController;
    private bool preventMenuClosing;
    private DateTime clearTransitionTime;
    private const int ClearConfirmCooldownMs = 500;

    /// <summary>
    /// Debounces rebuilding the dithered background surfaces while the window is being resized.
    /// The surfaces must be re-rendered at the new native pixel size (a stretched dithered bitmap
    /// re-bands), but doing so on every <c>SizeChanged</c> tick would thrash, so the rebuild is
    /// coalesced to the end of the resize.
    /// </summary>
    private DispatcherTimer ditheredRebuildTimer;
    private const int DitheredRebuildDebounceMs = 90;

    /// <summary>
    /// Fallback that drops the startup cover even if the page never reports its first paint (e.g. a
    /// WebView2 creation failure), so the editor area can never stay stuck showing the flat cover.
    /// </summary>
    private DispatcherTimer startupCoverTimer;
    private const int StartupCoverFallbackMs = 2500;

    /// <summary>
    /// True while a one-shot retry of <see cref="ApplyDitheredBackground"/> is pending because the
    /// editor host was not laid out yet (see <see cref="ScheduleDitheredRetry"/>).
    /// </summary>
    private bool isDitheredRetryScheduled;

    /// <summary>
    /// Raised when the user clicks the pin button. The caller should toggle
    /// the pinned state and call <see cref="SetPinned"/> to update the icon.
    /// </summary>
    public event EventHandler PinToggleRequested;

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
    /// Places keyboard focus into the active buffer's editor so the user can type immediately after
    /// the panel is shown. When the editor page is not yet ready, focus is deferred until it reports
    /// <c>ready</c> (only the latest request wins, since there is a single shared editor).
    /// </summary>
    public void FocusActiveEditor()
    {
        wantFocusOnReady = true;
        if (editorHost == null)
        {
            return;
        }

        EditorWebView.Focus(FocusState.Programmatic);
        editorHost.FocusEditor();
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

        this.bufferService = bufferService;
        this.settingsService = settingsService;
        this.tabDefinitions = tabDefinitions;
        this.initialBuffers = buffers;

        tabEditFlyout = new TabEditFlyout(emojiPicker, settingsService);
        tabEditFlyout.Saved += OnTabEditSaved;
        tabEditFlyout.FlyoutClosed += OnTabEditFlyoutClosed;

        keyboardController = new BufferKeyboardController(BufferTabView);
        keyboardController.EditFlyoutRequested += OnEditFlyoutRequested;

        editorContextMenu = new EditorContextMenu(
            onUndo: () => editorHost?.SendCommand(EditorCommandUndo),
            onRedo: () => editorHost?.SendCommand(EditorCommandRedo),
            onCut: () => editorHost?.SendCommand(EditorCommandCut),
            onCopy: () => editorHost?.SendCommand(EditorCommandCopy),
            onPaste: OnEditorPasteRequested,
            onSelectAll: () => editorHost?.SendCommand(EditorCommandSelectAll));

        ClearAllDictionaries();

        foreach (Buffer buffer in OrderBuffersByTabOrder(buffers, tabDefinitions))
        {
            TabDefinition tab = FindTabDefinition(buffer.Index) ?? new TabDefinition
            {
                Id = buffer.Index,
                Emoji = "📋",
                Title = buffer.Index.ToString(),
            };

            var item = BuildTabViewItem(buffer, tab);
            BufferTabView.TabItems.Add(item);
        }

        expectedTabCount = BufferTabView.TabItems.Count;

        if (BufferTabView.TabItems.Count > 0)
        {
            BufferTabView.SelectedIndex = 0;
        }

        BufferTabView.TabItemsChanged += OnTabItemsChanged;
        BufferTabView.SelectionChanged += OnBufferTabSelectionChanged;
        BufferTabView.SizeChanged += OnBufferTabViewSizeChanged;
        BufferTabView.Loaded += OnBufferTabViewLoaded;
        RootGrid.PreviewKeyDown += OnPanelPreviewKeyDown;

        editorHost = new EditorHost(EditorWebView, bufferService.AppDataDirectory);
        editorHost.Ready += OnEditorReady;
        editorHost.ContentSynced += OnEditorContentSynced;
        editorHost.KeyCommandReceived += OnEditorKeyCommand;
        editorHost.ContextMenuRequested += OnEditorContextMenuRequested;
        editorHost.Painted += OnEditorPainted;

        // A flat mid-tone stands in on every surface until the dithered mesh swaps in (the window
        // via the fallback brush here; the editor via the WebView2 DefaultBackgroundColor set on
        // the editor host below). Never the XAML linear gradient, which bands on this field.
        ApplyFallbackBackground();
        Loaded += OnPanelLoaded;
        ActualThemeChanged += OnPanelActualThemeChanged;
        uiSettings.ColorValuesChanged += OnColorValuesChanged;
        RootGrid.SizeChanged += OnRootGridSizeChanged;

        bool isDark = ActualTheme == ElementTheme.Dark;
        _ = editorHost.InitializeAsync(DitheredGradientBrushFactory.MidColor(isDark));

        UpdateEqualTabMaxWidth();
        RecomputeFirstTabLeadingGap();
    }

    /// <summary>
    /// Returns <paramref name="buffers"/> in the left-to-right order the persisted
    /// <paramref name="tabs"/> describe. Any buffer no tab definition names is appended in buffer
    /// order, so a truncated or hand-edited <c>settings.json</c> can never drop a buffer's tab.
    /// </summary>
    private static List<Buffer> OrderBuffersByTabOrder(IReadOnlyList<Buffer> buffers, IReadOnlyList<TabDefinition> tabs)
    {
        var byIndex = new Dictionary<int, Buffer>(buffers.Count);
        foreach (Buffer buffer in buffers)
        {
            byIndex[buffer.Index] = buffer;
        }

        var ordered = new List<Buffer>(buffers.Count);
        foreach (TabDefinition tab in tabs)
        {
            if (byIndex.TryGetValue(tab.Id, out Buffer buffer) && !ordered.Contains(buffer))
            {
                ordered.Add(buffer);
            }
        }

        foreach (Buffer buffer in buffers)
        {
            if (!ordered.Contains(buffer))
            {
                ordered.Add(buffer);
            }
        }

        return ordered;
    }

    private void OnBufferTabViewSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateEqualTabMaxWidth();
        PinRightContentColumnReservation();
        ResetStripScrollWhenTabsFit();
        PositionEditorHost();
    }

    private void OnBufferTabViewLoaded(object sender, RoutedEventArgs e)
    {
        PinRightContentColumnReservation();
        PositionEditorHost();
    }

    /// <summary>
    /// Positions the editor host overlay to cover exactly the TabView's content presenter, so the
    /// tab strip stays interactive above it. Walks the live template (matching the panel's other
    /// template-part lookups) and sets margins from the presenter's bounds relative to the root.
    /// </summary>
    private void PositionEditorHost()
    {
        if (EditorHostLayer == null)
        {
            return;
        }

        ContentPresenter presenter = VisualTreeHelpers.FindVisualChild<ContentPresenter>(BufferTabView, TabContentPresenterPartName);
        if (presenter == null || presenter.ActualWidth <= 0 || presenter.ActualHeight <= 0)
        {
            return;
        }

        GeneralTransform transform = presenter.TransformToVisual(RootGrid);
        Point origin = transform.TransformPoint(new Point(0, 0));

        double right = RootGrid.ActualWidth - origin.X - presenter.ActualWidth;
        double bottom = RootGrid.ActualHeight - origin.Y - presenter.ActualHeight;
        EditorHostLayer.Margin = new Thickness(origin.X, origin.Y, Math.Max(0, right), Math.Max(0, bottom));
    }

    /// <summary>
    /// Pins the SDK <c>TabView</c> template's right-content column to a fixed pixel width equal
    /// to the overlay button cluster, so the <c>TabStripFooter</c> reservation survives overflow
    /// mode. In the default template that column is <c>Width="*"</c> and collapses to ~0 once
    /// the tabs overflow and the Auto <c>TabColumn</c> consumes all available width; the SDK
    /// then stops reserving the cluster and the tabs + scroll-forward button render under the
    /// top-right pin/close overlay. Walking the live visual tree (rather than copying the SDK
    /// template) is intentional: a full template copy is pinned to whatever <c>WindowsAppSDK</c>
    /// ships and crashes the app when parts diverge. The right-content column is always the
    /// last column inside <c>TabContainerGrid</c>. The reservation is re-applied on every
    /// <c>SizeChanged</c> to survive the SDK's own column-width updates.
    /// </summary>
    private void PinRightContentColumnReservation()
    {
        Grid container = VisualTreeHelpers.FindVisualChild<Grid>(BufferTabView, TabContainerGridPartName);
        if (container == null || container.ColumnDefinitions.Count == 0)
        {
            return;
        }

        ColumnDefinition rightColumn = container.ColumnDefinitions[container.ColumnDefinitions.Count - 1];
        var target = new GridLength(TabStripCalculator.TitleBarFooterFallbackWidth, GridUnitType.Pixel);
        if (rightColumn.Width.GridUnitType != target.GridUnitType || rightColumn.Width.Value != target.Value)
        {
            rightColumn.Width = target;
        }
    }

    /// <summary>
    /// Sizes every tab to the equal share of the live tab-strip width, so the tabs fill the row
    /// exactly and shrink as the window narrows. The share is applied to each tab's <em>header</em>
    /// (the tab sizes to its content under <c>TabWidthMode="SizeToContent"</c>), which is why the
    /// tabs come out equal without the SDK stamping a width on them — see the sizing note in
    /// BufferPanel.xaml for why Equal mode is not used. The share is floored at
    /// <see cref="TabStripCalculator.TabMinWidth"/> so that once the tabs can no longer fit at
    /// their minimum the strip overflows and surfaces its scroll buttons instead of clipping. The
    /// matching title <c>MaxWidth</c> is derived from the same stable share.
    /// </summary>
    private void UpdateEqualTabMaxWidth()
    {
        int count = BufferTabView.TabItems.Count;
        if (count == 0)
        {
            return;
        }

        double totalWidth = BufferTabView.ActualWidth;
        if (totalWidth <= 0)
        {
            return;
        }

        double headerWidth = TabStripCalculator.TitleBarHeaderFallbackWidth;
        if (TitleBarIconDragArea != null && TitleBarIconDragArea.ActualWidth > 0)
        {
            headerWidth = TitleBarIconDragArea.ActualWidth;
        }

        double footerWidth = TabStripCalculator.TitleBarFooterFallbackWidth;
        if (TitleBarFooterSpacer != null && TitleBarFooterSpacer.ActualWidth > 0)
        {
            footerWidth = TitleBarFooterSpacer.ActualWidth;
        }

        double perTab = TabStripCalculator.ComputePerTabMaxWidth(totalWidth, headerWidth, footerWidth, count);
        SuppressScrollButtonsWhenTabsFit(perTab);

        foreach (var obj in BufferTabView.TabItems)
        {
            if (obj is TabViewItem item)
            {
                item.MaxWidth = perTab;
            }
        }

        double headerWidthEach = TabStripCalculator.ComputeHeaderWidth(perTab);
        foreach (var entry in headerContainersByIndex)
        {
            if (entry.Value != null)
            {
                entry.Value.Width = headerWidthEach;
            }
        }

        foreach (var entry in tabTitleBlocksByIndex)
        {
            TextBlock titleBlock = entry.Value;
            if (titleBlock == null)
            {
                continue;
            }

            double emojiWidth = TabStripCalculator.TabEmojiFallbackWidth;
            if (tabEmojiBlocksByIndex.TryGetValue(entry.Key, out TextBlock emojiBlock)
                && emojiBlock != null
                && emojiBlock.ActualWidth > 0)
            {
                emojiWidth = emojiBlock.ActualWidth;
            }

            titleBlock.MaxWidth = TabStripCalculator.ComputeTitleMaxWidth(perTab, emojiWidth);
        }
    }

    /// <summary>
    /// Hides the tab strip's scroll buttons whenever the five tabs fit at more than their minimum
    /// width, and hands visibility back to the SDK once they are squeezed to that floor and the
    /// strip must really scroll.
    /// </summary>
    /// <remarks>
    /// The buttons' containers are template-bound to the strip <c>ScrollViewer</c>'s
    /// <c>ComputedHorizontalScrollBarVisibility</c>, and the tabs are sized to fill the strip
    /// exactly. A drag-reorder lifts the dragged tab out of <c>TabItems</c> and puts it back a
    /// frame later; during that gap the strip measures as scrollable and the buttons appear. They
    /// then occupy a column each, which shrinks the viewport below the tabs' width — so the strip
    /// stays "scrollable" and the buttons stay up, shifting every tab sideways for the whole drag
    /// and snapping back on drop. Pinning the scroll-bar visibility to <c>Hidden</c> while the tabs
    /// fit denies that latch its first step. <c>Hidden</c> rather than <c>Disabled</c> keeps the
    /// strip scrollable by wheel and by keyboard.
    /// </remarks>
    private void SuppressScrollButtonsWhenTabsFit(double perTabWidth)
    {
        ScrollViewer scrollViewer = ResolveStripScrollViewer();
        if (scrollViewer == null)
        {
            return;
        }

        bool tabsFit = perTabWidth > TabStripCalculator.TabMinWidth;
        ScrollBarVisibility target = tabsFit ? ScrollBarVisibility.Hidden : ScrollBarVisibility.Auto;
        if (scrollViewer.HorizontalScrollBarVisibility != target)
        {
            scrollViewer.HorizontalScrollBarVisibility = target;
        }
    }

    /// <summary>
    /// Resolves the tab strip's <c>ScrollViewer</c> from the live template, hooking its
    /// <c>ViewChanged</c> the first time it is found.
    /// </summary>
    private ScrollViewer ResolveStripScrollViewer()
    {
        if (tabStripScrollViewer == null)
        {
            tabStripScrollViewer = VisualTreeHelpers.FindVisualChild<ScrollViewer>(BufferTabView, TabStripScrollViewerPartName);
            if (tabStripScrollViewer != null)
            {
                tabStripScrollViewer.ViewChanged += OnStripViewChanged;
            }
        }

        return tabStripScrollViewer;
    }

    private void OnStripViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        if (e.IsIntermediate)
        {
            return;
        }

        ResetStripScrollWhenTabsFit();
    }

    /// <summary>
    /// Scrolls the tab strip back to its left edge whenever the five tabs are meant to fit, so the
    /// row always starts where the tab strip starts.
    /// </summary>
    /// <remarks>
    /// The tabs are sized to fill the strip, but each one lands a fraction of a pixel wider than
    /// its computed share once layout snaps it to whole device pixels, so the strip's content
    /// measures a handful of DIPs wider than its viewport and is technically scrollable at rest.
    /// Nothing scrolls it in normal use — but a drag-reorder does, and the strip then stays parked
    /// at that offset, drawing every tab a few pixels left of where it belongs. (It looks like it
    /// heals itself when you switch tabs only because the SDK scrolls the newly selected tab back
    /// into view.) This is the counterpart of <see cref="SuppressScrollButtonsWhenTabsFit"/>: in
    /// the regime where the strip presents itself as unscrollable, it must not sit scrolled either.
    /// Below the per-tab floor the strip genuinely scrolls and the offset is the user's to keep.
    ///
    /// Enforced from the strip's own <c>ViewChanged</c> rather than at the end of a reorder: the
    /// SDK scrolls the dropped tab into view after the tab collection has settled, so a one-shot
    /// reset driven off the collection change is overwritten a moment later.
    /// </remarks>
    private void ResetStripScrollWhenTabsFit()
    {
        ScrollViewer scrollViewer = ResolveStripScrollViewer();
        if (scrollViewer == null || scrollViewer.HorizontalScrollBarVisibility != ScrollBarVisibility.Hidden)
        {
            return;
        }

        if (scrollViewer.HorizontalOffset > 0)
        {
            scrollViewer.ChangeView(0.0, null, null, true);
        }
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
        if (BufferTabView.TabItems.Count != expectedTabCount)
        {
            return;
        }

        if (!(BufferTabView.SelectedItem is TabViewItem selected) || !(selected.Tag is int bufferIndex))
        {
            return;
        }

        // The tab-content entrance animation is replayed inside the editor page when it activates
        // the buffer (a WebView2's own opacity cannot be animated from XAML without flashing black).
        if (editorHost != null && bufferIndex != lastActivatedBufferIndex)
        {
            lastActivatedBufferIndex = bufferIndex;
            editorHost.Activate(bufferIndex);
        }

        FocusActiveEditor();
    }

    /// <summary>
    /// Runs when the tab strip's item vector changes, which for QuinSlate only ever means a
    /// drag-reorder finished (tabs are never added or removed). Fires twice per reorder — once for
    /// the dragged item's removal, once for its re-insertion — so the half-populated first pass is
    /// skipped rather than persisted as a four-tab strip.
    /// </summary>
    private void OnTabItemsChanged(TabView sender, IVectorChangedEventArgs args)
    {
        if (expectedTabCount == 0 || BufferTabView.TabItems.Count != expectedTabCount)
        {
            return;
        }

        // The vector also fills in as the strip is first realized, raising a Reset plus one
        // insertion per tab; only an actual change of order is worth reacting to.
        if (!PersistTabOrder())
        {
            return;
        }

        RecomputeFirstTabLeadingGap();
        SyncActiveBuffer();
        ResetStripScrollWhenTabsFit();
    }

    private void ClearAllDictionaries()
    {
        BufferTabView.TabItems.Clear();
        headerContainersByIndex.Clear();
        clearMenuItemsByIndex.Clear();
        tabEmojiBlocksByIndex.Clear();
        tabTitleBlocksByIndex.Clear();
    }

    private TabViewItem BuildTabViewItem(Buffer buffer, TabDefinition tab)
    {
        var header = TabHeaderBuilder.Build(
            buffer,
            tab,
            onDoubleTapped: (s, e) => OpenEditFlyout(buffer.Index));

        headerContainersByIndex[buffer.Index] = header.HeaderContainer;
        tabEmojiBlocksByIndex[buffer.Index] = header.EmojiBlock;
        tabTitleBlocksByIndex[buffer.Index] = header.TitleBlock;

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

        var menuFlyout = new MenuFlyout();
        var presenterStyle = new Style(typeof(MenuFlyoutPresenter));
        presenterStyle.Setters.Add(new Setter(Control.MinWidthProperty, 150.0));
        menuFlyout.MenuFlyoutPresenterStyle = presenterStyle;

        var renameItem = new MenuFlyoutItem
        {
            Text = RenameTabMenuText,
            Icon = new FontIcon
            {
                Glyph = RenameTabIconGlyph,
                FontFamily = new FontFamily(FluentIconFontFamily),
            }
        };
        renameItem.Click += (s, e) => OpenEditFlyout(buffer.Index);
        menuFlyout.Items.Add(renameItem);

        var clearIcon = new FontIcon
        {
            Glyph = ClearTabIconGlyph,
            FontFamily = new FontFamily(FluentIconFontFamily),
        };

        var clearItem = new MenuFlyoutItem
        {
            Text = ClearTabMenuText,
            Icon = clearIcon,
            IsEnabled = !string.IsNullOrEmpty(buffer.Content),
            RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5),
        };

        var itemTranslate = new TranslateTransform();
        clearItem.RenderTransform = itemTranslate;
        clearItem.Click += (s, e) => OnClearItemClick(buffer.Index, clearItem);
        menuFlyout.Items.Add(clearItem);
        clearMenuItemsByIndex[buffer.Index] = clearItem;

        // Forces the arrow cursor the instant the menu's windowed popup appears so the
        // busy/app-starting cursor never shows. See microsoft-ui-xaml#8829.
        menuFlyout.Opened += (s, e) =>
        {
            ForceArrowCursor();
            bool isActive = ReferenceEquals(BufferTabView.SelectedItem, tabItem);
            if (!isActive)
            {
                SetMenuOpenIndicatorVisibility(tabItem, true);
            }
        };

        menuFlyout.Closing += (s, e) =>
        {
            if (preventMenuClosing)
            {
                e.Cancel = true;
                preventMenuClosing = false;
            }
        };

        menuFlyout.Closed += (s, e) =>
        {
            ResetClearMenuItem(buffer.Index);
            if (tabEditFlyout == null || !tabEditFlyout.IsOpen || tabEditFlyout.EditingTabIndex != buffer.Index)
            {
                SetMenuOpenIndicatorVisibility(tabItem, false);
            }
        };

        tabItem.ContextFlyout = menuFlyout;
        return tabItem;
    }

    /// <summary>
    /// Sets the system arrow cursor for the current frame to suppress the busy/app-starting
    /// cursor that WinUI shows over a freshly-opened flyout popup (microsoft-ui-xaml#8829).
    /// The handle returned for a system cursor is shared and OS-cached, so it is never freed.
    /// </summary>
    private void ForceArrowCursor()
    {
        NativeMethods.SetCursor(NativeMethods.LoadCursor(IntPtr.Zero, NativeMethods.IDC_ARROW));
    }

    private void OnPanelLoaded(object sender, RoutedEventArgs e)
    {
        ApplyDitheredBackground();
        PositionEditorHost();
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => emojiPicker.Prewarm(RootGrid.XamlRoot));
    }

    private void OnPanelActualThemeChanged(FrameworkElement sender, object args)
    {
        ApplyDitheredBackground();
    }

    private void OnRootGridSizeChanged(object sender, SizeChangedEventArgs e)
    {
        PositionEditorHost();

        if (ditheredRebuildTimer == null)
        {
            ditheredRebuildTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(DitheredRebuildDebounceMs),
            };
            ditheredRebuildTimer.Tick += OnDitheredRebuildTimerTick;
        }

        ditheredRebuildTimer.Stop();
        ditheredRebuildTimer.Start();
    }

    private void OnDitheredRebuildTimerTick(object sender, object e)
    {
        ditheredRebuildTimer.Stop();
        ApplyDitheredBackground();
    }

    private void OnEditorReady(object sender, EventArgs e)
    {
        var payload = new List<KeyValuePair<int, string>>();
        if (initialBuffers != null)
        {
            foreach (Buffer buffer in initialBuffers)
            {
                payload.Add(new KeyValuePair<int, string>(buffer.Index, buffer.Content ?? string.Empty));
            }
        }

        editorHost.InitBuffers(payload);
        lastActivatedBufferIndex = GetActiveBufferIndex();
        editorHost.Activate(lastActivatedBufferIndex);
        ApplyDitheredBackground();
        StartStartupCoverFallback();

        if (wantFocusOnReady)
        {
            EditorWebView.Focus(FocusState.Programmatic);
            editorHost.FocusEditor();
        }
    }

    private void StartStartupCoverFallback()
    {
        if (startupCoverTimer != null || EditorStartupCover == null || EditorStartupCover.Visibility != Visibility.Visible)
        {
            return;
        }

        startupCoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(StartupCoverFallbackMs) };
        startupCoverTimer.Tick += (s, e) => DropStartupCover();
        startupCoverTimer.Start();
    }

    /// <summary>
    /// Builds the dithered gradient at each surface's native pixel size and applies it to the window
    /// background (a brush) and the editor page (a PNG shown 1:1) in the same pass. The swap is
    /// all-or-nothing: until the editor page is ready and its bitmap is built, every surface keeps
    /// the flat mid-tone (the window fallback brush and the WebView2 DefaultBackgroundColor), so no
    /// window-mesh-before-editor-mesh snap can occur.
    /// </summary>
    private async void ApplyDitheredBackground()
    {
        if (editorHost == null || editorHost.IsReady == false)
        {
            ApplyFallbackBackground();
            return;
        }

        // The foreground colours do not depend on the gradient bitmap, so send them first. Gating them
        // on the bitmap would leave the page on the previous theme's text colour whenever the bitmap
        // is late (editor not yet laid out) or fails outright.
        ApplyEditorThemeColors();

        DitheredBackground background = await DitheredGradientBrushFactory.CreateElementBackgroundAsync(EditorWebView);
        if (background == null)
        {
            ApplyFallbackBackground();
            ScheduleDitheredRetry();
            return;
        }

        ImageBrush windowBrush = DitheredGradientBrushFactory.CreateForElement(RootGrid);
        if (windowBrush != null)
        {
            RootGrid.Background = windowBrush;
        }

        editorHost.SetBackground(background);
    }

    /// <summary>
    /// Re-runs <see cref="ApplyDitheredBackground"/> once the editor host can provide a size, on its
    /// next <c>SizeChanged</c>.
    /// </summary>
    private void ScheduleDitheredRetry()
    {
        if (isDitheredRetryScheduled)
        {
            return;
        }

        isDitheredRetryScheduled = true;
        SizeChangedEventHandler onSized = null;
        onSized = (s, e) =>
        {
            EditorWebView.SizeChanged -= onSized;
            isDitheredRetryScheduled = false;
            ApplyDitheredBackground();
        };
        EditorWebView.SizeChanged += onSized;
    }

    /// <summary>
    /// Paints the window with the flat mid-tone of the gradient mesh. Applied synchronously before
    /// the panel is first shown (and whenever the dithered mesh is not yet available), so the first
    /// composited frame is a uniform flat tone rather than the banding XAML linear-gradient
    /// fallback. The editor's own flat tone comes from the WebView2 DefaultBackgroundColor.
    /// </summary>
    private void ApplyFallbackBackground()
    {
        bool isDark = ActualTheme == ElementTheme.Dark;
        Color mid = DitheredGradientBrushFactory.MidColor(isDark);
        RootGrid.Background = new SolidColorBrush(mid);

        // Keep the startup cover matched to the mid-tone until the page reports its first paint, so
        // dropping it is imperceptible (mid-tone -> the barely-different mesh).
        if (EditorStartupCover != null && EditorStartupCover.Visibility == Visibility.Visible)
        {
            EditorStartupCover.Background = new SolidColorBrush(mid);
        }
    }

    private void OnEditorPainted(object sender, EventArgs e)
    {
        DropStartupCover();
    }

    /// <summary>
    /// Hides the flat startup cover once the editor page has painted its first real frame (or after
    /// a bounded fallback delay), revealing the gradient + text without the black WebView2 swapchain
    /// ever showing. Idempotent.
    /// </summary>
    private void DropStartupCover()
    {
        if (startupCoverTimer != null)
        {
            startupCoverTimer.Stop();
            startupCoverTimer = null;
        }

        if (EditorStartupCover != null)
        {
            EditorStartupCover.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Sends the editor's per-theme colours (text, caret, selection, calc accent) to the page. Read
    /// from the current theme so the surface matches the RichEditBox foreground, and the Windows
    /// accent so selection and the calc highlight match the rest of the shell.
    /// </summary>
    private void ApplyEditorThemeColors()
    {
        if (editorHost == null)
        {
            return;
        }

        bool isDark = ActualTheme == ElementTheme.Dark;
        Color text = isDark ? EditorTextColorDark : EditorTextColorLight;

        // The RichEditBox took its SelectionHighlightColor from TextControlSelectionHighlightColor,
        // which resolves to AccentFillColorSelectedTextBackgroundBrush -> the opaque SystemAccentColor.
        // CodeMirror paints .cm-selectionBackground beneath the text, so an opaque fill reproduces it.
        Color accent = ReadAccentColor();
        editorHost.SetTheme(text, text, accent, accent);
    }

    /// <summary>
    /// Re-sends the editor colours when the user changes their Windows accent (or the system theme)
    /// while the app is running. Raised on a background thread, so it must hop to the UI thread.
    /// </summary>
    private void OnColorValuesChanged(UISettings sender, object args)
    {
        DispatcherQueue.TryEnqueue(ApplyEditorThemeColors);
    }

    /// <summary>
    /// The live Windows accent colour. Read straight from <see cref="UISettings"/> rather than the
    /// <c>SystemAccentColor</c> XAML resource: <c>ColorValuesChanged</c> can fire before XAML has
    /// refreshed its theme resources, so the resource may still hold the previous accent.
    /// </summary>
    private Color ReadAccentColor()
    {
        return uiSettings.GetColorValue(UIColorType.Accent);
    }

    private int GetActiveBufferIndex()
    {
        if (BufferTabView.SelectedItem is TabViewItem item && item.Tag is int index)
        {
            return index;
        }

        return BufferTabView.SelectedIndex + 1;
    }

    private void OnTabItemGettingFocus(UIElement sender, GettingFocusEventArgs e)
    {
        if (ReferenceEquals(e.NewFocusedElement, sender))
        {
            e.TryCancel();
        }
    }

    private void OnEditFlyoutRequested(object sender, int bufferIndex)
    {
        OpenEditFlyout(bufferIndex);
    }

    private void OpenEditFlyout(int bufferIndex)
    {
        if (tabEditFlyout == null)
        {
            return;
        }

        TabDefinition currentTab = FindTabDefinition(bufferIndex);
        if (currentTab == null)
        {
            return;
        }

        if (headerContainersByIndex.TryGetValue(bufferIndex, out Grid headerContainer))
        {
            tabEditFlyout.Open(bufferIndex, currentTab, headerContainer);

            var tabItem = FindTabViewItem(bufferIndex);
            if (tabItem != null)
            {
                bool isActive = ReferenceEquals(BufferTabView.SelectedItem, tabItem);
                if (!isActive)
                {
                    SetMenuOpenIndicatorVisibility(tabItem, true);
                }
            }
        }
    }

    private void OnTabEditSaved(object sender, TabEditSavedEventArgs e)
    {
        UpdateTabLabel(e.BufferIndex, e.Emoji, e.Title);
        TabLabelChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnTabEditFlyoutClosed(object sender, EventArgs e)
    {
        foreach (var obj in BufferTabView.TabItems)
        {
            if (obj is TabViewItem tabItem)
            {
                SetMenuOpenIndicatorVisibility(tabItem, false);
            }
        }
    }

    private TabViewItem FindTabViewItem(int bufferIndex)
    {
        foreach (var obj in BufferTabView.TabItems)
        {
            if (obj is TabViewItem tabItem && tabItem.Tag is int index && index == bufferIndex)
            {
                return tabItem;
            }
        }

        return null;
    }

    private void SetMenuOpenIndicatorVisibility(TabViewItem tabItem, bool visible)
    {
        if (tabItem == null)
        {
            return;
        }

        Border indicator = VisualTreeHelpers.FindVisualChild<Border>(tabItem, "MenuOpenIndicator");
        if (indicator != null)
        {
            indicator.Opacity = visible ? 1 : 0;
        }

        Border activeIndicator = VisualTreeHelpers.FindVisualChild<Border>(tabItem, "ActiveIndicator");
        if (activeIndicator != null)
        {
            activeIndicator.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    /// <summary>
    /// Writes the strip's live left-to-right order back to <c>settings.json</c>, where the tab
    /// array's order <em>is</em> the tab order, so a reorder survives relaunch. Returns
    /// <c>true</c> only when the strip's order actually differs from the order already held, so
    /// that neither the strip's initial realization nor a drag the user drops where it started
    /// triggers a settings write.
    /// </summary>
    private bool PersistTabOrder()
    {
        if (settingsService == null)
        {
            return false;
        }

        var reordered = new List<TabDefinition>(BufferTabView.TabItems.Count);
        foreach (var obj in BufferTabView.TabItems)
        {
            var tabItem = obj as TabViewItem;
            if (tabItem == null)
            {
                continue;
            }

            if (tabItem.Tag is int bufferIndex)
            {
                TabDefinition found = FindTabDefinition(bufferIndex);
                if (found != null)
                {
                    reordered.Add(found);
                }
            }
        }

        if (reordered.Count != expectedTabCount || OrderMatches(reordered, tabDefinitions))
        {
            return false;
        }

        tabDefinitions = reordered;
        settingsService.SetTabs(reordered);
        return true;
    }

    private static bool OrderMatches(IReadOnlyList<TabDefinition> left, IReadOnlyList<TabDefinition> right)
    {
        if (right == null || left.Count != right.Count)
        {
            return false;
        }

        for (int i = 0; i < left.Count; i++)
        {
            if (left[i].Id != right[i].Id)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Removes the leading inter-tab gap from the FIRST tab only so the tab strip is
    /// symmetric: the first tab's pill must start flush at the left rather than leaving an
    /// <see cref="TabStripCalculator.InterTabGapLeft"/> dead strip after the left edge / scroll-back button,
    /// and because no tab carries a trailing margin the rightmost-visible tab always abuts
    /// the right edge / scroll-forward button. The gap stays intact between all other tabs.
    /// Restores the gap on whichever tab was previously first (the first item changes after
    /// a reorder), then zeroes the left margin on the new first tab's <c>TabBackground</c>
    /// pill. When the new first tab's template is not yet realized, the work is deferred
    /// until that item raises its <c>Loaded</c> event.
    /// </summary>
    private void RecomputeFirstTabLeadingGap()
    {
        int count = BufferTabView.TabItems.Count;
        TabViewItem newFirst = null;
        if (count > 0 && BufferTabView.TabItems[0] is TabViewItem candidate)
        {
            newFirst = candidate;
        }

        if (firstTabWithZeroedGap != null && !ReferenceEquals(firstTabWithZeroedGap, newFirst))
        {
            SetTabBackgroundLeftMargin(firstTabWithZeroedGap, TabStripCalculator.InterTabGapLeft);
            firstTabWithZeroedGap = null;
        }

        if (newFirst == null)
        {
            return;
        }

        if (SetTabBackgroundLeftMargin(newFirst, 0))
        {
            firstTabWithZeroedGap = newFirst;
            return;
        }

        RoutedEventHandler onLoaded = null;
        onLoaded = (s, e) =>
        {
            newFirst.Loaded -= onLoaded;
            if (ReferenceEquals(BufferTabView.TabItems.Count > 0
                ? BufferTabView.TabItems[0]
                : null, newFirst))
            {
                if (SetTabBackgroundLeftMargin(newFirst, 0))
                {
                    firstTabWithZeroedGap = newFirst;
                }
            }
        };
        newFirst.Loaded += onLoaded;
    }

    /// <summary>
    /// Finds the <c>TabBackground</c> pill Border inside <paramref name="item"/>'s realized
    /// template and sets its left margin to <paramref name="leftMargin"/>, leaving top,
    /// right, and bottom untouched. Returns <c>false</c> when the template part is not yet
    /// realized so the caller can defer.
    /// </summary>
    private bool SetTabBackgroundLeftMargin(TabViewItem item, double leftMargin)
    {
        if (item == null)
        {
            return false;
        }

        Border pill = VisualTreeHelpers.FindVisualChild<Border>(item, TabBackgroundPartName);
        if (pill == null)
        {
            return false;
        }

        Thickness current = pill.Margin;
        pill.Margin = new Thickness(leftMargin, current.Top, current.Right, current.Bottom);
        return true;
    }

    private void UpdateTabLabel(int bufferIndex, string emoji, string title)
    {
        if (tabEmojiBlocksByIndex.TryGetValue(bufferIndex, out TextBlock emojiBlock))
        {
            emojiBlock.Text = emoji;
        }

        if (tabTitleBlocksByIndex.TryGetValue(bufferIndex, out TextBlock titleBlock))
        {
            titleBlock.Text = title;
        }

        if (tabDefinitions == null)
        {
            return;
        }

        var updated = new List<TabDefinition>();
        foreach (var td in tabDefinitions)
        {
            if (td.Id == bufferIndex)
            {
                updated.Add(new TabDefinition { Id = td.Id, Emoji = emoji, Title = title });
            }
            else
            {
                updated.Add(new TabDefinition { Id = td.Id, Emoji = td.Emoji, Title = td.Title });
            }
        }

        tabDefinitions = updated;

        if (settingsService != null)
        {
            settingsService.SetTabs(updated);
        }

        UpdateEqualTabMaxWidth();
    }

    private void OnPinButtonClicked(object sender, RoutedEventArgs e)
    {
        PinToggleRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnCloseButtonClicked(object sender, RoutedEventArgs e)
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnClearItemClick(int bufferIndex, MenuFlyoutItem clearItem)
    {
        if (clearItem.Text == ClearTabMenuText)
        {
            clearItem.Text = "Confirm clear";
            if (clearItem.Icon is FontIcon fontIcon)
            {
                fontIcon.Glyph = ""; // Checkmark glyph
            }

            if (clearItem.RenderTransform is TranslateTransform translate)
            {
                var duration = new Duration(TimeSpan.FromMilliseconds(250));
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

                var translateXAnim = new DoubleAnimation { From = -20.0, To = 0.0, Duration = duration, EasingFunction = ease };

                Storyboard.SetTarget(translateXAnim, translate);
                Storyboard.SetTargetProperty(translateXAnim, "X");

                var sb = new Storyboard();
                sb.Children.Add(translateXAnim);
                sb.Begin();
            }

            clearTransitionTime = DateTime.UtcNow;
            preventMenuClosing = true;
        }
        else
        {
            if ((DateTime.UtcNow - clearTransitionTime).TotalMilliseconds < ClearConfirmCooldownMs)
            {
                preventMenuClosing = true;
                return;
            }

            OnClearConfirmed(bufferIndex);
        }
    }

    private void ResetClearMenuItem(int bufferIndex)
    {
        if (clearMenuItemsByIndex.TryGetValue(bufferIndex, out MenuFlyoutItem clearItem))
        {
            clearItem.Text = ClearTabMenuText;
            if (clearItem.Icon is FontIcon fontIcon)
            {
                fontIcon.Glyph = ClearTabIconGlyph;
            }
        }
    }

    private void OnClearConfirmed(int bufferIndex)
    {
        if (editorHost != null)
        {
            editorHost.SetText(bufferIndex, string.Empty);
        }

        if (bufferService != null)
        {
            bufferService.UpdateContent(bufferIndex, string.Empty);
        }

        UpdateClearButtonState(bufferIndex, isEmpty: true);
        ResetClearMenuItem(bufferIndex);
    }

    private async void OnEditorPasteRequested()
    {
        // Text-only, host-driven paste: reuses the STA WinRT clipboard path; the page clamps the
        // insertion to the cap and normalises line endings, matching the retired paste path.
        if (editorHost == null)
        {
            return;
        }

        DataPackageView dataView = Clipboard.GetContent();
        if (!dataView.Contains(StandardDataFormats.Text))
        {
            return;
        }

        string text = await dataView.GetTextAsync();
        editorHost.InsertText(text);
    }

    private void OnEditorContentSynced(object sender, EditorContentEventArgs e)
    {
        if (bufferService == null)
        {
            return;
        }

        // The page sends "\n"-joined text; the buffer file uses CRLF. Convert and trim trailing
        // breaks exactly as the retired ExtractDirtyBuffers did.
        string text = e.Text ?? string.Empty;
        text = text.Replace("\n", "\r\n").TrimEnd('\r', '\n');

        bufferService.UpdateContent(e.Index, text);
        UpdateClearButtonState(e.Index, isEmpty: string.IsNullOrEmpty(text));
    }

    private void OnEditorKeyCommand(object sender, string command)
    {
        if (keyboardController == null)
        {
            return;
        }

        if (command == KeyCommandCycleNext)
        {
            keyboardController.CycleBuffer(1);
        }
        else if (command == KeyCommandCyclePrev)
        {
            keyboardController.CycleBuffer(-1);
        }
        else if (command == KeyCommandEditFlyout)
        {
            keyboardController.RequestEditFlyout();
        }
        else if (command.StartsWith(KeyCommandSelectPrefix, StringComparison.Ordinal))
        {
            if (int.TryParse(command.Substring(KeyCommandSelectPrefix.Length), out int oneBased))
            {
                keyboardController.SelectBuffer(oneBased - 1);
            }
        }
    }

    private void OnEditorContextMenuRequested(object sender, EditorContextMenuEventArgs e)
    {
        if (editorContextMenu == null)
        {
            return;
        }

        editorContextMenu.ShowAt(EditorWebView, new Point(e.X, e.Y), e.CanUndo, e.CanRedo, e.HasSelection);
    }

    /// <summary>
    /// Asks the editor page to push any pending content immediately. Called on shutdown, before the
    /// buffer service's own flush. The host-side mirror (fed by the debounced/blur/hide content sync)
    /// is the authoritative flush source; this is a best-effort nudge.
    /// </summary>
    public void FlushPendingContent()
    {
        if (editorHost != null)
        {
            editorHost.RequestFlush();
        }
    }

    private void OnPanelPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        keyboardController.HandlePanelPreviewKey(e);
    }

    private void UpdateClearButtonState(int bufferIndex, bool isEmpty)
    {
        if (clearMenuItemsByIndex.TryGetValue(bufferIndex, out MenuFlyoutItem clearItem))
        {
            clearItem.IsEnabled = !isEmpty;
        }
    }

    private TabDefinition FindTabDefinition(int bufferIndex)
    {
        if (tabDefinitions == null)
        {
            return null;
        }

        foreach (var td in tabDefinitions)
        {
            if (td.Id == bufferIndex)
            {
                return td;
            }
        }

        return null;
    }
}
