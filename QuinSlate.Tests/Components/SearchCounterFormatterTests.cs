using QuinSlate.Ui.Components;
using System;
using System.Globalization;

namespace QuinSlate.Tests.Components;

public sealed class SearchCounterFormatterTests : IDisposable
{
    private const int PhraseTier = 1;

    // The counter groups thousands in the user's culture; the tests pin one so they read the same
    // on every machine, and put the runner's back afterwards.
    private readonly CultureInfo originalCulture = CultureInfo.CurrentCulture;

    public SearchCounterFormatterTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
    }

    public void Dispose()
    {
        CultureInfo.CurrentCulture = originalCulture;
    }

    [Fact]
    public void Format_CurrentMatch_ShowsPositionOfTotal()
    {
        SearchCounterText counter = SearchCounterFormatter.Format(6, false, PhraseTier, 2);
        Assert.Equal("2 of 6", counter.Display);
        Assert.Equal("2 of 6", counter.Accessible);
        Assert.Null(counter.ToolTip);
        Assert.False(counter.IsMuted);
        Assert.True(counter.CanStep);
    }

    [Fact]
    public void Format_NoCurrentMatch_ShowsTheTotalOnly()
    {
        Assert.Equal("6 results", SearchCounterFormatter.Format(6, false, PhraseTier, 0).Display);
        Assert.Equal("1 result", SearchCounterFormatter.Format(1, false, PhraseTier, 0).Display);
    }

    [Fact]
    public void Format_CloseTier_MarksTheCounterAndSpellsItOut()
    {
        SearchCounterText counter = SearchCounterFormatter.Format(3, false, SearchCounterFormatter.CloseTier, 1);
        Assert.Equal("≈ 1 of 3", counter.Display);
        Assert.Equal("1 of 3 close matches", counter.Accessible);
        Assert.Equal("No exact matches — showing close ones", counter.ToolTip);
    }

    [Fact]
    public void Format_NothingMatched_IsMutedAndCannotStep()
    {
        SearchCounterText counter = SearchCounterFormatter.Format(0, false, 0, 0);
        Assert.Equal("No results", counter.Display);
        Assert.True(counter.IsMuted);
        Assert.False(counter.CanStep);
    }

    [Fact]
    public void Format_CappedSlate_SaysTheTotalIsAFloor()
    {
        Assert.Equal("9,999+ results", SearchCounterFormatter.Format(9999, true, PhraseTier, 0).Display);
        Assert.Equal("1 of 10,004+", SearchCounterFormatter.Format(10004, true, PhraseTier, 1).Display);
    }
}
