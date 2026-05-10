using Jott.Ui.Services;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using Windows.UI;
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
