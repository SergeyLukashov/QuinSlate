using Microsoft.Web.WebView2.Core;

namespace QuinSlate.Ui.Components;

/// <summary>
/// The hardened <see cref="CoreWebView2Settings"/> the editor's WebView2 runs with: a text editor,
/// not a browser.
/// </summary>
internal static class EditorWebViewSettings
{
    /// <summary>
    /// Turns off every browser affordance the editor page must not have: default context menus,
    /// accelerator keys, zoom, the status bar, swipe navigation, autofill, and (outside Debug)
    /// DevTools.
    /// </summary>
    public static void Harden(CoreWebView2Settings settings)
    {
        settings.AreDefaultContextMenusEnabled = false;
        settings.AreBrowserAcceleratorKeysEnabled = false;
        settings.IsZoomControlEnabled = false;
        settings.IsStatusBarEnabled = false;
        settings.IsSwipeNavigationEnabled = false;
        settings.IsPasswordAutosaveEnabled = false;
        settings.IsGeneralAutofillEnabled = false;
        settings.IsPinchZoomEnabled = false;
#if DEBUG
        settings.AreDevToolsEnabled = true;
#else
        settings.AreDevToolsEnabled = false;
#endif
    }
}
