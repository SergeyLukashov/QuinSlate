using QuinSlate.Ui.Models;
using Buffer = QuinSlate.Ui.Models.Buffer;

namespace QuinSlate.Tests.Models;

public sealed class TabOrderTests
{
    // ── OrderBuffers ──────────────────────────────────────────────────────────

    [Fact]
    public void OrderBuffers_FollowsTheTabOrder()
    {
        List<Buffer> buffers = Buffers(1, 2, 3, 4, 5);

        List<Buffer> ordered = TabOrder.OrderBuffers(buffers, Tabs(3, 1, 5, 2, 4));

        Assert.Equal(new[] { 3, 1, 5, 2, 4 }, ordered.Select(b => b.Index));
    }

    [Fact]
    public void OrderBuffers_BufferNoTabNames_IsAppendedInBufferOrder()
    {
        // A truncated or hand-edited settings.json must never drop a buffer's tab.
        List<Buffer> buffers = Buffers(1, 2, 3, 4, 5);

        List<Buffer> ordered = TabOrder.OrderBuffers(buffers, Tabs(4, 2));

        Assert.Equal(new[] { 4, 2, 1, 3, 5 }, ordered.Select(b => b.Index));
    }

    [Fact]
    public void OrderBuffers_DuplicateAndUnknownTabIds_AreIgnored()
    {
        List<Buffer> buffers = Buffers(1, 2, 3);

        List<Buffer> ordered = TabOrder.OrderBuffers(buffers, Tabs(2, 9, 2, 1));

        Assert.Equal(new[] { 2, 1, 3 }, ordered.Select(b => b.Index));
    }

    [Fact]
    public void OrderBuffers_KeepsTheSameBufferInstances()
    {
        List<Buffer> buffers = Buffers(1, 2);

        List<Buffer> ordered = TabOrder.OrderBuffers(buffers, Tabs(2, 1));

        Assert.Same(buffers[1], ordered[0]);
        Assert.Same(buffers[0], ordered[1]);
    }

    // ── FromStrip ─────────────────────────────────────────────────────────────

    [Fact]
    public void FromStrip_ReturnsTheDefinitionsInStripOrder()
    {
        List<TabDefinition> tabs = Tabs(1, 2, 3);

        List<TabDefinition> reordered = TabOrder.FromStrip(new[] { 3, 1, 2 }, tabs);

        Assert.Equal(new[] { 3, 1, 2 }, reordered.Select(t => t.Id));
        Assert.Same(tabs[2], reordered[0]);
    }

    [Fact]
    public void FromStrip_IndexWithoutDefinition_IsSkipped()
    {
        List<TabDefinition> reordered = TabOrder.FromStrip(new[] { 2, 7, 1 }, Tabs(1, 2));

        Assert.Equal(new[] { 2, 1 }, reordered.Select(t => t.Id));
    }

    [Fact]
    public void FromStrip_NullDefinitions_ReturnsEmpty()
    {
        Assert.Empty(TabOrder.FromStrip(new[] { 1, 2 }, null));
    }

    // ── Matches ───────────────────────────────────────────────────────────────

    [Fact]
    public void Matches_SameIdsInSameOrder_IsTrue()
    {
        Assert.True(TabOrder.Matches(Tabs(1, 2, 3), Tabs(1, 2, 3)));
    }

    [Fact]
    public void Matches_SameIdsInDifferentOrder_IsFalse()
    {
        Assert.False(TabOrder.Matches(Tabs(1, 2, 3), Tabs(1, 3, 2)));
    }

    [Fact]
    public void Matches_DifferentCounts_IsFalse()
    {
        Assert.False(TabOrder.Matches(Tabs(1, 2), Tabs(1, 2, 3)));
    }

    [Fact]
    public void Matches_NullRight_IsFalse()
    {
        Assert.False(TabOrder.Matches(Tabs(1), null));
    }

    [Fact]
    public void Matches_IgnoresLabels()
    {
        List<TabDefinition> relabelled = Tabs(1, 2);
        relabelled[0].Title = "Renamed";

        Assert.True(TabOrder.Matches(relabelled, Tabs(1, 2)));
    }

    // ── Find ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Find_KnownId_ReturnsItsDefinition()
    {
        List<TabDefinition> tabs = Tabs(4, 2);

        Assert.Same(tabs[1], TabOrder.Find(tabs, 2));
    }

    [Fact]
    public void Find_UnknownId_ReturnsNull()
    {
        Assert.Null(TabOrder.Find(Tabs(1, 2), 5));
    }

    [Fact]
    public void Find_NullDefinitions_ReturnsNull()
    {
        Assert.Null(TabOrder.Find(null, 1));
    }

    private static List<Buffer> Buffers(params int[] indices)
    {
        return indices.Select(i => new Buffer(i, $"buffer{i}.txt", string.Empty)).ToList();
    }

    private static List<TabDefinition> Tabs(params int[] ids)
    {
        return ids.Select(id => new TabDefinition { Id = id, Emoji = "📋", Title = id.ToString() }).ToList();
    }
}
