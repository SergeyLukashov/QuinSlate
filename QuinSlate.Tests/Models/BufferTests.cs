using Buffer = QuinSlate.Ui.Models.Buffer;

namespace QuinSlate.Tests.Models;

public sealed class BufferTests
{
    [Fact]
    public void Constructor_SetsPropertiesCorrectly()
    {
        var index = 3;
        var filePath = @"C:\dummy\path.txt";
        var content = "test content";

        var buffer = new Buffer(index, filePath, content);

        Assert.Equal(index, buffer.Index);
        Assert.Equal(filePath, buffer.FilePath);
        Assert.Equal(content, buffer.Content);
    }

    [Fact]
    public void Constructor_NullContent_SetsEmptyString()
    {
        var buffer = new Buffer(1, "path.txt", null);

        Assert.Equal(string.Empty, buffer.Content);
    }

    [Fact]
    public void MaxIndex_IsFive()
    {
        Assert.Equal(5, Buffer.MaxIndex);
    }

    [Fact]
    public void MinIndex_IsOne()
    {
        Assert.Equal(1, Buffer.MinIndex);
    }
}
