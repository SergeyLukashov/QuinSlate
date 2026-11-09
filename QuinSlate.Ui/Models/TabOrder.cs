using System.Collections.Generic;

namespace QuinSlate.Ui.Models;

/// <summary>
/// The left-to-right order of the tabs. <c>settings.json</c>'s tab array order <em>is</em> the tab
/// order, and a tab's buffer identity is its <see cref="TabDefinition.Id"/>, so reordering never
/// moves text between buffers.
/// </summary>
internal static class TabOrder
{
    /// <summary>
    /// Returns <paramref name="buffers"/> in the left-to-right order the persisted
    /// <paramref name="tabs"/> describe. Any buffer no tab definition names is appended in buffer
    /// order, so a truncated or hand-edited <c>settings.json</c> can never drop a buffer's tab.
    /// </summary>
    public static List<Buffer> OrderBuffers(IReadOnlyList<Buffer> buffers, IReadOnlyList<TabDefinition> tabs)
    {
        var byIndex = new Dictionary<int, Buffer>(buffers.Count);
        foreach (Buffer buffer in buffers)
        {
            byIndex[buffer.Index] = buffer;
        }

        var ordered = new List<Buffer>(buffers.Count);
        foreach (TabDefinition tab in tabs)
        {
            if (byIndex.TryGetValue(tab.Id, out Buffer buffer) && !ordered.Contains(buffer))
            {
                ordered.Add(buffer);
            }
        }

        foreach (Buffer buffer in buffers)
        {
            if (!ordered.Contains(buffer))
            {
                ordered.Add(buffer);
            }
        }

        return ordered;
    }

    /// <summary>
    /// The definitions of the buffers in <paramref name="stripBufferIndices"/>, in that order. An
    /// index <paramref name="tabs"/> has no definition for is skipped.
    /// </summary>
    public static List<TabDefinition> FromStrip(IReadOnlyList<int> stripBufferIndices, IReadOnlyList<TabDefinition> tabs)
    {
        var reordered = new List<TabDefinition>(stripBufferIndices.Count);
        foreach (int bufferIndex in stripBufferIndices)
        {
            TabDefinition found = Find(tabs, bufferIndex);
            if (found != null)
            {
                reordered.Add(found);
            }
        }

        return reordered;
    }

    /// <summary>
    /// Whether <paramref name="left"/> and <paramref name="right"/> hold the same tabs in the same
    /// order. A null <paramref name="right"/> never matches.
    /// </summary>
    public static bool Matches(IReadOnlyList<TabDefinition> left, IReadOnlyList<TabDefinition> right)
    {
        if (right == null || left.Count != right.Count)
        {
            return false;
        }

        for (int i = 0; i < left.Count; i++)
        {
            if (left[i].Id != right[i].Id)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The definition of buffer <paramref name="bufferIndex"/>, or null when there is none.</summary>
    public static TabDefinition Find(IReadOnlyList<TabDefinition> tabs, int bufferIndex)
    {
        if (tabs == null)
        {
            return null;
        }

        foreach (var td in tabs)
        {
            if (td.Id == bufferIndex)
            {
                return td;
            }
        }

        return null;
    }
}
