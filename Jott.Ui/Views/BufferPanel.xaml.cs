using Jott.Ui.Components;
using Jott.Ui.Helpers;
using Jott.Ui.Layout;
using Jott.Ui.Models;
using Jott.Ui.Services;
using Microsoft.UI.Input;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using System;
using System.Collections.Generic;
using Windows.System;
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
    private const string MonospaceFont = "Cascadia Code";
    private const double EditorFontSize = 15;
    private const string PinGlyph = "\uE718";
    private const string PinnedGlyph = "\uE77A";
    private const string PinTooltip = "Pin";
    private const string UnpinTooltip = "Unpin";
    private const int MaxBufferLength = 1_000_000;

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

    private const double EditorClearButtonSize = 32;
    private const double EditorClearGlyphSize = 13;
    private const string EditorClearGlyph = "\uE74D";
    private const double EditorOverlayMargin = 8;
    private const double EditorContentTopGap = 4;
    private const double EditorConfirmTextFontSize = 11;
    private const double EditorConfirmButtonSize = 32;
    private const double EditorConfirmButtonMarginLeft = 4;

    private const string ConfirmCardBackgroundBrushKey = "SystemControlBackgroundChromeMediumLowBrush";
    private const string ConfirmCardBorderBrushKey = "SystemControlTransientBorderBrush";
    private const string ConfirmTextBrushKey = "SystemFillColorAttentionBrush";
    private const string ConfirmAccentBrushKey = "SystemAccentColorBrush";
    private const string ConfirmHoverBrushKey = "SubtleFillColorSecondaryBrush";
    private const string ConfirmPressedBrushKey = "SubtleFillColorTertiaryBrush";
    private const string ConfirmTextPrimaryBrushKey = "TextFillColorPrimaryBrush";

    private BufferService bufferService;
    private SettingsService settingsService;
    private IReadOnlyList<TabDefinition> tabDefinitions;

    private readonly Dictionary<int, RichEditBox> editorsByBufferIndex = new Dictionary<int, RichEditBox>();
    private readonly Dictionary<int, Grid> headerContainersByIndex = new Dictionary<int, Grid>();
    private readonly Dictionary<int, Grid> editorContainersByIndex = new Dictionary<int, Grid>();
    private readonly Dictionary<int, Border> confirmPanelsByIndex = new Dictionary<int, Border>();
    private readonly Dictionary<int, Button> clearButtonsByIndex = new Dictionary<int, Button>();
    private readonly Dictionary<int, TextBlock> tabEmojiBlocksByIndex = new Dictionary<int, TextBlock>();
    private readonly Dictionary<int, TextBlock> tabTitleBlocksByIndex = new Dictionary<int, TextBlock>();

    private RichEditBox pendingFocusEditor;
    private RoutedEventHandler pendingFocusHandler;

    private readonly EmojiPicker emojiPicker = new EmojiPicker();
    private TabEditFlyout tabEditFlyout;
    private ClearConfirmOverlay clearConfirmOverlay;
    private readonly CalcResultAnimator calcResultAnimator = new CalcResultAnimator();

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

        FocusEditorWhenReady(editor);
    }

    private void FocusEditorWhenReady(RichEditBox editor)
    {
        ClearPendingFocusHandler();

        if (editor.IsLoaded)
        {
            DispatcherQueue.TryEnqueue(() => editor.Focus(FocusState.Programmatic));
            return;
        }

        pendingFocusEditor = editor;
        pendingFocusHandler = OnPendingFocusEditorLoaded;
        editor.Loaded += pendingFocusHandler;
    }

    private void OnPendingFocusEditorLoaded(object sender, RoutedEventArgs e)
    {
        var editor = sender as RichEditBox;
        ClearPendingFocusHandler();
        if (editor == null)
        {
            return;
        }

        DispatcherQueue.TryEnqueue(() => editor.Focus(FocusState.Programmatic));
    }

    private void ClearPendingFocusHandler()
    {
        if (pendingFocusEditor != null && pendingFocusHandler != null)
        {
            pendingFocusEditor.Loaded -= pendingFocusHandler;
        }

        pendingFocusEditor = null;
        pendingFocusHandler = null;
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

        clearConfirmOverlay = new ClearConfirmOverlay(confirmPanelsByIndex);
        clearConfirmOverlay.Cleared += OnClearConfirmed;

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
    /// share is floored at <see cref="TabMinWidth"/> so that once the tabs can no longer fit at
    /// their minimum the SDK overflows and surfaces its scroll buttons instead of clipping.
    /// The matching title <c>MaxWidth</c> is derived from the same stable share.
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
        confirmPanelsByIndex.Clear();
        clearButtonsByIndex.Clear();
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

        var editor = new RichEditBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily(MonospaceFont),
            FontSize = EditorFontSize,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            IsSpellCheckEnabled = false,
            Tag = buffer.Index,
            SelectionFlyout = null,
            MaxLength = MaxBufferLength,
            BorderThickness = new Thickness(0),
        };

        editor.Document.SetText(TextSetOptions.None, buffer.Content ?? string.Empty);

        editor.TextChanged += OnEditorTextChanged;
        editor.KeyDown += OnEditorKeyDown;
        editor.PointerPressed += OnEditorPointerPressed;
        editor.Paste += OnEditorPaste;
        editorsByBufferIndex[buffer.Index] = editor;

        var (editorContainer, clearButton, confirmPanel) = BuildEditorContainer(buffer, editor);
        confirmPanelsByIndex[buffer.Index] = confirmPanel;
        clearButtonsByIndex[buffer.Index] = clearButton;

        var menuFlyout = new MenuFlyout();
        var renameItem = new MenuFlyoutItem
        {
            Text = "Rename tab",
            Icon = new FontIcon
            {
                Glyph = "\uE8AC",
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons"),
            }
        };
        renameItem.Click += (s, e) => OpenEditFlyout(buffer.Index);
        menuFlyout.Items.Add(renameItem);

        var tabItem = new TabViewItem
        {
            Header = header.HeaderContainer,
            Content = editorContainer,
            IsClosable = false,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            ContextFlyout = menuFlyout,
            Tag = buffer.Index,
        };
        tabItem.GettingFocus += OnTabItemGettingFocus;
        return tabItem;
    }

    private (Grid container, Button clearButton, Border confirmPanel) BuildEditorContainer(Buffer buffer, RichEditBox editor)
    {
        var clearButton = new Button
        {
            Width = EditorClearButtonSize,
            Height = EditorClearButtonSize,
            Padding = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
            IsEnabled = !string.IsNullOrEmpty(buffer.Content),
            Tag = buffer.Index,
            CornerRadius = new CornerRadius(4),
            Content = new FontIcon { Glyph = EditorClearGlyph, FontSize = EditorClearGlyphSize },
        };

        clearButton.Click += OnClearButtonClick;
        ToolTipService.SetToolTip(clearButton, "Clear this tab");

        var confirmPanel = BuildEditorConfirmPanel(buffer.Index);

        var overlayContainer = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, EditorOverlayMargin, EditorOverlayMargin),
            IsHitTestVisible = true,
        };
        overlayContainer.Children.Add(clearButton);
        overlayContainer.Children.Add(confirmPanel);

        var editorContainer = new Grid
        {
            Margin = new Thickness(0, EditorContentTopGap, 0, 0),
            RenderTransform = new TranslateTransform(),
        };
        editorContainer.Children.Add(editor);
        editorContainer.Children.Add(overlayContainer);
        editorContainersByIndex[buffer.Index] = editorContainer;

        editorContainer.PointerEntered += (s, e) =>
        {
            if (!clearConfirmOverlay.IsConfirming)
            {
                clearButton.Visibility = Visibility.Visible;
            }
        };
        editorContainer.PointerExited += (s, e) =>
        {
            if (!clearConfirmOverlay.IsConfirming)
            {
                clearButton.Visibility = Visibility.Collapsed;
            }
        };

        return (editorContainer, clearButton, confirmPanel);
    }

    private Brush GetThemeBrush(string key)
    {
        if (this.Resources.TryGetValue(key, out var localValue) && localValue is Brush localBrush)
        {
            return localBrush;
        }

        return (Brush)Application.Current.Resources[key];
    }

    private Border BuildEditorConfirmPanel(int bufferIndex)
    {
        Brush cardBg = GetThemeBrush(ConfirmCardBackgroundBrushKey);
        Brush cardBorder = GetThemeBrush(ConfirmCardBorderBrushKey);
        Brush textFg = GetThemeBrush(ConfirmTextBrushKey);
        Brush accentBrush = GetThemeBrush(ConfirmAccentBrushKey);
        Brush hoverBg = GetThemeBrush(ConfirmHoverBrushKey);
        Brush pressedBg = GetThemeBrush(ConfirmPressedBrushKey);
        Brush normalText = GetThemeBrush(ConfirmTextPrimaryBrushKey);

        var confirmButton = new Button
        {
            Content = "✓",
            Width = EditorConfirmButtonSize,
            Height = EditorConfirmButtonSize,
            Padding = new Thickness(0),
            Margin = new Thickness(EditorConfirmButtonMarginLeft, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            CornerRadius = new CornerRadius(4),
            Tag = bufferIndex,
            Foreground = accentBrush,
        };
        confirmButton.Click += OnConfirmCheckButtonClick;

        var transparentBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        confirmButton.Resources["ButtonBackground"] = transparentBrush;
        confirmButton.Resources["ButtonBorderBrush"] = transparentBrush;
        confirmButton.Resources["ButtonBorderBrushPointerOver"] = transparentBrush;
        confirmButton.Resources["ButtonBorderBrushPressed"] = transparentBrush;
        confirmButton.Resources["ButtonBackgroundDisabled"] = transparentBrush;
        confirmButton.Resources["ButtonBorderBrushDisabled"] = transparentBrush;
        confirmButton.Resources["ButtonBackgroundPointerOver"] = hoverBg;
        confirmButton.Resources["ButtonBackgroundPressed"] = pressedBg;
        confirmButton.Resources["ButtonForegroundPointerOver"] = normalText;
        confirmButton.Resources["ButtonForegroundPressed"] = normalText;

        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        content.Children.Add(new TextBlock
        {
            Text = "Clear?",
            FontSize = EditorConfirmTextFontSize,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = textFg,
            FontWeight = FontWeights.SemiBold,
        });
        content.Children.Add(confirmButton);

        return new Border
        {
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            BorderBrush = cardBorder,
            Padding = new Thickness(8, 2, 4, 2),
            Background = cardBg,
            VerticalAlignment = VerticalAlignment.Center,
            Child = content,
            Visibility = Visibility.Collapsed,
        };
    }

    private void OnTabItemGettingFocus(UIElement sender, GettingFocusEventArgs e)
    {
        if (ReferenceEquals(e.NewFocusedElement, sender))
        {
            e.TryCancel();
        }
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
        }
    }

    private void OnTabEditSaved(object sender, TabEditSavedEventArgs e)
    {
        UpdateTabLabel(e.BufferIndex, e.Emoji, e.Title);
        TabLabelChanged?.Invoke(this, EventArgs.Empty);
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
    /// <see cref="InterTabGapLeft"/> dead strip after the left edge / scroll-back button,
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

    private void OnClearButtonClick(object sender, RoutedEventArgs e)
    {
        var button = sender as Button;
        if (button == null)
        {
            return;
        }

        if (button.Tag is int index)
        {
            clearConfirmOverlay.Enter(index);
            if (clearButtonsByIndex.TryGetValue(index, out Button clearBtn))
            {
                clearBtn.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void OnConfirmCheckButtonClick(object sender, RoutedEventArgs e)
    {
        var button = sender as Button;
        if (button == null)
        {
            return;
        }

        if (button.Tag is int index)
        {
            clearConfirmOverlay.Confirm(index);
        }
    }

    private void OnClearConfirmed(object sender, int bufferIndex)
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
    }

    private void OnEditorPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (clearConfirmOverlay.IsConfirming)
        {
            clearConfirmOverlay.Exit();
        }
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

    private void OnEditorKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Tab)
        {
            bool back = IsKeyDown(InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift));
            CycleBuffer(back ? -1 : 1);
            e.Handled = true;
            return;
        }

        calcResultAnimator.TrackKeyDown(
            (int)e.Key,
            IsKeyDown(InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)));
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
        if (e.Key == VirtualKey.Escape && clearConfirmOverlay.IsConfirming)
        {
            clearConfirmOverlay.Exit();
            e.Handled = true;
            return;
        }

        if (e.Key == VirtualKey.F2)
        {
            int selectedIndex = BufferTabView.SelectedIndex;
            if (selectedIndex >= 0)
            {
                OpenEditFlyout(selectedIndex + 1);
                e.Handled = true;
                return;
            }
        }

        bool ctrl = IsKeyDown(InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control));
        if (!ctrl)
        {
            return;
        }

        if (e.Key == VirtualKey.Tab)
        {
            bool back = IsKeyDown(InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift));
            CycleBuffer(back ? -1 : 1);
            e.Handled = true;
            return;
        }

        if (e.Key == VirtualKey.B || e.Key == VirtualKey.I || e.Key == VirtualKey.U)
        {
            e.Handled = true;
            return;
        }

        int bufferIndexFromKey = -1;
        if (e.Key >= VirtualKey.Number1 && e.Key <= VirtualKey.Number5)
        {
            bufferIndexFromKey = (int)e.Key - (int)VirtualKey.Number1;
        }
        else if (e.Key >= VirtualKey.NumberPad1 && e.Key <= VirtualKey.NumberPad5)
        {
            bufferIndexFromKey = (int)e.Key - (int)VirtualKey.NumberPad1;
        }

        if (bufferIndexFromKey >= 0)
        {
            SelectBuffer(bufferIndexFromKey);
            e.Handled = true;
        }
    }

    private void UpdateClearButtonState(int bufferIndex, bool isEmpty)
    {
        if (clearButtonsByIndex.TryGetValue(bufferIndex, out Button clearButton))
        {
            clearButton.IsEnabled = !isEmpty;
        }
    }

    private void CycleBuffer(int direction)
    {
        int count = BufferTabView.TabItems.Count;
        if (count == 0)
        {
            return;
        }

        int next = ((BufferTabView.SelectedIndex + direction) % count + count) % count;
        SelectBuffer(next);
    }

    private void SelectBuffer(int zeroBasedIndex)
    {
        if (zeroBasedIndex < 0 || zeroBasedIndex >= BufferTabView.TabItems.Count)
        {
            return;
        }

        BufferTabView.SelectedIndex = zeroBasedIndex;
        int bufferIndex = zeroBasedIndex + 1;
        if (editorsByBufferIndex.TryGetValue(bufferIndex, out RichEditBox editor))
        {
            DispatcherQueue.TryEnqueue(() => editor.Focus(FocusState.Programmatic));
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

    private static bool IsKeyDown(Windows.UI.Core.CoreVirtualKeyStates state)
    {
        return (state & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
    }
}
