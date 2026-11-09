using Microsoft.UI.Xaml;
using QuinSlate.Ui.Services;
using System;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace QuinSlate.Ui.Components;

/// <summary>
/// Sends the editor page its per-theme colours (text, caret, selection, calc accent, link), and
/// re-sends them when the user changes their Windows accent or the system theme.
/// </summary>
internal sealed class EditorThemeColors
{
    // WinUI's TextControlForeground resolves to TextFillColorPrimaryBrush, the foreground the retired
    // RichEditBox inherited. Dark is opaque white; light is black at 0xE4 alpha, *not* an opaque near-
    // black. The alpha matters: it lets the warm gradient mesh tint the glyphs instead of flattening
    // them to a neutral grey, and it must survive the trip across the bridge as rgba().
    private static readonly Color EditorTextColorDark = Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);
    private static readonly Color EditorTextColorLight = Color.FromArgb(0xE4, 0x00, 0x00, 0x00);

    // What AccentTextColorResolver blends toward when a user's accent shade is still too dim against
    // the gradient. Opaque by construction: a blend needs a solid endpoint, so these are pure white /
    // pure black rather than the alpha-carrying editor text colours above.
    private static readonly Color LinkReinforcementDark = Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);
    private static readonly Color LinkReinforcementLight = Color.FromArgb(0xFF, 0x00, 0x00, 0x00);

    // Must stay rooted in a field: UISettings raises ColorValuesChanged off a weak reference, so a
    // collected instance silently stops delivering accent changes.
    private readonly UISettings uiSettings = new UISettings();

    private readonly FrameworkElement themeSource;
    private readonly EditorHost editorHost;

    /// <summary>Creates the colour source for <paramref name="editorHost"/>.</summary>
    /// <param name="themeSource">The element whose <c>ActualTheme</c> the editor follows.</param>
    /// <param name="editorHost">The editor bridge the colours are sent over.</param>
    public EditorThemeColors(FrameworkElement themeSource, EditorHost editorHost)
    {
        if (themeSource == null)
        {
            throw new ArgumentNullException(nameof(themeSource));
        }

        this.themeSource = themeSource;
        this.editorHost = editorHost;
    }

    /// <summary>
    /// Starts re-sending the colours on every system colour change. The owner must keep this
    /// instance alive for as long as it wants the updates (see the note on the settings field).
    /// </summary>
    public void TrackSystemColorChanges()
    {
        uiSettings.ColorValuesChanged += OnColorValuesChanged;
    }

    /// <summary>
    /// Sends the editor's per-theme colours (text, caret, selection, calc accent, link) to the page.
    /// Read from the current theme so the surface matches the RichEditBox foreground, and the Windows
    /// accent so selection and the calc highlight match the rest of the shell.
    /// </summary>
    public void Apply()
    {
        if (editorHost == null)
        {
            return;
        }

        bool isDark = themeSource.ActualTheme == ElementTheme.Dark;
        Color text = isDark ? EditorTextColorDark : EditorTextColorLight;

        // The RichEditBox took its SelectionHighlightColor from TextControlSelectionHighlightColor,
        // which resolves to AccentFillColorSelectedTextBackgroundBrush -> the opaque SystemAccentColor.
        // CodeMirror paints .cm-selectionBackground beneath the text, so an opaque fill reproduces it.
        Color accent = ReadAccentColor();
        editorHost.SetTheme(text, text, accent, accent, ReadLinkColor(isDark));
    }

    /// <summary>
    /// The colour link text is drawn in: the OS accent shade WinUI itself uses for accent-coloured
    /// text, verified against the gradient the editor actually paints on.
    /// </summary>
    private Color ReadLinkColor(bool isDark)
    {
        // AccentTextFillColorPrimaryBrush resolves to SystemAccentColorLight3 on dark and
        // SystemAccentColorDark2 on light; these are the same shades behind those resources.
        // Read from UISettings rather than the XAML resource for the reason ReadAccentColor gives:
        // ColorValuesChanged can beat XAML's theme-resource refresh.
        Color preferred = uiSettings.GetColorValue(isDark ? UIColorType.AccentLight3 : UIColorType.AccentDark2);
        return AccentTextColorResolver.Resolve(
            preferred,
            DitheredGradientBrushFactory.MidColor(isDark),
            isDark ? LinkReinforcementDark : LinkReinforcementLight);
    }

    /// <summary>
    /// Re-sends the editor colours when the user changes their Windows accent (or the system theme)
    /// while the app is running. Raised on a background thread, so it must hop to the UI thread.
    /// </summary>
    private void OnColorValuesChanged(UISettings sender, object args)
    {
        themeSource.DispatcherQueue.TryEnqueue(Apply);
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
}
