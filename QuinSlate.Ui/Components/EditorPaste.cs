using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Controls;
using QuinSlate.Ui.Constants;
using System;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;

namespace QuinSlate.Ui.Components;

/// <summary>
/// Inserts clipboard text into a <see cref="RichEditBox"/> under the buffer's
/// character cap. This is the single paste path shared by the editor's
/// <c>Paste</c> event and its custom context-menu "Paste" item so both clamp to
/// <see cref="AppConstants.MaxBufferLength"/> and collapse line endings
/// identically.
/// </summary>
internal static class EditorPaste
{
    /// <summary>
    /// Reads text from the clipboard and inserts it at the editor's current
    /// selection, truncated so the resulting document never exceeds
    /// <see cref="AppConstants.MaxBufferLength"/> characters. Non-text clipboard
    /// content is ignored. When the buffer is already at the cap, nothing is
    /// inserted.
    /// </summary>
    /// <param name="editor">The editor to paste into.</param>
    public static async Task PasteClampedAsync(RichEditBox editor)
    {
        if (editor == null)
        {
            throw new ArgumentNullException(nameof(editor));
        }

        DataPackageView dataView = Clipboard.GetContent();
        if (!dataView.Contains(StandardDataFormats.Text))
        {
            return;
        }

        string text = await dataView.GetTextAsync();

        // RichEdit's TypeText inserts a break for each CR and each LF it sees, so
        // clipboard CRLF pairs would double every line. Collapse all line endings to
        // the single CR that RichEdit uses as its paragraph separator.
        text = text.Replace("\r\n", "\r").Replace('\n', '\r');

        editor.Document.GetText(TextGetOptions.None, out string currentText);

        int selectionLength = editor.Document.Selection.Text.Length;
        int maxAllowedPaste = AppConstants.MaxBufferLength - (currentText.Length - selectionLength);

        if (maxAllowedPaste <= 0)
        {
            return;
        }

        if (text.Length > maxAllowedPaste)
        {
            text = text.Substring(0, maxAllowedPaste);
        }

        editor.Document.Selection.TypeText(text);
    }
}
