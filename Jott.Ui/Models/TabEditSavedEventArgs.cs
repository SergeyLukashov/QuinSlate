using System;

namespace Jott.Ui.Models;

/// <summary>
/// Carries the result of a confirmed tab-label edit.
/// </summary>
public sealed class TabEditSavedEventArgs : EventArgs
{
    /// <summary>Gets the 1-based index of the edited tab.</summary>
    public int BufferIndex { get; }

    /// <summary>Gets the chosen emoji for the tab.</summary>
    public string Emoji { get; }

    /// <summary>Gets the new title for the tab.</summary>
    public string Title { get; }

    /// <summary>
    /// Initialises a new instance with the given buffer index, emoji, and title.
    /// </summary>
    public TabEditSavedEventArgs(int bufferIndex, string emoji, string title)
    {
        BufferIndex = bufferIndex;
        Emoji = emoji;
        Title = title;
    }
}
