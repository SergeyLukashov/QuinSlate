using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using QuinSlate.Ui.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.UI;

namespace QuinSlate.AssetGenerator.Emoji;

/// <summary>
/// Renders every picker emoji from <see cref="EmojiData"/> into a single
/// sprite atlas PNG at <see cref="EmojiAtlasFormat.AtlasScale"/>× resolution,
/// plus a metadata JSON carrying the content hash that ties the atlas to the
/// exact ordered emoji list it was rendered from. The app decodes this atlas
/// at the display's rasterization scale instead of paying first-time
/// colour-glyph font rasterization at runtime.
///
/// Rendering goes through Win2D (Direct2D/DWriteCore) rather than Skia so the
/// sprites match how XAML and RichEdit render Segoe UI Emoji on Windows 11:
/// DWrite draws the modern COLRv1 gradient glyphs, while Skia falls back to
/// the flat COLRv0 layers, whose solid fills look visibly more saturated.
/// </summary>
internal static class EmojiAtlasGenerator
{
    private const string EmojiFontFamily = "Segoe UI Emoji";

    /// <summary>
    /// Generates the atlas image and metadata into
    /// <paramref name="outputDirectory"/>. Returns true when every emoji
    /// rendered successfully; failures are listed on stderr.
    /// </summary>
    internal static async Task<bool> RunAsync(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);

        IReadOnlyList<EmojiEntry> entries = EmojiData.GetAllEntries();
        int rows = EmojiAtlasFormat.RowsFor(entries.Count);
        int atlasWidth = EmojiAtlasFormat.Columns * EmojiAtlasFormat.SpritePixelSize;
        int atlasHeight = rows * EmojiAtlasFormat.SpritePixelSize;
        float fontSize = (float)(EmojiAtlasFormat.GlyphFontSize * EmojiAtlasFormat.AtlasScale);

        // Software rendering keeps the output identical across build machines
        // regardless of GPU; DWriteCore rasterizes the glyphs either way.
        using var device = new CanvasDevice(forceSoftwareRenderer: true);

        // 96 DPI so one canvas DIP is exactly one atlas pixel.
        const float PixelPerfectDpi = 96;
        using var renderTarget = new CanvasRenderTarget(device, atlasWidth, atlasHeight, PixelPerfectDpi);

        using var textFormat = new CanvasTextFormat
        {
            FontFamily = EmojiFontFamily,
            FontSize = fontSize,
            HorizontalAlignment = CanvasHorizontalAlignment.Left,
            VerticalAlignment = CanvasVerticalAlignment.Top,
        };

        var failures = new List<string>();
        using (CanvasDrawingSession session = renderTarget.CreateDrawingSession())
        {
            // ClearType needs an opaque backdrop; on the transparent atlas the
            // monochrome parts of glyphs must use grayscale antialiasing.
            session.TextAntialiasing = CanvasTextAntialiasing.Grayscale;
            session.Clear(Color.FromArgb(0, 0, 0, 0));

            for (int i = 0; i < entries.Count; i++)
            {
                string failure = DrawSprite(session, device, textFormat, entries[i].Emoji, i);
                if (failure != null)
                {
                    failures.Add($"  [{i}] {failure}");
                }
            }
        }

        if (failures.Count > 0)
        {
            Console.Error.WriteLine($"Error: {failures.Count} emoji failed to render:");
            failures.ForEach(Console.Error.WriteLine);
            return false;
        }

        string atlasPath = Path.Combine(outputDirectory, EmojiAtlasFormat.AtlasFileName);
        File.Delete(atlasPath);
        await renderTarget.SaveAsync(atlasPath, CanvasBitmapFileFormat.Png);

        string metadataPath = Path.Combine(outputDirectory, EmojiAtlasFormat.MetadataFileName);
        WriteMetadata(metadataPath, entries);

        Console.WriteLine($"  {EmojiAtlasFormat.AtlasFileName} ({atlasWidth}x{atlasHeight}, {entries.Count} sprites, {EmojiAtlasFormat.Columns} columns)");
        Console.WriteLine($"  {EmojiAtlasFormat.MetadataFileName}");
        return true;
    }

    /// <summary>
    /// Draws one emoji centred by its ink bounds inside its grid cell,
    /// mirroring how the picker's TextBlock glyphs centred visually inside
    /// their cells. DWrite shapes the text (fallback fonts, variation
    /// selectors, and ZWJ sequences all behave exactly like a TextBlock).
    /// Returns null on success, or a failure description when the emoji has
    /// no ink or overflows its cell.
    /// </summary>
    private static string DrawSprite(CanvasDrawingSession session, CanvasDevice device, CanvasTextFormat textFormat, string emoji, int index)
    {
        int cellPx = EmojiAtlasFormat.SpritePixelSize;

        using var layout = new CanvasTextLayout(device, emoji, textFormat, cellPx * 2, cellPx * 2)
        {
            Options = CanvasDrawTextOptions.EnableColorFont,
        };

        Rect ink = layout.DrawBounds;
        if (ink.Width <= 0 || ink.Height <= 0)
        {
            return $"'{emoji}' rendered no ink (missing glyph?).";
        }

        if (ink.Width > cellPx || ink.Height > cellPx)
        {
            return $"'{emoji}' ink {ink.Width:F0}x{ink.Height:F0} px overflows the {cellPx} px cell.";
        }

        double cellLeft = index % EmojiAtlasFormat.Columns * cellPx;
        double cellTop = index / EmojiAtlasFormat.Columns * cellPx;
        float x = (float)(cellLeft + (cellPx - ink.Width) / 2 - ink.X);
        float y = (float)(cellTop + (cellPx - ink.Height) / 2 - ink.Y);

        session.DrawTextLayout(layout, x, y, Color.FromArgb(255, 0, 0, 0));
        return null;
    }

    private static void WriteMetadata(string path, IReadOnlyList<EmojiEntry> entries)
    {
        var metadata = new Dictionary<string, object>
        {
            ["spriteCount"] = entries.Count,
            ["columns"] = EmojiAtlasFormat.Columns,
            ["spritePixelSize"] = EmojiAtlasFormat.SpritePixelSize,
            ["atlasScale"] = EmojiAtlasFormat.AtlasScale,
            ["glyphFontSize"] = EmojiAtlasFormat.GlyphFontSize,
            ["contentHash"] = EmojiAtlasFormat.ComputeContentHash(entries),
            ["generatedUtc"] = DateTime.UtcNow.ToString("O"),
        };

        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(path, JsonSerializer.Serialize(metadata, options));
    }
}
