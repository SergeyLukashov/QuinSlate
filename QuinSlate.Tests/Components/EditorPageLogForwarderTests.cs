using QuinSlate.Ui.Components;
using Serilog.Events;

namespace QuinSlate.Tests.Components;

public sealed class EditorPageLogForwarderTests
{
    [Theory]
    [InlineData("debug", LogEventLevel.Debug)]
    [InlineData("information", LogEventLevel.Information)]
    [InlineData("warning", LogEventLevel.Warning)]
    [InlineData("error", LogEventLevel.Error)]
    public void MapLevel_KnownLevel_MapsToSerilogLevel(string level, LogEventLevel expected)
    {
        Assert.Equal(expected, EditorPageLogForwarder.MapLevel(level));
    }

    [Theory]
    [InlineData("")]
    [InlineData("verbose")]
    [InlineData("ERROR")]
    public void MapLevel_UnknownLevel_FallsBackToInformation(string level)
    {
        Assert.Equal(LogEventLevel.Information, EditorPageLogForwarder.MapLevel(level));
    }

    [Fact]
    public void Clamp_Null_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, EditorPageLogForwarder.Clamp(null));
    }

    [Fact]
    public void Clamp_ShortText_IsUnchanged()
    {
        Assert.Equal("a page message", EditorPageLogForwarder.Clamp("a page message"));
    }

    [Fact]
    public void Clamp_OverlongText_IsBounded()
    {
        string clamped = EditorPageLogForwarder.Clamp(new string('x', 10000));
        Assert.Equal(4096, clamped.Length);
    }
}
