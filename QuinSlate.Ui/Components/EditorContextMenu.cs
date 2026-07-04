using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using QuinSlate.Ui.Interop;
using System;

namespace QuinSlate.Ui.Components;

/// <summary>
/// Creates and manages the custom context menu for the RichEditBox editor.
/// </summary>
internal static class EditorContextMenu
{
    /// <summary>
    /// Creates and assigns a custom context menu to the given RichEditBox editor
    /// containing standard editing actions with matching SymbolIcon icons.
    /// </summary>
    /// <param name="editor">The RichEditBox editor to configure.</param>
    public static void Create(RichEditBox editor)
    {
        if (editor == null)
        {
            throw new ArgumentNullException(nameof(editor));
        }

        var menu = new MenuFlyout();

        var presenterStyle = new Style(typeof(MenuFlyoutPresenter));
        presenterStyle.Setters.Add(new Setter(Control.MinWidthProperty, 135));
        menu.MenuFlyoutPresenterStyle = presenterStyle;

        var undoItem = new MenuFlyoutItem { Text = "Undo", Icon = new SymbolIcon(Symbol.Undo) };
        var redoItem = new MenuFlyoutItem { Text = "Redo", Icon = new SymbolIcon(Symbol.Redo) };
        var cutItem = new MenuFlyoutItem { Text = "Cut", Icon = new SymbolIcon(Symbol.Cut) };
        var copyItem = new MenuFlyoutItem { Text = "Copy", Icon = new SymbolIcon(Symbol.Copy) };
        var pasteItem = new MenuFlyoutItem { Text = "Paste", Icon = new SymbolIcon(Symbol.Paste) };
        var selectAllItem = new MenuFlyoutItem { Text = "Select All", Icon = new SymbolIcon(Symbol.SelectAll) };

        undoItem.Click += (s, e) => editor.Document.Undo();
        redoItem.Click += (s, e) => editor.Document.Redo();
        cutItem.Click += (s, e) => editor.Document.Selection.Cut();
        copyItem.Click += (s, e) => editor.Document.Selection.Copy();
        pasteItem.Click += async (s, e) => await EditorPaste.PasteClampedAsync(editor);
        selectAllItem.Click += (s, e) => editor.Document.Selection.SetRange(0, int.MaxValue);

        menu.Items.Add(undoItem);
        menu.Items.Add(redoItem);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(cutItem);
        menu.Items.Add(copyItem);
        menu.Items.Add(pasteItem);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(selectAllItem);

        menu.Opening += (s, e) =>
        {
            var doc = editor.Document;
            undoItem.IsEnabled = doc.CanUndo();
            redoItem.IsEnabled = doc.CanRedo();

            var sel = doc.Selection;
            bool hasSelection = sel.StartPosition != sel.EndPosition;
            cutItem.IsEnabled = hasSelection;
            copyItem.IsEnabled = hasSelection;
            pasteItem.IsEnabled = true;
        };

        // Forces the arrow cursor the instant the menu's windowed popup appears so the
        // busy/app-starting cursor never shows. See microsoft-ui-xaml#8829.
        menu.Opened += (s, e) =>
        {
            NativeMethods.SetCursor(NativeMethods.LoadCursor(IntPtr.Zero, NativeMethods.IDC_ARROW));
        };

        editor.ContextFlyout = menu;
    }
}
