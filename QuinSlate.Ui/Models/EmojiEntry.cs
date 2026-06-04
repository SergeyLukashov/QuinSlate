namespace QuinSlate.Ui.Models;

/// <summary>
/// A single emoji with its associated search keywords.
/// </summary>
public sealed class EmojiEntry
{
    /// <summary>The emoji character(s).</summary>
    public string Emoji { get; }

    /// <summary>Space-separated keywords for search matching.</summary>
    public string Keywords { get; }

    /// <summary>Constructs an entry with the given emoji and keywords.</summary>
    public EmojiEntry(string emoji, string keywords)
    {
        Emoji = emoji;
        Keywords = keywords;
    }
}
