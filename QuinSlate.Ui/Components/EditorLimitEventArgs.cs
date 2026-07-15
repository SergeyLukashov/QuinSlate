using System;

namespace QuinSlate.Ui.Components;

/// <summary>
/// Carries a report from the editor page that a user edit was clamped at the buffer character
/// cap (<c>AppConstants.MaxBufferLength</c>): the buffer it happened in, what the user was doing,
/// and how many characters were dropped. No buffer text crosses the bridge with it.
/// </summary>
internal sealed class EditorLimitEventArgs : EventArgs
{
    /// <summary>Creates the event args.</summary>
    public EditorLimitEventArgs(int index, string cause, int droppedCharacters)
    {
        Index = index;
        Cause = cause;
        DroppedCharacters = droppedCharacters;
    }

    /// <summary>The 1-based buffer index the clamped edit was made in.</summary>
    public int Index { get; }

    /// <summary>
    /// What the user did: <see cref="EditorHost.CausePaste"/> (paste or drag-drop) or
    /// <see cref="EditorHost.CauseType"/> (typing, IME, or dictation).
    /// </summary>
    public string Cause { get; }

    /// <summary>How many characters did not fit and were dropped, counted in CRLF form.</summary>
    public int DroppedCharacters { get; }
}
