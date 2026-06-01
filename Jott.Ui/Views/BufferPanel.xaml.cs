using Jott.Ui.Components;
using Jott.Ui.Helpers;
using Jott.Ui.Interop;
using Jott.Ui.Layout;
using Jott.Ui.Models;
using Jott.Ui.Services;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using System;
using System.Collections.Generic;
using Buffer = Jott.Ui.Models.Buffer;

namespace Jott.Ui.Views;

/// <summary>
/// The 5-tab buffer UI surface. Each tab has a user-editable emoji + title header
/// and contains a monospace multiline rich-edit box bound to a single <see cref="Buffer"/>.
///
/// Title-bar layout: the pin and close buttons are a top-level overlay anchored to the
/// window's top-right corner, so they can never be clipped by the TabView's internal
/// columns. The TabView's TabStripFooter holds a fixed-width transparent spacer reserving
/// exactly the overlay cluster's width so the tabs stop where the buttons begin. Each tab's
/// header is given an explicit width (see <see cref="UpdateEqualTabMaxWidth"/>) so the five
/// equal-mode tabs fill the row at every practical width. The scroll (overflow) buttons
/// surface only on genuine overflow, once the row is too narrow to give every tab its 100px
/// minimum.
/// </summary>
public sealed partial class BufferPanel : UserControl
{
    private const string PinGlyph = "";
    private const string PinnedGlyph = "";
    private const string PinTooltip = "Pin";
    private const string UnpinTooltip = "Unpin";
    private const int MaxBufferLength = 1_000_000;
    private const string RenameTabMenuText = "Rename tab";
    private const string RenameTabIconGlyph = "";
    private const string FluentIconFontFamily = "Segoe Fluent Icons";
    private const string ClearTabMenuText = "Clear tab";
    private const string ClearTabIconGlyph = "";

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

    private TabViewItem firstTabWithZeroedGap;

    /// <summary>
    /// Duration, in milliseconds, of the tab-content entrance animation (fade + slide-up)
    /// that plays when the active tab changes.
    /// </summary>
    private const int ContentEntranceDurationMs = 180;

    /// <summary>
    /// Vertical offset, in pixels, the newly-shown tab content slides up from during its
    /// entrance animation.
    /// </summary>
    private const double ContentEntranceSlideOffset = 12;

    private BufferService bufferService;
    private SettingsService settingsService;
    private IReadOnlyList<TabDefinition> tabDefinitions;

    private readonly Dictionary<int, RichEditBox> editorsByBufferIndex = new Dictionary<int, RichEditBox>();
    private readonly Dictionary<int, Grid> headerContainersByIndex = new Dictionary<int, Grid>();
    private readonly Dictionary<int, Grid> editorContainersByIndex = new Dictionary<int, Grid>();
    private readonly Dictionary<int, MenuFlyoutItem> clearMenuItemsByIndex = new Dictionary<int, MenuFlyoutItem>();
    private readonly Dictionary<int, TextBlock> tabEmojiBlocksByIndex = new Dictionary<int, TextBlock>();
    private readonly Dictionary<int, TextBlock> tabTitleBlocksByIndex = new Dictionary<int, TextBlock>();

    private readonly EmojiPicker emojiPicker = new EmojiPicker();
    private readonly EditorFocusController focusController = new EditorFocusController();
    private readonly CalcResultAnimator calcResultAnimator = new CalcResultAnimator();
    private TabEditFlyout tabEditFlyout;
    private BufferKeyboardController keyboardController;
    private bool preventMenuClosing;
    private DateTime clearTransitionTime;
    private const int ClearConfirmCooldownMs = 500;

    /// <summary>
    /// Raised when the user clicks the pin button. The caller should toggle
    /// the pinned state and call <see cref="SetPinned"/> to update the icon.
    /// </summary>
    public event EventHandler PinToggleRequested;

    /// <summary>
    /// Raised when the user clicks the close button. The caller should hide the
    /// window (Jott hides to the tray rather than terminating).
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
        ToolTipService.SetToolTip(TitleBarIconImage, AppConstants.AppName);
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
    /// Places keyboard focus into the rich-edit box of the currently selected
    /// tab so the user can type immediately after the panel is shown. When the
    /// editor's visual tree is not yet realized (for example on app startup),
    /// focus is deferred until the editor raises its <c>Loaded</c> event.
    /// </summary>
    public void FocusActiveEditor()
    {
        int bufferIndex = BufferTabView.SelectedIndex + 1;
        if (editorsByBufferIndex.TryGetValue(bufferIndex, out RichEditBox editor) == false)
        {
            return;
        }

        focusController.FocusWhenReady(editor);
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

        tabEditFlyout = new TabEditFlyout(emojiPicker, settingsService);
        tabEditFlyout.Saved += OnTabEditSaved;
        tabEditFlyout.FlyoutClosed += OnTabEditFlyoutClosed;

        keyboardController = new BufferKeyboardController(
            BufferTabView,
            editorsByBufferIndex,
            calcResultAnimator);
        keyboardController.EditFlyoutRequested += OnEditFlyoutRequested;

        ClearAllDictionaries();

        for (int i = 0; i < buffers.Count; i++)
        {
            var buffer = buffers[i];
            TabDefinition tab = FindTabDefinition(buffer.Index) ?? new TabDefinition
            {
                Id = buffer.Index,
                Emoji = "📋",
                Title = buffer.Index.ToString(),
            };

            var item = BuildTabViewItem(buffer, tab);
            BufferTabView.TabItems.Add(item);
        }

        if (BufferTabView.TabItems.Count > 0)
        {
            BufferTabView.SelectedIndex = 0;
        }

        BufferTabView.TabDragCompleted += OnTabDragCompleted;
        BufferTabView.SelectionChanged += OnBufferTabSelectionChanged;
        BufferTabView.SizeChanged += OnBufferTabViewSizeChanged;
        BufferTabView.Loaded += OnBufferTabViewLoaded;
        RootGrid.PreviewKeyDown += OnPanelPreviewKeyDown;

        UpdateEqualTabMaxWidth();
        RecomputeFirstTabLeadingGap();
    }

    private void OnBufferTabViewSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateEqualTabMaxWidth();
        PinRightContentColumnReservation();
    }

    private void OnBufferTabViewLoaded(object sender, RoutedEventArgs e)
    {
        PinRightContentColumnReservation();
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
    /// Sets each <see cref="TabViewItem.Width"/> to the equal share of the live tab-strip
    /// width so the tabs fill the row exactly and shrink as the window narrows. In
    /// <c>TabWidthMode="Equal"</c> the SDK sizes each tab to its own content (a <c>MaxWidth</c>
    /// is only a ceiling and never forces a tab to grow), so without an explicit width the
    /// tabs sit at their title width and leave unused space on the right. Assigning an explicit
    /// per-tab width forces all of them to the same value and consumes the full strip. The
    /// share is floored at <see cref="TabStripCalculator.TabMinWidth"/> so that once the tabs
    /// can no longer fit at their minimum the SDK overflows and surfaces its scroll buttons
    /// instead of clipping. The matching title <c>MaxWidth</c> is derived from the same stable
    /// share.
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

    private void OnBufferTabSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        AnimateActiveContentEntrance();
        FocusActiveEditor();
    }

    /// <summary>
    /// Plays a fast fade + slide-up entrance on the newly-selected tab's content so the
    /// editor area animates in rather than appearing instantly. Operates purely on a
    /// <see cref="TranslateTransform"/> and <see cref="UIElement.Opacity"/>, leaving layout
    /// and focus untouched.
    /// </summary>
    private void AnimateActiveContentEntrance()
    {
        int bufferIndex = BufferTabView.SelectedIndex + 1;
        if (editorContainersByIndex.TryGetValue(bufferIndex, out Grid container) == false)
        {
            return;
        }

        var translate = container.RenderTransform as TranslateTransform;
        if (translate == null)
        {
            return;
        }

        var duration = new Duration(TimeSpan.FromMilliseconds(ContentEntranceDurationMs));
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        var fade = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = duration,
            EasingFunction = ease,
        };
        Storyboard.SetTarget(fade, container);
        Storyboard.SetTargetProperty(fade, "Opacity");

        var slide = new DoubleAnimation
        {
            From = ContentEntranceSlideOffset,
            To = 0,
            Duration = duration,
            EasingFunction = ease,
        };
        Storyboard.SetTarget(slide, translate);
        Storyboard.SetTargetProperty(slide, "Y");

        var storyboard = new Storyboard();
        storyboard.Children.Add(fade);
        storyboard.Children.Add(slide);
        storyboard.Begin();
    }

    private void ClearAllDictionaries()
    {
        BufferTabView.TabItems.Clear();
        editorsByBufferIndex.Clear();
        headerContainersByIndex.Clear();
        editorContainersByIndex.Clear();
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

        EditorView editorView = EditorViewBuilder.Build(buffer, GetThemeBrush);

        RichEditBox editor = editorView.Editor;
        editor.TextChanged += OnEditorTextChanged;
        editor.KeyDown += keyboardController.HandleEditorKey;
        editor.Paste += OnEditorPaste;
        editorsByBufferIndex[buffer.Index] = editor;

        Grid editorContainer = editorView.Container;
        editorContainersByIndex[buffer.Index] = editorContainer;

        var tabItem = new TabViewItem
        {
            Header = header.HeaderContainer,
            Content = editorContainer,
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

    private Brush GetThemeBrush(string key)
    {
        if (this.Resources.TryGetValue(key, out var localValue) && localValue is Brush localBrush)
        {
            return localBrush;
        }

        return (Brush)Application.Current.Resources[key];
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

    private void OnTabDragCompleted(TabView sender, TabViewTabDragCompletedEventArgs args)
    {
        var reordered = new List<TabDefinition>();
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

        if (reordered.Count > 0 && settingsService != null)
        {
            tabDefinitions = reordered;
            settingsService.SetTabs(reordered);
        }

        RecomputeFirstTabLeadingGap();
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
        if (editorsByBufferIndex.TryGetValue(bufferIndex, out RichEditBox editor))
        {
            editor.Document.SetText(TextSetOptions.None, string.Empty);
        }

        if (bufferService != null)
        {
            bufferService.UpdateContent(bufferIndex, string.Empty);
        }

        UpdateClearButtonState(bufferIndex, isEmpty: true);
        ResetClearMenuItem(bufferIndex);
    }

    private async void OnEditorPaste(object sender, TextControlPasteEventArgs e)
    {
        e.Handled = true;
        var editor = sender as RichEditBox;
        if (editor == null)
        {
            return;
        }

        var dataView = Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
        if (dataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text))
        {
            string text = await dataView.GetTextAsync();
            editor.Document.GetText(TextGetOptions.None, out string currentText);

            int selectionLength = editor.Document.Selection.Text.Length;
            int maxAllowedPaste = MaxBufferLength - (currentText.Length - selectionLength);

            if (maxAllowedPaste > 0)
            {
                if (text.Length > maxAllowedPaste)
                {
                    text = text.Substring(0, maxAllowedPaste);
                }

                editor.Document.Selection.TypeText(text);
            }
        }
    }

    private void OnEditorTextChanged(object sender, RoutedEventArgs e)
    {
        if (bufferService == null)
        {
            return;
        }

        var editor = sender as RichEditBox;
        if (editor == null)
        {
            return;
        }

        if (editor.Tag is int index)
        {
            calcResultAnimator.HandleTextChanged(editor);

            editor.Document.GetText(TextGetOptions.UseCrlf, out string text);
            text = text.TrimEnd('\r', '\n');
            bufferService.UpdateContent(index, text);
            UpdateClearButtonState(index, isEmpty: string.IsNullOrEmpty(text));
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
