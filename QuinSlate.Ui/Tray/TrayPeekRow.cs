namespace QuinSlate.Ui.Tray;

/// <summary>
/// Display data for a single row in the tray peek window.
/// </summary>
internal sealed class TrayPeekRow
{
    /// <summary>The 1-based index/number of the buffer.</summary>
    public int Number { get; }

    /// <summary>Emoji character(s) for the tab.</summary>
    public string Emoji { get; }

    /// <summary>Display title for the tab.</summary>
    public string Title { get; }

    /// <summary>First line of the buffer content, or empty if no content.</summary>
    public string Preview { get; }

    /// <summary><c>true</c> when the buffer has no content.</summary>
    public bool IsEmpty { get; }

    public TrayPeekRow(int number, string emoji, string title, string preview, bool isEmpty)
    {
        Number = number;
        Emoji = emoji;
        Title = title;
        Preview = preview;
        IsEmpty = isEmpty;
    }
}
