namespace Jott.Ui.Tray;

/// <summary>
/// Display data for a single row in the tray peek window.
/// </summary>
internal sealed class TrayPeekRow
{
    /// <summary>Emoji followed by the tab title, e.g. "📋 Scratch".</summary>
    public string Label { get; }

    /// <summary>First line of the buffer content, or the empty-state placeholder.</summary>
    public string Preview { get; }

    /// <summary><c>true</c> when the buffer has no content; renders the preview as muted.</summary>
    public bool IsEmpty { get; }

    public TrayPeekRow(string label, string preview, bool isEmpty)
    {
        Label = label;
        Preview = preview;
        IsEmpty = isEmpty;
    }
}
