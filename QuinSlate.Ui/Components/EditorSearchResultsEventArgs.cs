using System;
using System.Collections.Generic;

namespace QuinSlate.Ui.Components;

/// <summary>
/// The editor page's answer to a search: how many matches each slate holds, which tier found them,
/// and where the current match sits in walk order. Counts and positions only — no text crosses the
/// bridge with it.
/// </summary>
internal sealed class EditorSearchResultsEventArgs : EventArgs
{
    private readonly IReadOnlyList<int> counts;
    private readonly IReadOnlyList<bool> capped;

    /// <summary>Creates the event args.</summary>
    /// <param name="counts">Match count per slate, element 0 being buffer 1.</param>
    /// <param name="capped">Whether each slate stopped counting at the cap, in the same order.</param>
    /// <param name="tier">The tier the matches came from; 0 when nothing matched.</param>
    /// <param name="currentIndex">The buffer the current match is in; 0 when there is none.</param>
    /// <param name="currentOrdinal">The current match's 1-based position among all matches; 0 when there is none.</param>
    public EditorSearchResultsEventArgs(IReadOnlyList<int> counts, IReadOnlyList<bool> capped, int tier, int currentIndex, int currentOrdinal)
    {
        this.counts = counts ?? Array.Empty<int>();
        this.capped = capped ?? Array.Empty<bool>();
        Tier = tier;
        CurrentIndex = currentIndex;
        CurrentOrdinal = currentOrdinal;

        foreach (int count in this.counts)
        {
            Total += count;
        }

        foreach (bool isCapped in this.capped)
        {
            AnyCapped |= isCapped;
        }
    }

    /// <summary>The tier the matches came from (1 phrase, 2 all words, 3 close); 0 when nothing matched.</summary>
    public int Tier { get; }

    /// <summary>The buffer the current match is in; 0 when there is no current match.</summary>
    public int CurrentIndex { get; }

    /// <summary>The current match's 1-based position among all matches in walk order; 0 when there is none.</summary>
    public int CurrentOrdinal { get; }

    /// <summary>Matches across every slate.</summary>
    public int Total { get; }

    /// <summary>Whether any slate stopped counting at the cap, so <see cref="Total"/> is a floor.</summary>
    public bool AnyCapped { get; }

    /// <summary>The match count of the 1-based buffer <paramref name="bufferIndex"/>.</summary>
    public int CountFor(int bufferIndex)
    {
        int position = bufferIndex - 1;
        return position >= 0 && position < counts.Count ? counts[position] : 0;
    }
}
