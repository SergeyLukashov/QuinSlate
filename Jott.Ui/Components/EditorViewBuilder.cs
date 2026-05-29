using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using Buffer = Jott.Ui.Models.Buffer;

namespace Jott.Ui.Components;

/// <summary>
/// Builds the editor UI for a single buffer. Returns an <see cref="EditorView"/>
/// carrying the named parts so the owning panel can wire callbacks without
/// depending on the internal visual-tree layout.
/// </summary>
internal static class EditorViewBuilder
{
    private const string MonospaceFont = "Cascadia Code";
    private const double EditorFontSize = 15;
    private const int MaxBufferLength = 1_000_000;
    private const double EditorContentTopGap = 4;

    /// <summary>
    /// Builds the editor surface for <paramref name="buffer"/>.
    /// </summary>
    /// <param name="buffer">The buffer whose content seeds the editor.</param>
    /// <param name="getThemeBrush">Resolves a theme brush by key.</param>
    /// <returns>An <see cref="EditorView"/> with all named sub-elements populated.</returns>
    public static EditorView Build(Buffer buffer, Func<string, Brush> getThemeBrush)
    {
        if (buffer == null)
        {
            throw new ArgumentNullException(nameof(buffer));
        }

        if (getThemeBrush == null)
        {
            throw new ArgumentNullException(nameof(getThemeBrush));
        }

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
            MaxLength = MaxBufferLength,
            BorderThickness = new Thickness(0),
        };

        editor.Document.SetText(TextSetOptions.None, buffer.Content ?? string.Empty);

        var editorContainer = new Grid
        {
            Margin = new Thickness(0, EditorContentTopGap, 0, 0),
            RenderTransform = new TranslateTransform(),
        };
        editorContainer.Children.Add(editor);

        return new EditorView
        {
            Container = editorContainer,
            Editor = editor,
        };
    }
}
