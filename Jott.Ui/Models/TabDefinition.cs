namespace Jott.Ui.Models;

/// <summary>
/// User-editable metadata for a single tab: its emoji and display title.
/// </summary>
public sealed class TabDefinition
{
    /// <summary>1-based tab identifier, matching the buffer index.</summary>
    public int Id { get; set; }

    /// <summary>Emoji character(s) shown at the left of the tab header.</summary>
    public string Emoji { get; set; }

    /// <summary>Display title shown after the emoji in the tab header.</summary>
    public string Title { get; set; }
}
