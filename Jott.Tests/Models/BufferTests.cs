using Buffer = Jott.Ui.Models.Buffer;

namespace Jott.Tests.Models;

public sealed class BufferTests
{
    [Fact]
    public void Constructor_SetsPropertiesCorrectly()
    {
        var index = 3;
        var colorHex = "#FFF176";
        var filePath = @"C:\dummy\path.txt";
        var content = "test content";

        var buffer = new Buffer(index, colorHex, filePath, content);

        Assert.Equal(index, buffer.Index);
        Assert.Equal(colorHex, buffer.ColorHex);
        Assert.Equal(filePath, buffer.FilePath);
        Assert.Equal(content, buffer.Content);
    }

    [Fact]
    public void Constructor_NullContent_SetsEmptyString()
    {
        var buffer = new Buffer(1, "#E57373", "path.txt", null);

        Assert.Equal(string.Empty, buffer.Content);
    }

    [Fact]
    public void MaxIndex_IsSeven()
    {
        Assert.Equal(7, Buffer.MaxIndex);
    }

    [Fact]
    public void MinIndex_IsOne()
    {
        Assert.Equal(1, Buffer.MinIndex);
    }

    [Fact]
    public void Colors_HasSevenElements()
    {
        Assert.Equal(7, Buffer.Colors.Length);
    }
}
