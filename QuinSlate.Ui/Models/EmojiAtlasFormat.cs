using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace QuinSlate.Ui.Models;

/// <summary>
/// The contract between the build-time emoji atlas generator
/// (QuinSlate.AssetGenerator emoji-atlas) and the runtime sprite pipeline:
/// sprite grid geometry, the rendered font size, asset file names, and the
/// content hash that ties a generated atlas to the exact ordered emoji list
/// it was rendered from. Sprite index i lives at grid position
/// (i % <see cref="Columns"/>, i / <see cref="Columns"/>) in
/// <see cref="EmojiData.GetAllEntries"/> order.
/// </summary>
public static class EmojiAtlasFormat
{
    /// <summary>Number of sprite columns in the atlas grid.</summary>
    public const int Columns = 25;

    /// <summary>
    /// Side length of one sprite cell in logical pixels (DIPs). Comfortably
    /// contains a <see cref="GlyphFontSize"/> emoji glyph and fits inside the
    /// picker's 36px item area.
    /// </summary>
    public const double SpriteLogicalSize = 32;

    /// <summary>
    /// The scale the atlas is rendered at. 4 covers every standard Windows
    /// display scale (100–400%); at lower scales the atlas is downscaled at
    /// decode time, which is visually lossless for dense colour emoji art.
    /// </summary>
    public const int AtlasScale = 4;

    /// <summary>Side length of one sprite cell in atlas pixels.</summary>
    public const int SpritePixelSize = (int)SpriteLogicalSize * AtlasScale;

    /// <summary>
    /// Emoji glyph font size in logical pixels (the size the picker's
    /// TextBlock glyphs historically used). The generator renders glyphs at
    /// this size × <see cref="AtlasScale"/>.
    /// </summary>
    public const double GlyphFontSize = 19.8;

    /// <summary>Atlas image file name under the app's Assets folder.</summary>
    public const string AtlasFileName = "EmojiAtlas.png";

    /// <summary>Atlas metadata file name, written next to the atlas image.</summary>
    public const string MetadataFileName = "EmojiAtlas.json";

    /// <summary>Number of grid rows needed for <paramref name="spriteCount"/> sprites.</summary>
    public static int RowsFor(int spriteCount)
    {
        return (spriteCount + Columns - 1) / Columns;
    }

    /// <summary>
    /// Hash of the ordered emoji list an atlas was generated from. Stored in
    /// the atlas metadata and compared against the current
    /// <see cref="EmojiData"/> content by a unit test, so an atlas that has
    /// gone stale after an emoji data change fails the build's test run
    /// instead of silently showing wrong sprites.
    /// </summary>
    public static string ComputeContentHash(IEnumerable<EmojiEntry> entries)
    {
        var joined = new StringBuilder();
        foreach (EmojiEntry entry in entries)
        {
            joined.Append(entry.Emoji);
            joined.Append('\n');
        }

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(joined.ToString()));
        return System.Convert.ToHexStringLower(hash);
    }
}
