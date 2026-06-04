using System.Collections.Generic;

namespace QuinSlate.Ui.Models;

/// <summary>
/// A named group of emoji entries for display in the picker.
/// </summary>
public sealed class EmojiGroup
{
    /// <summary>Display name for the category header.</summary>
    public string Name { get; }

    /// <summary>The emoji entries belonging to this group.</summary>
    public IReadOnlyList<EmojiEntry> Entries { get; }

    /// <summary>Constructs a group with the given name and entry list.</summary>
    public EmojiGroup(string name, EmojiEntry[] entries)
    {
        Name = name;
        Entries = entries;
    }
}
