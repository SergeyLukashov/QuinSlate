using System;

namespace QuinSlate.Ui.Components;

/// <summary>
/// Carries a debounced content-sync push from the editor page: the full current text of one
/// buffer, which the host mirrors and hands to <c>BufferService</c>.
/// </summary>
internal sealed class EditorContentEventArgs : EventArgs
{
    /// <summary>Creates the event args.</summary>
    public EditorContentEventArgs(int index, string text)
    {
        Index = index;
        Text = text;
    }

    /// <summary>The 1-based buffer index the text belongs to.</summary>
    public int Index { get; }

    /// <summary>The buffer's full text, with <c>\n</c> line breaks (the host converts to CRLF).</summary>
    public string Text { get; }
}
