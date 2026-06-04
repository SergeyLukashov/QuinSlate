using System.Collections.Generic;
using System.IO;

namespace Jott.AssetGenerator.Imaging;

/// <summary>
/// Writes a multi-resolution Windows <c>.ico</c> file. Small frames are stored
/// as uncompressed 32-bit DIBs (the format the shell loads for tray and taskbar
/// sizes); the largest frame is stored as a PNG.
/// </summary>
public static class IconFileWriter
{
    private const int BytesPerPixel = 4;
    private const int DibHeaderSize = 40;
    private const int IconDirSize = 6;
    private const int IconEntrySize = 16;
    private const short IconType = 1;
    private const short Planes = 1;
    private const short BitCount = 32;
    private const int LargeIconThreshold = 256;
    private const int AndMaskAlignmentBits = 32;
    private const int AndMaskAlignmentBytes = 4;

    /// <summary>
    /// Writes an icon file containing the given DIB frames plus one PNG frame.
    /// </summary>
    /// <param name="path">Destination <c>.ico</c> path.</param>
    /// <param name="bmpFrames">Uncompressed frames (typically 16-64 px).</param>
    /// <param name="pngFrame">PNG-encoded bytes of the large frame.</param>
    /// <param name="pngFrameSize">Edge length of the PNG frame in pixels.</param>
    public static void Write(string path, IReadOnlyList<IconBitmapFrame> bmpFrames, byte[] pngFrame, int pngFrameSize)
    {
        var frames = new List<(int Size, byte[] Data)>();
        foreach (var frame in bmpFrames)
        {
            frames.Add((frame.Size, BuildDib(frame)));
        }

        frames.Add((pngFrameSize, pngFrame));

        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);

        writer.Write((short)0);
        writer.Write(IconType);
        writer.Write((short)frames.Count);

        int offset = IconDirSize + IconEntrySize * frames.Count;
        foreach (var frame in frames)
        {
            byte dimension = frame.Size >= LargeIconThreshold ? (byte)0 : (byte)frame.Size;
            writer.Write(dimension);
            writer.Write(dimension);
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write(Planes);
            writer.Write(BitCount);
            writer.Write(frame.Data.Length);
            writer.Write(offset);
            offset += frame.Data.Length;
        }

        foreach (var frame in frames)
        {
            writer.Write(frame.Data);
        }
    }

    private static byte[] BuildDib(IconBitmapFrame frame)
    {
        int size = frame.Size;
        using var memory = new MemoryStream();
        using var writer = new BinaryWriter(memory);

        writer.Write(DibHeaderSize);
        writer.Write(size);
        writer.Write(size * 2);
        writer.Write(Planes);
        writer.Write(BitCount);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);

        int stride = size * BytesPerPixel;
        for (int row = size - 1; row >= 0; row--)
        {
            writer.Write(frame.StraightBgra, row * stride, stride);
        }

        int andRowBytes = (size + AndMaskAlignmentBits - 1) / AndMaskAlignmentBits * AndMaskAlignmentBytes;
        byte[] emptyMaskRow = new byte[andRowBytes];
        for (int row = 0; row < size; row++)
        {
            writer.Write(emptyMaskRow);
        }

        writer.Flush();
        return memory.ToArray();
    }
}
