using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using QuinSlate.Ui.Interop;
using System;
using Windows.Foundation;

namespace QuinSlate.Ui.Components;

/// <summary>
/// The editor's right-click context menu: the same native <see cref="MenuFlyout"/> the RichEditBox
/// showed (Undo / Redo / — / Cut / Copy / Paste / — / Select All, matching icons and 135px min
/// width). Because the editor now lives in a WebView2, the menu is opened by the host at the page's
/// reported click position and its actions are routed back over the bridge.
/// </summary>
internal sealed class EditorContextMenu
{
    private const double MenuMinWidth = 135;

    private readonly MenuFlyout menu;
    private readonly MenuFlyoutItem undoItem;
    private readonly MenuFlyoutItem redoItem;
    private readonly MenuFlyoutItem cutItem;
    private readonly MenuFlyoutItem copyItem;
    private readonly MenuFlyoutItem pasteItem;

    /// <summary>
    /// Builds the menu, wiring each item to a host callback that forwards the action to the editor
    /// page (paste is host-driven so it reuses the STA clipboard path and cap clamp).
    /// </summary>
    public EditorContextMenu(
        Action onUndo,
        Action onRedo,
        Action onCut,
        Action onCopy,
        Action onPaste,
        Action onSelectAll)
    {
        menu = new MenuFlyout();

        var presenterStyle = new Style(typeof(MenuFlyoutPresenter));
        presenterStyle.Setters.Add(new Setter(Control.MinWidthProperty, MenuMinWidth));
        menu.MenuFlyoutPresenterStyle = presenterStyle;

        undoItem = new MenuFlyoutItem { Text = "Undo", Icon = new SymbolIcon(Symbol.Undo) };
        redoItem = new MenuFlyoutItem { Text = "Redo", Icon = new SymbolIcon(Symbol.Redo) };
        cutItem = new MenuFlyoutItem { Text = "Cut", Icon = new SymbolIcon(Symbol.Cut) };
        copyItem = new MenuFlyoutItem { Text = "Copy", Icon = new SymbolIcon(Symbol.Copy) };
        pasteItem = new MenuFlyoutItem { Text = "Paste", Icon = new SymbolIcon(Symbol.Paste) };
        var selectAllItem = new MenuFlyoutItem { Text = "Select All", Icon = new SymbolIcon(Symbol.SelectAll) };

        undoItem.Click += (s, e) => onUndo?.Invoke();
        redoItem.Click += (s, e) => onRedo?.Invoke();
        cutItem.Click += (s, e) => onCut?.Invoke();
        copyItem.Click += (s, e) => onCopy?.Invoke();
        pasteItem.Click += (s, e) => onPaste?.Invoke();
        selectAllItem.Click += (s, e) => onSelectAll?.Invoke();

        menu.Items.Add(undoItem);
        menu.Items.Add(redoItem);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(cutItem);
        menu.Items.Add(copyItem);
        menu.Items.Add(pasteItem);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(selectAllItem);

        // Forces the arrow cursor the instant the menu's windowed popup appears so the busy/
        // app-starting cursor never shows over it. See microsoft-ui-xaml#8829.
        menu.Opened += (s, e) =>
        {
            NativeMethods.SetCursor(NativeMethods.LoadCursor(IntPtr.Zero, NativeMethods.IDC_ARROW));
        };
    }

    /// <summary>
    /// Shows the menu at <paramref name="position"/> (in DIPs relative to <paramref name="target"/>)
    /// with the given enablement: Undo/Redo per the page's history state, Cut/Copy only with a
    /// selection, Paste always enabled.
    /// </summary>
    public void ShowAt(FrameworkElement target, Point position, bool canUndo, bool canRedo, bool hasSelection)
    {
        undoItem.IsEnabled = canUndo;
        redoItem.IsEnabled = canRedo;
        cutItem.IsEnabled = hasSelection;
        copyItem.IsEnabled = hasSelection;
        pasteItem.IsEnabled = true;

        menu.ShowAt(target, new FlyoutShowOptions { Position = position });
    }
}
