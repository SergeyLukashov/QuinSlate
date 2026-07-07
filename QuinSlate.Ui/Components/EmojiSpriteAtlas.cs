using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using QuinSlate.Ui.Models;
using Serilog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;

namespace QuinSlate.Ui.Components;

/// <summary>
/// The picker's pre-rasterized emoji sprites: decodes the build-time atlas
/// (see <see cref="EmojiAtlasFormat"/>) at the display's exact rasterization
/// scale and slices it into one <see cref="WriteableBitmap"/> per emoji, so
/// showing an emoji is a cached-bitmap draw instead of first-time colour-glyph
/// font rasterization (which cost seconds of render-thread work per session
/// on slow hardware). Decoding happens once per process per scale — a few
/// hundred milliseconds of mostly off-thread work — and is re-run when the
/// scale changes. All members must be called on the UI thread.
/// </summary>
internal sealed class EmojiSpriteAtlas
{
    private const int BytesPerPixel = 4;
    private const double ScaleEpsilon = 0.001;
    private const string AssetsFolderName = "Assets";

    private readonly Dictionary<string, int> indexByEmoji;
    private readonly int spriteCount;

    private ImageSource[] sprites;
    private double loadedScale;
    private bool isLoading;
    private double lastRequestedScale;

    /// <summary>Raised on the UI thread when the sprites (re)become available.</summary>
    internal event EventHandler SpritesReady;

    /// <summary>Gets whether the sprites are available.</summary>
    internal bool IsLoaded => sprites != null;

    internal EmojiSpriteAtlas()
    {
        IReadOnlyList<EmojiEntry> entries = EmojiData.GetAllEntries();
        spriteCount = entries.Count;
        indexByEmoji = new Dictionary<string, int>(entries.Count, StringComparer.Ordinal);
        for (int i = 0; i < entries.Count; i++)
        {
            indexByEmoji[entries[i].Emoji] = i;
        }
    }

    /// <summary>
    /// The sprite index of <paramref name="emoji"/> in
    /// <see cref="EmojiData.GetAllEntries"/> order, or -1 when unknown.
    /// </summary>
    internal int IndexOf(string emoji)
    {
        return emoji != null && indexByEmoji.TryGetValue(emoji, out int index) ? index : -1;
    }

    /// <summary>The sprite for <paramref name="index"/>, or null while not loaded.</summary>
    internal ImageSource GetSprite(int index)
    {
        return sprites != null && index >= 0 && index < sprites.Length ? sprites[index] : null;
    }

    /// <summary>
    /// Starts decoding the atlas for <paramref name="rasterizationScale"/>
    /// unless sprites at that scale are already available or being decoded.
    /// Fire-and-forget: completion is signalled via <see cref="SpritesReady"/>
    /// and failures are logged, leaving any previously loaded sprites in use.
    /// </summary>
    internal void EnsureLoaded(double rasterizationScale)
    {
        if (rasterizationScale <= 0)
        {
            return;
        }

        lastRequestedScale = rasterizationScale;
        if (isLoading || (sprites != null && Math.Abs(loadedScale - rasterizationScale) < ScaleEpsilon))
        {
            return;
        }

        isLoading = true;
        _ = LoadAsync(rasterizationScale);
    }

    private async Task LoadAsync(double rasterizationScale)
    {
        try
        {
            await DecodeAndSliceAsync(rasterizationScale);
        }
        catch (Exception ex)
        {
            Log.ForContext<EmojiSpriteAtlas>().Error(
                ex, "Emoji sprite atlas failed to load at scale {Scale}.", rasterizationScale);
        }
        finally
        {
            isLoading = false;
        }

        if (sprites != null && Math.Abs(loadedScale - lastRequestedScale) >= ScaleEpsilon)
        {
            EnsureLoaded(lastRequestedScale);
        }
    }

    private async Task DecodeAndSliceAsync(double rasterizationScale)
    {
        var stopwatch = Stopwatch.StartNew();

        string atlasPath = Path.Combine(AppContext.BaseDirectory, AssetsFolderName, EmojiAtlasFormat.AtlasFileName);
        int rows = EmojiAtlasFormat.RowsFor(spriteCount);

        byte[] atlasPixels;
        int cellPx;
        using (FileStream fileStream = File.OpenRead(atlasPath))
        using (var randomAccessStream = fileStream.AsRandomAccessStream())
        {
            BitmapDecoder decoder = await BitmapDecoder.CreateAsync(randomAccessStream);

            uint expectedWidth = (uint)(EmojiAtlasFormat.Columns * EmojiAtlasFormat.SpritePixelSize);
            uint expectedHeight = (uint)(rows * EmojiAtlasFormat.SpritePixelSize);
            if (decoder.PixelWidth != expectedWidth || decoder.PixelHeight != expectedHeight)
            {
                Log.ForContext<EmojiSpriteAtlas>().Error(
                    "Emoji sprite atlas is {ActualWidth}x{ActualHeight} px but {ExpectedWidth}x{ExpectedHeight} px was expected; regenerate it (AssetGenerator emoji-atlas).",
                    decoder.PixelWidth,
                    decoder.PixelHeight,
                    expectedWidth,
                    expectedHeight);
                return;
            }

            cellPx = Math.Max(1, (int)Math.Round(EmojiAtlasFormat.SpriteLogicalSize * rasterizationScale));
            var transform = new BitmapTransform
            {
                ScaledWidth = (uint)(cellPx * EmojiAtlasFormat.Columns),
                ScaledHeight = (uint)(cellPx * rows),
                InterpolationMode = BitmapInterpolationMode.Fant,
            };

            PixelDataProvider pixelData = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                transform,
                ExifOrientationMode.IgnoreExifOrientation,
                ColorManagementMode.DoNotColorManage);
            atlasPixels = pixelData.DetachPixelData();
        }

        int atlasRowBytes = cellPx * EmojiAtlasFormat.Columns * BytesPerPixel;
        int spriteRowBytes = cellPx * BytesPerPixel;
        var spriteBuffer = new byte[cellPx * spriteRowBytes];
        var loaded = new ImageSource[spriteCount];

        for (int i = 0; i < spriteCount; i++)
        {
            int sourceLeft = i % EmojiAtlasFormat.Columns * spriteRowBytes;
            int sourceTop = i / EmojiAtlasFormat.Columns * cellPx;

            for (int row = 0; row < cellPx; row++)
            {
                System.Buffer.BlockCopy(
                    atlasPixels,
                    (sourceTop + row) * atlasRowBytes + sourceLeft,
                    spriteBuffer,
                    row * spriteRowBytes,
                    spriteRowBytes);
            }

            var bitmap = new WriteableBitmap(cellPx, cellPx);
            using (Stream pixelStream = bitmap.PixelBuffer.AsStream())
            {
                pixelStream.Write(spriteBuffer, 0, spriteBuffer.Length);
            }

            bitmap.Invalidate();
            loaded[i] = bitmap;
        }

        sprites = loaded;
        loadedScale = rasterizationScale;

        Log.ForContext<EmojiSpriteAtlas>().Information(
            "Emoji sprite atlas loaded in {ElapsedMs:F1} ms: {SpriteCount} sprites at {CellPx} px (scale {Scale:F2}).",
            stopwatch.Elapsed.TotalMilliseconds,
            spriteCount,
            cellPx,
            rasterizationScale);

        SpritesReady?.Invoke(this, EventArgs.Empty);
    }
}
