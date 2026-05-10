using Jott.Ui.Services;
using Microsoft.UI;
using Microsoft.UI.Input;
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
/// monospace multiline text box bound to a single <see cref="Buffer"/>.
/// </summary>
public sealed partial class BufferPanel : UserControl
{
    private const string MonospaceFont = "Consolas";
    private const double EditorFontSize = 13;
    private const double TabHeaderWidth = 28;
    private const double TabHeaderHeight = 28;
    private const double TabHeaderCornerRadius = 4;
    private const double TabHeaderMargin = 2;

    private BufferService bufferService;
    private readonly Dictionary<int, TextBox> editorsByBufferIndex = new Dictionary<int, TextBox>();

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

        var headerBorder = new Border
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

        var editor = new TextBox
        {
            Text = buffer.Content,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily(MonospaceFont),
            FontSize = EditorFontSize,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Tag = buffer.Index,
        };

        editor.TextChanged += OnEditorTextChanged;
        editorsByBufferIndex[buffer.Index] = editor;

        var item = new PivotItem
        {
            Header = headerBorder,
            Content = editor,
        };

        return item;
    }

    private void OnEditorTextChanged(object sender, TextChangedEventArgs e)
    {
        if (bufferService == null)
        {
            return;
        }

        var textBox = sender as TextBox;
        if (textBox == null)
        {
            return;
        }

        if (textBox.Tag is int index)
        {
            bufferService.UpdateContent(index, textBox.Text);
        }
    }

    private void OnPanelPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
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
        if (editorsByBufferIndex.TryGetValue(bufferIndex, out TextBox editor))
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
