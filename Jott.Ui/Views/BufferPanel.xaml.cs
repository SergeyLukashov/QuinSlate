using Jott.Ui.Components;
using Jott.Ui.Models;
using Jott.Ui.Services;
using Microsoft.UI.Input;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.Generic;
using Windows.System;
using Windows.UI;
using Buffer = Jott.Ui.Models.Buffer;

namespace Jott.Ui.Views;

/// <summary>
/// The 5-tab buffer UI surface. Each tab has a user-editable emoji + title header
/// and contains a monospace multiline rich-edit box bound to a single <see cref="Buffer"/>.
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

    private const double EditorClearButtonSize = 32;
    private const double EditorClearGlyphSize = 13;
    private const string EditorClearGlyph = "\uE74D";
    private const double EditorOverlayMargin = 8;
    private const double EditorConfirmBorderCornerRadius = 4;
    private const double EditorConfirmPaddingH = 6;
    private const double EditorConfirmPaddingV = 2;
    private const double EditorConfirmTextFontSize = 11;
    private const double EditorConfirmButtonSize = 32;
    private const double EditorConfirmButtonMarginLeft = 4;
    private const byte EditorConfirmBgR = 180;
    private const byte EditorConfirmBgG = 60;
    private const byte EditorConfirmBgB = 60;
    private const byte EditorConfirmBgA = 220;

    private BufferService bufferService;
    private SettingsService settingsService;
    private IReadOnlyList<TabDefinition> tabDefinitions;

    private readonly Dictionary<int, RichEditBox> editorsByBufferIndex = new Dictionary<int, RichEditBox>();
    private readonly Dictionary<int, Grid> headerContainersByIndex = new Dictionary<int, Grid>();
    private readonly Dictionary<int, FrameworkElement> normalPanelsByIndex = new Dictionary<int, FrameworkElement>();
    private readonly Dictionary<int, Border> confirmPanelsByIndex = new Dictionary<int, Border>();
    private readonly Dictionary<int, Button> clearButtonsByIndex = new Dictionary<int, Button>();
    private readonly Dictionary<int, TabViewItem> tabItemsByIndex = new Dictionary<int, TabViewItem>();
    private readonly Dictionary<int, TextBlock> tabEmojiBlocksByIndex = new Dictionary<int, TextBlock>();
    private readonly Dictionary<int, TextBlock> tabTitleBlocksByIndex = new Dictionary<int, TextBlock>();
    private readonly Dictionary<int, Button> editButtonsByIndex = new Dictionary<int, Button>();

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

        clearConfirmOverlay = new ClearConfirmOverlay(normalPanelsByIndex, confirmPanelsByIndex);
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
            tabItemsByIndex[buffer.Index] = item;
        }

        if (BufferTabView.TabItems.Count > 0)
        {
            BufferTabView.SelectedIndex = 0;
        }

        BufferTabView.TabDragCompleted += OnTabDragCompleted;
        BufferTabView.SelectionChanged += OnBufferTabSelectionChanged;
        RootGrid.PreviewKeyDown += OnPanelPreviewKeyDown;
    }

    private void OnBufferTabSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        FocusActiveEditor();
    }

    private void ClearAllDictionaries()
    {
        BufferTabView.TabItems.Clear();
        editorsByBufferIndex.Clear();
        headerContainersByIndex.Clear();
        normalPanelsByIndex.Clear();
        confirmPanelsByIndex.Clear();
        clearButtonsByIndex.Clear();
        tabItemsByIndex.Clear();
        tabEmojiBlocksByIndex.Clear();
        tabTitleBlocksByIndex.Clear();
        editButtonsByIndex.Clear();
    }

    private TabViewItem BuildTabViewItem(Buffer buffer, TabDefinition tab)
    {
        var header = TabHeaderBuilder.Build(
            buffer,
            tab,
            onEditClicked: (s, e) => OpenEditFlyout(buffer.Index),
            onDoubleTapped: (s, e) => OpenEditFlyout(buffer.Index));

        headerContainersByIndex[buffer.Index] = header.HeaderContainer;
        tabEmojiBlocksByIndex[buffer.Index] = header.EmojiBlock;
        tabTitleBlocksByIndex[buffer.Index] = header.TitleBlock;
        editButtonsByIndex[buffer.Index] = header.EditButton;

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
        normalPanelsByIndex[buffer.Index] = clearButton;
        confirmPanelsByIndex[buffer.Index] = confirmPanel;
        clearButtonsByIndex[buffer.Index] = clearButton;

        var tabItem = new TabViewItem
        {
            Header = header.HeaderContainer,
            Content = editorContainer,
            IsClosable = false,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
        };
        bool isPointerOverTab = false;
        tabItem.PointerEntered += (s, e) =>
        {
            isPointerOverTab = true;
            header.EditButton.Visibility = Visibility.Visible;
        };
        tabItem.PointerExited += (s, e) =>
        {
            isPointerOverTab = false;
            if (!tabEditFlyout.IsOpen)
            {
                header.EditButton.Visibility = Visibility.Collapsed;
            }
        };
        tabEditFlyout.FlyoutClosed += (s, e) =>
        {
            if (!isPointerOverTab)
            {
                header.EditButton.Visibility = Visibility.Collapsed;
            }
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

        var transparentBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
        clearButton.Resources["ButtonBackground"] = transparentBrush;
        clearButton.Resources["ButtonBorderBrush"] = transparentBrush;
        clearButton.Resources["ButtonBorderBrushPointerOver"] = transparentBrush;
        clearButton.Resources["ButtonBorderBrushPressed"] = transparentBrush;
        clearButton.Resources["ButtonBackgroundDisabled"] = transparentBrush;
        clearButton.Resources["ButtonBorderBrushDisabled"] = transparentBrush;

        if (Application.Current != null)
        {
            if (Application.Current.Resources.TryGetValue("SubtleFillColorSecondaryBrush", out var hoverBrush) && hoverBrush is Microsoft.UI.Xaml.Media.Brush hb)
            {
                clearButton.Resources["ButtonBackgroundPointerOver"] = hb;
            }
            if (Application.Current.Resources.TryGetValue("SubtleFillColorTertiaryBrush", out var pressedBrush) && pressedBrush is Microsoft.UI.Xaml.Media.Brush pb)
            {
                clearButton.Resources["ButtonBackgroundPressed"] = pb;
            }
            if (Application.Current.Resources.TryGetValue("TextFillColorSecondaryBrush", out var normalFore) && normalFore is Microsoft.UI.Xaml.Media.Brush nf)
            {
                clearButton.Foreground = nf;
            }
            if (Application.Current.Resources.TryGetValue("TextFillColorPrimaryBrush", out var hoverFore) && hoverFore is Microsoft.UI.Xaml.Media.Brush hf)
            {
                clearButton.Resources["ButtonForegroundPointerOver"] = hf;
                clearButton.Resources["ButtonForegroundPressed"] = hf;
            }
        }

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

        var editorContainer = new Grid();
        editorContainer.Children.Add(editor);
        editorContainer.Children.Add(overlayContainer);

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

    private Border BuildEditorConfirmPanel(int bufferIndex)
    {
        // Premium default fallbacks in case system resources are missing
        Microsoft.UI.Xaml.Media.Brush cardBg = new Microsoft.UI.Xaml.Media.SolidColorBrush(Color.FromArgb(220, 32, 32, 32)); // Dark fallback
        Microsoft.UI.Xaml.Media.Brush cardBorder = new Microsoft.UI.Xaml.Media.SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
        Microsoft.UI.Xaml.Media.Brush textFg = new Microsoft.UI.Xaml.Media.SolidColorBrush(Color.FromArgb(255, 255, 99, 71)); // Soft red fallback
        Microsoft.UI.Xaml.Media.Brush accentBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.DodgerBlue);
        Microsoft.UI.Xaml.Media.Brush hoverBg = new Microsoft.UI.Xaml.Media.SolidColorBrush(Color.FromArgb(20, 255, 255, 255));
        Microsoft.UI.Xaml.Media.Brush pressedBg = new Microsoft.UI.Xaml.Media.SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
        Microsoft.UI.Xaml.Media.Brush normalText = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White);

        if (Application.Current != null)
        {
            // Detect theme to choose proper fallback colors
            bool isDark = Application.Current.RequestedTheme == ApplicationTheme.Dark;
            if (!isDark)
            {
                cardBg = new Microsoft.UI.Xaml.Media.SolidColorBrush(Color.FromArgb(220, 243, 243, 243)); // Light fallback
                cardBorder = new Microsoft.UI.Xaml.Media.SolidColorBrush(Color.FromArgb(40, 0, 0, 0));
                textFg = new Microsoft.UI.Xaml.Media.SolidColorBrush(Color.FromArgb(255, 196, 43, 28)); // Darker red fallback
                accentBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Color.FromArgb(255, 0, 90, 158));
                hoverBg = new Microsoft.UI.Xaml.Media.SolidColorBrush(Color.FromArgb(10, 0, 0, 0));
                pressedBg = new Microsoft.UI.Xaml.Media.SolidColorBrush(Color.FromArgb(20, 0, 0, 0));
                normalText = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Black);
            }

            if (Application.Current.Resources.TryGetValue("SystemControlBackgroundChromeMediumLowBrush", out var cb) && cb is Microsoft.UI.Xaml.Media.Brush cbb)
            {
                cardBg = cbb;
            }
            if (Application.Current.Resources.TryGetValue("SystemControlTransientBorderBrush", out var sbb) && sbb is Microsoft.UI.Xaml.Media.Brush sbbb)
            {
                cardBorder = sbbb;
            }
            if (Application.Current.Resources.TryGetValue("SystemFillColorAttentionBrush", out var ab) && ab is Microsoft.UI.Xaml.Media.Brush abb)
            {
                textFg = abb;
            }
            if (Application.Current.Resources.TryGetValue("SystemAccentColorBrush", out var sacb) && sacb is Microsoft.UI.Xaml.Media.Brush sacbb)
            {
                accentBrush = sacbb;
            }
            if (Application.Current.Resources.TryGetValue("SubtleFillColorSecondaryBrush", out var sfcb) && sfcb is Microsoft.UI.Xaml.Media.Brush sfcbb)
            {
                hoverBg = sfcbb;
            }
            if (Application.Current.Resources.TryGetValue("SubtleFillColorTertiaryBrush", out var sfctb) && sfctb is Microsoft.UI.Xaml.Media.Brush sfctbb)
            {
                pressedBg = sfctbb;
            }
            if (Application.Current.Resources.TryGetValue("TextFillColorPrimaryBrush", out var tfcpb) && tfcpb is Microsoft.UI.Xaml.Media.Brush tfcpbb)
            {
                normalText = tfcpbb;
            }
        }

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
        };
        confirmButton.Click += OnConfirmCheckButtonClick;

        var transparentBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
        confirmButton.Resources["ButtonBackground"] = transparentBrush;
        confirmButton.Resources["ButtonBorderBrush"] = transparentBrush;
        confirmButton.Resources["ButtonBorderBrushPointerOver"] = transparentBrush;
        confirmButton.Resources["ButtonBorderBrushPressed"] = transparentBrush;
        confirmButton.Resources["ButtonBackgroundDisabled"] = transparentBrush;
        confirmButton.Resources["ButtonBorderBrushDisabled"] = transparentBrush;

        confirmButton.Foreground = accentBrush;
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
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
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
            if (editButtonsByIndex.TryGetValue(bufferIndex, out Button editButton))
            {
                editButton.Visibility = Visibility.Visible;
            }

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

            foreach (var kv in tabItemsByIndex)
            {
                if (kv.Value == tabItem)
                {
                    TabDefinition found = FindTabDefinition(kv.Key);
                    if (found != null)
                    {
                        reordered.Add(found);
                    }

                    break;
                }
            }
        }

        if (reordered.Count > 0 && settingsService != null)
        {
            tabDefinitions = reordered;
            settingsService.SetTabs(reordered);
        }
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
