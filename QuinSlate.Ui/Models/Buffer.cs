namespace QuinSlate.Ui.Models;

/// <summary>
/// In-memory representation of one of the five persistent text buffers.
/// </summary>
public sealed class Buffer
{
    /// <summary>
    /// 1-based buffer index, between <see cref="MinIndex"/> and <see cref="MaxIndex"/>.
    /// </summary>
    public int Index { get; }

    /// <summary>
    /// Absolute path to the buffer's <c>.txt</c> file under <c>%AppData%\QuinSlate\</c>.
    /// </summary>
    public string FilePath { get; }

    // volatile so reads on the thread-pool debounce timer always see the value
    // last written on the UI thread.
    private volatile string content;

    /// <summary>
    /// Current text content held in memory. Mutated by the UI; persisted by
    /// <c>BufferService</c> on its debounce timer.
    /// </summary>
    public string Content
    {
        get { return content; }
        set { content = value; }
    }

    /// <summary>The lowest valid <see cref="Index"/>.</summary>
    public const int MinIndex = 1;

    /// <summary>The highest valid <see cref="Index"/>.</summary>
    public const int MaxIndex = 5;

    /// <summary>
    /// Creates a buffer for the given index. <paramref name="content"/> may be
    /// the empty string for a fresh / missing-file buffer.
    /// </summary>
    public Buffer(int index, string filePath, string content)
    {
        Index = index;
        FilePath = filePath;
        this.content = content ?? string.Empty;
    }
}
