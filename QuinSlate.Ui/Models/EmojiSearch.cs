using System;
using System.Collections.Generic;

namespace QuinSlate.Ui.Models;

/// <summary>
/// Pure emoji search matching for the picker. The rule is a raw, untrimmed,
/// case-insensitive substring test against each entry's keywords and emoji
/// text; a null, empty, or whitespace-only query means browse mode and never
/// reaches matching.
/// </summary>
public static class EmojiSearch
{
    /// <summary>
    /// Returns true when <paramref name="query"/> selects browse mode
    /// (null, empty, or whitespace-only) rather than a search.
    /// </summary>
    public static bool IsBrowseQuery(string query)
    {
        return string.IsNullOrWhiteSpace(query);
    }

    /// <summary>
    /// Returns the indices into <paramref name="entries"/> whose keywords or
    /// emoji contain <paramref name="query"/> (ordinal, case-insensitive,
    /// untrimmed), in display order. Browse-mode queries and a null entry
    /// list yield an empty result.
    /// </summary>
    public static IReadOnlyList<int> FindMatchIndices(IReadOnlyList<EmojiEntry> entries, string query)
    {
        if (entries == null || IsBrowseQuery(query))
        {
            return Array.Empty<int>();
        }

        var matches = new List<int>();
        for (int i = 0; i < entries.Count; i++)
        {
            EmojiEntry entry = entries[i];
            if (entry == null)
            {
                continue;
            }

            bool isMatch = (entry.Keywords != null && entry.Keywords.Contains(query, StringComparison.OrdinalIgnoreCase))
                        || (entry.Emoji != null && entry.Emoji.Contains(query, StringComparison.OrdinalIgnoreCase));

            if (isMatch)
            {
                matches.Add(i);
            }
        }

        return matches;
    }
}
