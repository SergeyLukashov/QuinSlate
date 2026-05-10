namespace Jott.Ui.Models;

/// <summary>
/// In-memory representation of one of the seven persistent text buffers.
/// </summary>
public sealed class Buffer
{
    /// <summary>
    /// 1-based buffer index, between <see cref="MinIndex"/> and <see cref="MaxIndex"/>.
    /// </summary>
    public int Index { get; }

    /// <summary>
    /// Hex colour string (for example <c>#E57373</c>) used to tint the buffer's tab.
    /// </summary>
    public string ColorHex { get; }

    /// <summary>
    /// Absolute path to the buffer's <c>.txt</c> file under <c>%AppData%\Jott\</c>.
    /// </summary>
    public string FilePath { get; }

    /// <summary>
    /// Current text content held in memory. Mutated by the UI; persisted by
    /// <c>BufferService</c> on its debounce timer.
    /// </summary>
    public string Content { get; set; }

    /// <summary>The lowest valid <see cref="Index"/>.</summary>
    public const int MinIndex = 1;

    /// <summary>The highest valid <see cref="Index"/>.</summary>
    public const int MaxIndex = 7;

    /// <summary>
    /// Hex colour strings used to tint each buffer tab, ordered by 1-based index.
    /// </summary>
    public static readonly string[] Colors = new[]
    {
        "#E57373",
        "#FFB74D",
        "#FFF176",
        "#81C784",
        "#64B5F6",
        "#CE93D8",
        "#90A4AE",
    };

    /// <summary>
    /// Creates a buffer for the given index. <paramref name="content"/> may be
    /// the empty string for a fresh / missing-file buffer.
    /// </summary>
    public Buffer(int index, string colorHex, string filePath, string content)
    {
        Index = index;
        ColorHex = colorHex;
        FilePath = filePath;
        Content = content ?? string.Empty;
    }
}
