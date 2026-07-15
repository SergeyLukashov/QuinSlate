namespace QuinSlate.Ui.Components;

/// <summary>
/// What the user was doing when an edit was clamped at the buffer character cap. The notice is
/// worded from this; the editor page's bridge vocabulary stops at <see cref="EditorHost"/>.
/// </summary>
public enum LimitNoticeCause
{
    /// <summary>Typing, an IME commit, dictation, or the Windows emoji panel.</summary>
    Typing,

    /// <summary>A paste or a drag-drop of text.</summary>
    Paste,
}
