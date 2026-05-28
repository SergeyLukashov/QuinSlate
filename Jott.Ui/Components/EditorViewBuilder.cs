using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using Buffer = Jott.Ui.Models.Buffer;

namespace Jott.Ui.Components;

/// <summary>
/// Builds the editor UI for a single buffer: the <see cref="RichEditBox"/>, the
/// hover-revealed trash button, and the "Clear?" confirm card. Returns an
/// <see cref="EditorView"/> carrying the named parts so the owning panel can wire
/// callbacks without depending on the internal visual-tree layout.
/// </summary>
/// <remarks>
/// The builder does not subscribe to any control events. Hover-driven visibility,
/// keyboard handling, paste, and click handlers are wired by the owning panel
/// because they need cross-component state (e.g. the clear-confirm flow).
/// </remarks>
internal static class EditorViewBuilder
{
    private const string MonospaceFont = "Cascadia Code";
    private const double EditorFontSize = 15;
    private const int MaxBufferLength = 1_000_000;

    private const double EditorClearButtonSize = 32;
    private const double EditorClearGlyphSize = 13;
    private const string EditorClearGlyph = "";
    private const double EditorOverlayMargin = 8;
    private const double EditorContentTopGap = 4;
    private const double EditorConfirmTextFontSize = 11;
    private const double EditorConfirmButtonSize = 32;
    private const double EditorConfirmButtonMarginLeft = 4;
    private const double EditorCornerRadius = 4;
    private const double EditorConfirmCardCornerRadius = 6;

    private const string ConfirmCardBackgroundBrushKey = "SystemControlBackgroundChromeMediumLowBrush";
    private const string ConfirmCardBorderBrushKey = "SystemControlTransientBorderBrush";
    private const string ConfirmTextBrushKey = "SystemFillColorAttentionBrush";
    private const string ConfirmAccentBrushKey = "SystemAccentColorBrush";
    private const string ConfirmHoverBrushKey = "SubtleFillColorSecondaryBrush";
    private const string ConfirmPressedBrushKey = "SubtleFillColorTertiaryBrush";
    private const string ConfirmTextPrimaryBrushKey = "TextFillColorPrimaryBrush";

    private const string ClearTabTooltip = "Clear this tab";
    private const string ConfirmCardPrompt = "Clear?";
    private const string ConfirmCheckGlyph = "✓";

    /// <summary>
    /// Builds the editor surface for <paramref name="buffer"/>.
    /// </summary>
    /// <param name="buffer">The buffer whose content seeds the editor and whose index tags interactive controls.</param>
    /// <param name="getThemeBrush">Resolves a theme brush by key, walking the panel's resources first then the application resources.</param>
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

        var clearButton = new Button
        {
            Width = EditorClearButtonSize,
            Height = EditorClearButtonSize,
            Padding = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
            IsEnabled = !string.IsNullOrEmpty(buffer.Content),
            Tag = buffer.Index,
            CornerRadius = new CornerRadius(EditorCornerRadius),
            Content = new FontIcon { Glyph = EditorClearGlyph, FontSize = EditorClearGlyphSize },
        };
        ToolTipService.SetToolTip(clearButton, ClearTabTooltip);

        Button confirmButton;
        Border confirmPanel = BuildConfirmPanel(buffer.Index, getThemeBrush, out confirmButton);

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

        return new EditorView
        {
            Container = editorContainer,
            Editor = editor,
            ClearButton = clearButton,
            ConfirmPanel = confirmPanel,
            ConfirmButton = confirmButton,
        };
    }

    private static Border BuildConfirmPanel(int bufferIndex, Func<string, Brush> getThemeBrush, out Button confirmButton)
    {
        Brush cardBg = getThemeBrush(ConfirmCardBackgroundBrushKey);
        Brush cardBorder = getThemeBrush(ConfirmCardBorderBrushKey);
        Brush textFg = getThemeBrush(ConfirmTextBrushKey);
        Brush accentBrush = getThemeBrush(ConfirmAccentBrushKey);
        Brush hoverBg = getThemeBrush(ConfirmHoverBrushKey);
        Brush pressedBg = getThemeBrush(ConfirmPressedBrushKey);
        Brush normalText = getThemeBrush(ConfirmTextPrimaryBrushKey);

        confirmButton = new Button
        {
            Content = ConfirmCheckGlyph,
            Width = EditorConfirmButtonSize,
            Height = EditorConfirmButtonSize,
            Padding = new Thickness(0),
            Margin = new Thickness(EditorConfirmButtonMarginLeft, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            CornerRadius = new CornerRadius(EditorCornerRadius),
            Tag = bufferIndex,
            Foreground = accentBrush,
        };

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
            Text = ConfirmCardPrompt,
            FontSize = EditorConfirmTextFontSize,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = textFg,
            FontWeight = FontWeights.SemiBold,
        });
        content.Children.Add(confirmButton);

        return new Border
        {
            CornerRadius = new CornerRadius(EditorConfirmCardCornerRadius),
            BorderThickness = new Thickness(1),
            BorderBrush = cardBorder,
            Padding = new Thickness(8, 2, 4, 2),
            Background = cardBg,
            VerticalAlignment = VerticalAlignment.Center,
            Child = content,
            Visibility = Visibility.Collapsed,
        };
    }
}
