using QuinSlate.Ui.Models;
using System.Buffers.Binary;
using System.Text.Json;

namespace QuinSlate.Tests.Models;

/// <summary>
/// Verifies the checked-in emoji sprite atlas (QuinSlate.Ui/Assets) still
/// matches the current <see cref="EmojiData"/> content and the
/// <see cref="EmojiAtlasFormat"/> geometry. A failure here means the emoji
/// data changed after the atlas was generated: regenerate it with
/// `dotnet run --project QuinSlate.AssetGenerator -p:Platform=x64 -- emoji-atlas`
/// and copy the outputs into QuinSlate.Ui/Assets.
/// </summary>
public sealed class EmojiAtlasConsistencyTests
{
    private const int PngHeaderWidthOffset = 16;
    private const int PngHeaderHeightOffset = 20;
    private const int PngHeaderMinLength = 24;

    private static string AssetPath(string fileName)
    {
        return Path.Combine(AppContext.BaseDirectory, "Assets", fileName);
    }

    private static JsonElement ReadMetadata()
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(AssetPath(EmojiAtlasFormat.MetadataFileName)));
        return document.RootElement.Clone();
    }

    [Fact]
    public void Metadata_ContentHash_MatchesCurrentEmojiData()
    {
        JsonElement metadata = ReadMetadata();

        string expectedHash = EmojiAtlasFormat.ComputeContentHash(EmojiData.GetAllEntries());

        Assert.Equal(expectedHash, metadata.GetProperty("contentHash").GetString());
    }

    [Fact]
    public void Metadata_Geometry_MatchesAtlasFormatConstants()
    {
        JsonElement metadata = ReadMetadata();

        Assert.Equal(EmojiData.GetAllEntries().Count, metadata.GetProperty("spriteCount").GetInt32());
        Assert.Equal(EmojiAtlasFormat.Columns, metadata.GetProperty("columns").GetInt32());
        Assert.Equal(EmojiAtlasFormat.SpritePixelSize, metadata.GetProperty("spritePixelSize").GetInt32());
        Assert.Equal(EmojiAtlasFormat.AtlasScale, metadata.GetProperty("atlasScale").GetInt32());
        Assert.Equal(EmojiAtlasFormat.GlyphFontSize, metadata.GetProperty("glyphFontSize").GetDouble());
    }

    [Fact]
    public void AtlasImage_PixelDimensions_MatchEmojiDataGrid()
    {
        byte[] header = new byte[PngHeaderMinLength];
        using (FileStream stream = File.OpenRead(AssetPath(EmojiAtlasFormat.AtlasFileName)))
        {
            stream.ReadExactly(header);
        }

        int width = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(PngHeaderWidthOffset));
        int height = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(PngHeaderHeightOffset));

        int rows = EmojiAtlasFormat.RowsFor(EmojiData.GetAllEntries().Count);
        Assert.Equal(EmojiAtlasFormat.Columns * EmojiAtlasFormat.SpritePixelSize, width);
        Assert.Equal(rows * EmojiAtlasFormat.SpritePixelSize, height);
    }
}
