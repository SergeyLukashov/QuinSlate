using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace QuinSlate.Ui.Components;

/// <summary>
/// Places keyboard focus into a <see cref="RichEditBox"/>, deferring until the
/// editor's visual tree is realised when the control has not yet loaded.
/// </summary>
/// <remarks>
/// Only one pending focus request is tracked at a time. Calling
/// <see cref="FocusWhenReady"/> again before the previous deferred request fires
/// cancels the previous request and targets the new editor instead.
/// </remarks>
public sealed class EditorFocusController
{
    private RichEditBox pendingFocusEditor;
    private RoutedEventHandler pendingFocusHandler;

    /// <summary>
    /// Focuses <paramref name="editor"/> programmatically. If the editor is not yet
    /// loaded the focus request is deferred until its <c>Loaded</c> event fires.
    /// </summary>
    /// <param name="editor">The editor to focus. A <c>null</c> argument is a no-op.</param>
    public void FocusWhenReady(RichEditBox editor)
    {
        ClearPendingFocusHandler();

        if (editor == null)
        {
            return;
        }

        if (editor.IsLoaded)
        {
            editor.DispatcherQueue.TryEnqueue(() => editor.Focus(FocusState.Programmatic));
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

        editor.DispatcherQueue.TryEnqueue(() => editor.Focus(FocusState.Programmatic));
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
}
