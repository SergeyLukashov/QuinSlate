using Jott.Ui.Services;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using Windows.System;
using Windows.UI;
using Windows.UI.Core;
using Buffer = Jott.Ui.Models.Buffer;

namespace Jott.Ui.Views;

/// <summary>
/// The 7-tab buffer UI surface. Each tab is a coloured header containing a
/// monospace multiline rich-edit box bound to a single <see cref="Buffer"/>.
/// </summary>
public sealed partial class BufferPanel : UserControl
{
    private const string MonospaceFont = "Consolas";
    private const double EditorFontSize = 14;
    private const double TabHeaderWidth = 28;
    private const double TabHeaderHeight = 28;
    private const double TabHeaderCornerRadius = 4;
    private const double TabHeaderMargin = 2;
    private const double ClearButtonWidth = 20;
    private const double ClearButtonHeight = 20;
    private const double ClearButtonMarginLeft = 4;
    private const double ConfirmBorderCornerRadius = 4;
    private const double ConfirmBorderPaddingH = 6;
    private const double ConfirmBorderPaddingV = 2;
    private const double ConfirmBorderMarginLeft = 4;
    private const double ConfirmTextFontSize = 11;
    private const double ConfirmButtonWidth = 22;
    private const double ConfirmButtonHeight = 22;
    private const double ConfirmButtonMarginLeft = 4;
    private const int ConfirmAutoCancelSeconds = 4;
    private const byte ConfirmBackgroundR = 180;
    private const byte ConfirmBackgroundG = 60;
    private const byte ConfirmBackgroundB = 60;
    private const byte ConfirmBackgroundA = 220;
    private const int VKeyOemPlus = 187;

    private BufferService bufferService;
    private readonly Dictionary<int, RichEditBox> editorsByBufferIndex = new Dictionary<int, RichEditBox>();
    private readonly Dictionary<int, Grid> headerContainersByIndex = new Dictionary<int, Grid>();
    private readonly Dictionary<int, StackPanel> normalPanelsByIndex = new Dictionary<int, StackPanel>();
    private readonly Dictionary<int, Border> confirmPanelsByIndex = new Dictionary<int, Border>();
    private readonly Dictionary<int, Button> clearButtonsByIndex = new Dictionary<int, Button>();

    private int confirmingBufferIndex = -1;
    private DispatcherTimer confirmCancelTimer;
    private bool isCalcReplacing = false;
    private bool wasEqualsTyped = false;

    /// <summary>
    /// Creates the panel. <see cref="Initialise"/> must be called before the
    /// panel is shown.
    /// </summary>
    public BufferPanel()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Wires the panel to the given <paramref name="bufferService"/> and
    /// populates the seven tabs from <paramref name="buffers"/>.
    /// </summary>
    public void Initialise(BufferService bufferService, IReadOnlyList<Buffer> buffers)
    {
        if (bufferService == null)
        {
            throw new ArgumentNullException(nameof(bufferService));
        }

        if (buffers == null)
        {
            throw new ArgumentNullException(nameof(buffers));
        }

        this.bufferService = bufferService;
        BufferPivot.Items.Clear();
        editorsByBufferIndex.Clear();
        headerContainersByIndex.Clear();
        normalPanelsByIndex.Clear();
        confirmPanelsByIndex.Clear();
        clearButtonsByIndex.Clear();

        foreach (var buffer in buffers)
        {
            var item = BuildPivotItem(buffer);
            BufferPivot.Items.Add(item);
        }

        if (BufferPivot.Items.Count > 0)
        {
            BufferPivot.SelectedIndex = 0;
        }

        RootGrid.PreviewKeyDown += OnPanelPreviewKeyDown;
    }

    private PivotItem BuildPivotItem(Buffer buffer)
    {
        var headerColor = ParseColor(buffer.ColorHex);

        var numberBadge = new Border
        {
            Width = TabHeaderWidth,
            Height = TabHeaderHeight,
            Margin = new Thickness(TabHeaderMargin),
            CornerRadius = new CornerRadius(TabHeaderCornerRadius),
            Background = new SolidColorBrush(headerColor),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = buffer.Index.ToString(),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.Black),
            },
        };

        var clearButton = new Button
        {
            Content = "✕",
            Width = ClearButtonWidth,
            Height = ClearButtonHeight,
            Padding = new Thickness(0),
            Margin = new Thickness(ClearButtonMarginLeft, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Tag = buffer.Index,
            IsEnabled = !string.IsNullOrEmpty(buffer.Content),
        };
        clearButton.Click += OnClearButtonClick;
        clearButtonsByIndex[buffer.Index] = clearButton;

        var normalPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        normalPanel.Children.Add(numberBadge);
        normalPanel.Children.Add(clearButton);
        normalPanelsByIndex[buffer.Index] = normalPanel;

        var confirmCheckButton = new Button
        {
            Content = "✓",
            Width = ConfirmButtonWidth,
            Height = ConfirmButtonHeight,
            Padding = new Thickness(0),
            Margin = new Thickness(ConfirmButtonMarginLeft, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Tag = buffer.Index,
        };
        confirmCheckButton.Click += OnConfirmCheckButtonClick;

        var confirmContent = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        confirmContent.Children.Add(new TextBlock
        {
            Text = "Clear?",
            FontSize = ConfirmTextFontSize,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Colors.White),
        });
        confirmContent.Children.Add(confirmCheckButton);

        var confirmPanel = new Border
        {
            CornerRadius = new CornerRadius(ConfirmBorderCornerRadius),
            Padding = new Thickness(ConfirmBorderPaddingH, ConfirmBorderPaddingV, ConfirmBorderPaddingH, ConfirmBorderPaddingV),
            Margin = new Thickness(ConfirmBorderMarginLeft, 0, 0, 0),
            Background = new SolidColorBrush(Color.FromArgb(ConfirmBackgroundA, ConfirmBackgroundR, ConfirmBackgroundG, ConfirmBackgroundB)),
            VerticalAlignment = VerticalAlignment.Center,
            Child = confirmContent,
            Visibility = Visibility.Collapsed,
        };
        confirmPanelsByIndex[buffer.Index] = confirmPanel;

        var headerContainer = new Grid
        {
            VerticalAlignment = VerticalAlignment.Center,
        };
        headerContainer.Children.Add(normalPanel);
        headerContainer.Children.Add(confirmPanel);
        headerContainersByIndex[buffer.Index] = headerContainer;

        var editor = new RichEditBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily(MonospaceFont),
            FontSize = EditorFontSize,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            IsSpellCheckEnabled = false,
            Tag = buffer.Index,
            SelectionFlyout = null,
        };

        // Set initial content before subscribing to TextChanged so the load
        // does not trigger a redundant UpdateContent call.
        editor.Document.SetText(TextSetOptions.None, buffer.Content ?? string.Empty);

        editor.TextChanged += OnEditorTextChanged;
        editor.KeyDown += OnEditorKeyDown;
        editor.PointerPressed += OnEditorPointerPressed;
        editor.Paste += OnEditorPaste;
        editorsByBufferIndex[buffer.Index] = editor;

        var item = new PivotItem
        {
            Header = headerContainer,
            Content = editor,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
        };

        return item;
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
            EnterConfirmState(index);
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
            ConfirmClear(index);
        }
    }

    private void OnEditorPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (confirmingBufferIndex != -1)
        {
            ExitConfirmState();
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
            editor.Document.Selection.TypeText(text);
        }
    }

    private void EnterConfirmState(int bufferIndex)
    {
        if (confirmingBufferIndex != -1 && confirmingBufferIndex != bufferIndex)
        {
            ExitConfirmState();
        }

        confirmingBufferIndex = bufferIndex;

        if (normalPanelsByIndex.TryGetValue(bufferIndex, out StackPanel normalPanel))
        {
            normalPanel.Visibility = Visibility.Collapsed;
        }

        if (confirmPanelsByIndex.TryGetValue(bufferIndex, out Border confirmPanel))
        {
            confirmPanel.Visibility = Visibility.Visible;
        }

        if (confirmCancelTimer != null)
        {
            confirmCancelTimer.Stop();
            confirmCancelTimer = null;
        }

        confirmCancelTimer = new DispatcherTimer();
        confirmCancelTimer.Interval = TimeSpan.FromSeconds(ConfirmAutoCancelSeconds);
        confirmCancelTimer.Tick += OnConfirmTimerTick;
        confirmCancelTimer.Start();
    }

    private void ExitConfirmState()
    {
        if (confirmCancelTimer != null)
        {
            confirmCancelTimer.Stop();
            confirmCancelTimer.Tick -= OnConfirmTimerTick;
            confirmCancelTimer = null;
        }

        int index = confirmingBufferIndex;
        confirmingBufferIndex = -1;

        if (index == -1)
        {
            return;
        }

        if (normalPanelsByIndex.TryGetValue(index, out StackPanel normalPanel))
        {
            normalPanel.Visibility = Visibility.Visible;
        }

        if (confirmPanelsByIndex.TryGetValue(index, out Border confirmPanel))
        {
            confirmPanel.Visibility = Visibility.Collapsed;
        }
    }

    private void ConfirmClear(int bufferIndex)
    {
        ExitConfirmState();

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

    private void UpdateClearButtonState(int bufferIndex, bool isEmpty)
    {
        if (clearButtonsByIndex.TryGetValue(bufferIndex, out Button clearButton))
        {
            clearButton.IsEnabled = !isEmpty;
        }
    }

    private void OnConfirmTimerTick(object sender, object e)
    {
        ExitConfirmState();
    }

    private void OnEditorKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != (VirtualKey)VKeyOemPlus)
        {
            return;
        }

        var shiftState = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift);
        if ((shiftState & CoreVirtualKeyStates.Down) == 0)
        {
            wasEqualsTyped = true;
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
            if (!isCalcReplacing && wasEqualsTyped)
            {
                wasEqualsTyped = false;
                TryCalcReplace(editor);
            }
            else
            {
                wasEqualsTyped = false;
            }

            editor.Document.GetText(TextGetOptions.UseCrlf, out string text);
            text = text.TrimEnd('\r', '\n');
            bufferService.UpdateContent(index, text);
            UpdateClearButtonState(index, isEmpty: string.IsNullOrEmpty(text));
        }
    }

    private void TryCalcReplace(RichEditBox editor)
    {
        var sel = editor.Document.Selection;

        var lineRange = sel.GetClone();
        lineRange.Expand(TextRangeUnit.Line);
        int lineStart = lineRange.StartPosition;

        lineRange.GetText(TextGetOptions.None, out string fullLineText);

        bool lineHasBreak = fullLineText.Length > 0 && fullLineText[fullLineText.Length - 1] == '\r';
        string lineContent = lineHasBreak
            ? fullLineText.Substring(0, fullLineText.Length - 1)
            : fullLineText;

        int contentEnd = lineStart + lineContent.Length;

        if (!CalcService.TryEvaluate(lineContent, out string result))
        {
            return;
        }

        string newLineContent = lineContent + " " + result;

        isCalcReplacing = true;
        try
        {
            var contentRange = editor.Document.GetRange(lineStart, contentEnd);
            contentRange.SetText(TextSetOptions.None, newLineContent);
            int newCaretPos = lineStart + newLineContent.Length;
            editor.Document.Selection.SetRange(newCaretPos, newCaretPos);
        }
        finally
        {
            isCalcReplacing = false;
        }
    }

    private void OnPanelPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape && confirmingBufferIndex != -1)
        {
            ExitConfirmState();
            e.Handled = true;
            return;
        }

        if (e.Key == VirtualKey.Tab)
        {
            var shiftState = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift);
            bool back = (shiftState & CoreVirtualKeyStates.Down) != 0;
            CycleBuffer(back ? -1 : 1);
            e.Handled = true;
            return;
        }

        var ctrlState = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control);
        bool ctrl = (ctrlState & CoreVirtualKeyStates.Down) != 0;
        if (!ctrl)
        {
            return;
        }

        if (e.Key == VirtualKey.B || e.Key == VirtualKey.I || e.Key == VirtualKey.U)
        {
            e.Handled = true;
            return;
        }

        int bufferIndex = -1;
        if (e.Key >= VirtualKey.Number1 && e.Key <= VirtualKey.Number7)
        {
            bufferIndex = (int)e.Key - (int)VirtualKey.Number1;
        }
        else if (e.Key >= VirtualKey.NumberPad1 && e.Key <= VirtualKey.NumberPad7)
        {
            bufferIndex = (int)e.Key - (int)VirtualKey.NumberPad1;
        }

        if (bufferIndex >= 0)
        {
            SelectBuffer(bufferIndex);
            e.Handled = true;
        }
    }

    private void CycleBuffer(int direction)
    {
        int count = BufferPivot.Items.Count;
        if (count == 0)
        {
            return;
        }

        int next = ((BufferPivot.SelectedIndex + direction) % count + count) % count;
        SelectBuffer(next);
    }

    private void SelectBuffer(int zeroBasedIndex)
    {
        if (zeroBasedIndex < 0 || zeroBasedIndex >= BufferPivot.Items.Count)
        {
            return;
        }

        BufferPivot.SelectedIndex = zeroBasedIndex;
        int bufferIndex = zeroBasedIndex + 1;
        if (editorsByBufferIndex.TryGetValue(bufferIndex, out RichEditBox editor))
        {
            editor.Focus(FocusState.Programmatic);
        }
    }

    private static Color ParseColor(string hex)
    {
        if (string.IsNullOrEmpty(hex) || hex.Length != 7 || hex[0] != '#')
        {
            return Colors.Gray;
        }

        try
        {
            var r = Convert.ToByte(hex.Substring(1, 2), 16);
            var g = Convert.ToByte(hex.Substring(3, 2), 16);
            var b = Convert.ToByte(hex.Substring(5, 2), 16);
            return Color.FromArgb(0xFF, r, g, b);
        }
        catch (FormatException)
        {
            return Colors.Gray;
        }
    }
}
