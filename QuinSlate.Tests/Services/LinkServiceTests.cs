using QuinSlate.Ui.Services;

namespace QuinSlate.Tests.Services;

public sealed class LinkServiceTests
{
    [Fact]
    public void NullHref_ReturnsFalse()
    {
        Assert.False(LinkService.TryCreateLaunchUri(null, out _));
    }

    [Fact]
    public void EmptyHref_ReturnsFalse()
    {
        Assert.False(LinkService.TryCreateLaunchUri(string.Empty, out _));
    }

    [Fact]
    public void WhitespaceHref_ReturnsFalse()
    {
        Assert.False(LinkService.TryCreateLaunchUri("   ", out _));
    }

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("http://example.com/a/b?c=1&d=2")]
    [InlineData("http://localhost:3000/x#top")]
    [InlineData("mailto:someone@example.com")]
    public void LaunchableScheme_ReturnsTrueAndTheUri(string href)
    {
        Assert.True(LinkService.TryCreateLaunchUri(href, out Uri uri));
        Assert.NotNull(uri);
        Assert.Equal(href, uri.OriginalString);
    }

    [Fact]
    public void UppercaseScheme_IsLaunchable()
    {
        Assert.True(LinkService.TryCreateLaunchUri("HTTPS://EXAMPLE.COM", out Uri uri));
        Assert.Equal("https", uri.Scheme);
    }

    [Theory]
    [InlineData("ftp://example.com")]
    [InlineData("file:///c:/windows/system32/cmd.exe")]
    [InlineData("javascript:alert(1)")]
    [InlineData("ms-settings:privacy")]
    [InlineData("vbscript:msgbox(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    public void UnsupportedScheme_ReturnsFalse(string href)
    {
        Assert.False(LinkService.TryCreateLaunchUri(href, out Uri uri));
        Assert.Null(uri);
    }

    [Theory]
    [InlineData("example.com")]
    [InlineData("www.example.com")]
    [InlineData("/relative/path")]
    [InlineData("not a uri at all")]
    public void NonAbsoluteHref_ReturnsFalse(string href)
    {
        Assert.False(LinkService.TryCreateLaunchUri(href, out _));
    }
}
