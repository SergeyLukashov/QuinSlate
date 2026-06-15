using QuinSlate.Ui.Constants;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Buffer = QuinSlate.Ui.Models.Buffer;

namespace QuinSlate.Ui.Services;

/// <summary>
/// Owns the five persistent buffers: file IO, in-memory state, and the
/// debounced autosave timer.
/// </summary>
/// <remarks>
/// Buffer files use UTF-8 with BOM. Writes are debounced 300 ms after the last
/// keystroke to avoid IO churn. <see cref="FlushPendingWritesSync"/> must be
/// called on shutdown so unflushed edits are not lost.
/// </remarks>
public sealed class BufferService
{
    private const int DebounceMilliseconds = 300;
    private const string BufferFileNameFormat = "buffer-{0}.txt";

    private static readonly UTF8Encoding Utf8WithBom = new UTF8Encoding(true);

    private readonly string appDataDirectory;
    private readonly Dictionary<int, Buffer> buffersByIndex = new Dictionary<int, Buffer>();
    private readonly Dictionary<int, Timer> debounceTimers = new Dictionary<int, Timer>();
    private readonly object syncRoot = new object();

    /// <summary>
    /// Creates the service rooted at <paramref name="appDataDirectory"/>
    /// (typically <c>%AppData%\QuinSlate\</c>). The directory is created on first
    /// load if it does not already exist.
    /// </summary>
    public BufferService(string appDataDirectory)
    {
        if (appDataDirectory == null)
        {
            throw new ArgumentNullException(nameof(appDataDirectory));
        }

        this.appDataDirectory = appDataDirectory;
    }

    /// <summary>
    /// The absolute directory the service reads and writes buffer files in
    /// (typically <c>%AppData%\QuinSlate\</c>). Exposed so the UI can display
    /// the real, live storage location rather than recomputing it.
    /// </summary>
    public string AppDataDirectory => appDataDirectory;

    /// <summary>
    /// Loads all five buffers from disk. Missing files become empty buffers
    /// (this is not an error). Read errors are swallowed and the buffer is
    /// initialised to empty so the UI is always usable.
    /// </summary>
    public IReadOnlyList<Buffer> LoadAll()
    {
        if (Directory.Exists(appDataDirectory) == false)
        {
            Directory.CreateDirectory(appDataDirectory);
        }

        var ordered = new List<Buffer>(Buffer.MaxIndex);
        for (var index = Buffer.MinIndex; index <= Buffer.MaxIndex; index++)
        {
            var path = GetBufferFilePath(index);
            string content;
            if (File.Exists(path) == false)
            {
                content = DefaultBuffers.GetDefaultContent(index);
            }
            else
            {
                content = ReadFileSafe(path);
            }
            var buffer = new Buffer(index, path, content);
            buffersByIndex[index] = buffer;
            ordered.Add(buffer);
        }

        return ordered;
    }

    /// <summary>
    /// Returns the buffer at the given 1-based <paramref name="index"/>, or
    /// <c>null</c> if <see cref="LoadAll"/> has not been called or the index
    /// is out of range.
    /// </summary>
    public Buffer GetBuffer(int index)
    {
        Buffer buffer;
        if (buffersByIndex.TryGetValue(index, out buffer))
        {
            return buffer;
        }

        return null;
    }

    /// <summary>
    /// Records a content update for the buffer at <paramref name="index"/>
    /// and (re)arms the 300 ms debounce timer that writes it to disk.
    /// </summary>
    public void UpdateContent(int index, string newContent)
    {
        Buffer buffer;
        if (buffersByIndex.TryGetValue(index, out buffer) == false)
        {
            return;
        }

        buffer.Content = newContent ?? string.Empty;

        lock (syncRoot)
        {
            Timer existing;
            if (debounceTimers.TryGetValue(index, out existing))
            {
                existing.Change(DebounceMilliseconds, Timeout.Infinite);
                return;
            }

            var timer = new Timer(state => OnDebounceElapsed((int)state), index, DebounceMilliseconds, Timeout.Infinite);
            debounceTimers[index] = timer;
        }
    }

    /// <summary>
    /// Synchronously writes all loaded buffer content to disk and disposes any
    /// outstanding debounce timers. Call this on application exit.
    /// </summary>
    /// <remarks>
    /// Writing all buffers (not just those with active timers) covers the race
    /// where a debounce timer has already fired and removed itself from the
    /// dictionary but the resulting async write has not yet completed.
    /// </remarks>
    public void FlushPendingWritesSync()
    {
        List<Timer> timersToDispose;

        lock (syncRoot)
        {
            timersToDispose = new List<Timer>(debounceTimers.Values);
            debounceTimers.Clear();
        }

        foreach (var timer in timersToDispose)
        {
            timer.Dispose();
        }

        foreach (var buffer in buffersByIndex.Values)
        {
            WriteFileSafe(buffer.FilePath, buffer.Content);
        }
    }

    private void OnDebounceElapsed(int index)
    {
        Buffer buffer;
        if (buffersByIndex.TryGetValue(index, out buffer) == false)
        {
            return;
        }

        lock (syncRoot)
        {
            Timer timer;
            if (debounceTimers.TryGetValue(index, out timer))
            {
                debounceTimers.Remove(index);
                timer.Dispose();
            }
        }

        _ = WriteFileAsync(buffer.FilePath, buffer.Content);
    }

    private string GetBufferFilePath(int index)
    {
        var fileName = string.Format(BufferFileNameFormat, index);
        return Path.Combine(appDataDirectory, fileName);
    }

    private static string ReadFileSafe(string path)
    {
        try
        {
            return File.ReadAllText(path, Utf8WithBom);
        }
        catch (IOException ex)
        {
            Log.ForContext<BufferService>().Warning(ex, "Failed to read buffer file {Path}; treating as empty.", path);
            return string.Empty;
        }
        catch (UnauthorizedAccessException ex)
        {
            Log.ForContext<BufferService>().Warning(ex, "Access denied reading buffer file {Path}; treating as empty.", path);
            return string.Empty;
        }
    }

    private static async Task WriteFileAsync(string path, string content)
    {
        try
        {
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
            using (var writer = new StreamWriter(stream, Utf8WithBom))
            {
                await writer.WriteAsync(content ?? string.Empty);
            }
        }
        catch (IOException ex)
        {
            Log.ForContext<BufferService>().Warning(ex, "Failed to write buffer file {Path}.", path);
        }
        catch (UnauthorizedAccessException ex)
        {
            Log.ForContext<BufferService>().Warning(ex, "Access denied writing buffer file {Path}.", path);
        }
    }

    private static void WriteFileSafe(string path, string content)
    {
        try
        {
            File.WriteAllText(path, content ?? string.Empty, Utf8WithBom);
        }
        catch (IOException ex)
        {
            Log.ForContext<BufferService>().Warning(ex, "Failed to write buffer file {Path}.", path);
        }
        catch (UnauthorizedAccessException ex)
        {
            Log.ForContext<BufferService>().Warning(ex, "Access denied writing buffer file {Path}.", path);
        }
    }
}
