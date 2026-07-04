using QuinSlate.Ui.Models;

namespace QuinSlate.Tests.Models;

public sealed class EmojiSearchTests
{
    private static readonly IReadOnlyList<EmojiEntry> Entries = new[]
    {
        new EmojiEntry("😀", "grinning face smile happy"),
        new EmojiEntry("🐶", "dog puppy pet"),
        new EmojiEntry("🐱", "cat kitten pet"),
        new EmojiEntry("🔥", "fire hot flame"),
        new EmojiEntry("🧊", null),
    };

    // ── IsBrowseQuery ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void IsBrowseQuery_NullEmptyOrWhitespace_IsTrue(string query)
    {
        Assert.True(EmojiSearch.IsBrowseQuery(query));
    }

    [Theory]
    [InlineData("a")]
    [InlineData(" a")]
    [InlineData("🐶")]
    public void IsBrowseQuery_NonWhitespaceQuery_IsFalse(string query)
    {
        Assert.False(EmojiSearch.IsBrowseQuery(query));
    }

    // ── FindMatchIndices ──────────────────────────────────────────────────────

    [Fact]
    public void FindMatchIndices_KeywordSubstring_Matches()
    {
        Assert.Equal(new[] { 1 }, EmojiSearch.FindMatchIndices(Entries, "dog"));
    }

    [Fact]
    public void FindMatchIndices_IsCaseInsensitive()
    {
        Assert.Equal(new[] { 1 }, EmojiSearch.FindMatchIndices(Entries, "DOG"));
    }

    [Fact]
    public void FindMatchIndices_EmojiLiteral_Matches()
    {
        Assert.Equal(new[] { 1 }, EmojiSearch.FindMatchIndices(Entries, "🐶"));
    }

    [Fact]
    public void FindMatchIndices_EmojiLiteralWithNullKeywords_Matches()
    {
        Assert.Equal(new[] { 4 }, EmojiSearch.FindMatchIndices(Entries, "🧊"));
    }

    [Fact]
    public void FindMatchIndices_MultipleMatches_PreserveDisplayOrder()
    {
        Assert.Equal(new[] { 1, 2 }, EmojiSearch.FindMatchIndices(Entries, "pet"));
    }

    [Fact]
    public void FindMatchIndices_MidWordSubstring_Matches()
    {
        Assert.Equal(new[] { 0, 2, 3 }, EmojiSearch.FindMatchIndices(Entries, "a"));
    }

    [Fact]
    public void FindMatchIndices_QueryIsUsedRawWithoutTrimming()
    {
        // " p" (leading space) only matches keywords containing space + p.
        Assert.Equal(new[] { 1, 2 }, EmojiSearch.FindMatchIndices(Entries, " p"));
    }

    [Fact]
    public void FindMatchIndices_NoMatch_ReturnsEmpty()
    {
        Assert.Empty(EmojiSearch.FindMatchIndices(Entries, "zzz"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FindMatchIndices_BrowseQuery_ReturnsEmpty(string query)
    {
        Assert.Empty(EmojiSearch.FindMatchIndices(Entries, query));
    }

    [Fact]
    public void FindMatchIndices_NullEntries_ReturnsEmpty()
    {
        Assert.Empty(EmojiSearch.FindMatchIndices(null, "dog"));
    }

    [Fact]
    public void FindMatchIndices_NullEntryElement_IsSkipped()
    {
        var entries = new EmojiEntry[]
        {
            null,
            new EmojiEntry("🐶", "dog puppy pet"),
        };

        Assert.Equal(new[] { 1 }, EmojiSearch.FindMatchIndices(entries, "dog"));
    }
}
