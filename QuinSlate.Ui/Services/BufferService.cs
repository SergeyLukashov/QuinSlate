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

    /// <summary>
    /// Upper bound on how long the shutdown flush waits for debounce-fired async
    /// writes still in flight. Generous for local file IO while still keeping a
    /// wedged write from hanging process exit indefinitely.
    /// </summary>
    private const int FlushWaitTimeoutMilliseconds = 2000;

    private static readonly UTF8Encoding Utf8WithBom = new UTF8Encoding(true);

    private readonly string appDataDirectory;
    private readonly Dictionary<int, Buffer> buffersByIndex = new Dictionary<int, Buffer>();
    private readonly Dictionary<int, Timer> debounceTimers = new Dictionary<int, Timer>();
    private readonly Dictionary<int, Task> inFlightWrites = new Dictionary<int, Task>();

    // Per-buffer dirty tracking: a buffer is dirty when the version stamped on its
    // last content update is newer than the version last written to disk. The
    // shutdown flush uses this to skip buffers already persisted (or never edited),
    // rather than rewriting all five every exit. All access is under syncRoot.
    private readonly Dictionary<int, long> contentVersionByIndex = new Dictionary<int, long>();
    private readonly Dictionary<int, long> persistedVersionByIndex = new Dictionary<int, long>();
    private long versionCounter;

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
            contentVersionByIndex[index] = ++versionCounter;

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
    /// First waits (bounded) for debounce-fired async writes still in flight, so
    /// the final synchronous pass never collides with an open write handle and
    /// loses the freshest content to a sharing violation. Only buffers still dirty
    /// after that wait are written — those already persisted by the async path (or
    /// never edited this session) are skipped — while a dirty buffer whose write was
    /// never scheduled is still covered.
    /// </remarks>
    public void FlushPendingWritesSync()
    {
        List<Timer> timersToDispose;
        List<Task> writesInFlight;

        lock (syncRoot)
        {
            timersToDispose = new List<Timer>(debounceTimers.Values);
            debounceTimers.Clear();
            writesInFlight = new List<Task>(inFlightWrites.Values);
            inFlightWrites.Clear();
        }

        foreach (var timer in timersToDispose)
        {
            timer.Dispose();
        }

        try
        {
            Task.WaitAll(writesInFlight.ToArray(), FlushWaitTimeoutMilliseconds);
        }
        catch (AggregateException ex)
        {
            Log.ForContext<BufferService>().Warning(ex, "An in-flight buffer write faulted while flushing; the synchronous pass below rewrites every dirty buffer.");
        }

        foreach (var buffer in buffersByIndex.Values)
        {
            long version;
            lock (syncRoot)
            {
                if (IsDirty(buffer.Index, out version) == false)
                {
                    continue;
                }
            }

            if (WriteFileSafe(buffer.FilePath, buffer.Content))
            {
                MarkPersisted(buffer.Index, version);
            }
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

            long version = contentVersionByIndex.TryGetValue(index, out long v) ? v : 0;

            Task previousWrite;
            inFlightWrites.TryGetValue(index, out previousWrite);
            inFlightWrites[index] = WriteFileChainedAsync(previousWrite, index, buffer.FilePath, buffer.Content, version);
        }
    }

    /// <summary>
    /// Runs a buffer write after the previous write for the same file has
    /// finished, so two debounce firings can never write the same path
    /// concurrently (the atomic writer's exclusive temp file would make the
    /// newer write fail and its content would be lost until the next edit). On a
    /// successful write the buffer's persisted version advances so the shutdown
    /// flush can skip it.
    /// </summary>
    private async Task WriteFileChainedAsync(Task previousWrite, int index, string path, string content, long version)
    {
        if (previousWrite != null)
        {
            await previousWrite.ConfigureAwait(false);
        }

        if (await WriteFileAsync(path, content).ConfigureAwait(false))
        {
            MarkPersisted(index, version);
        }
    }

    /// <summary>
    /// Returns whether the buffer's in-memory content differs from what was last
    /// written to disk, and outputs the current content version. Must be called
    /// under <see cref="syncRoot"/>.
    /// </summary>
    private bool IsDirty(int index, out long contentVersion)
    {
        contentVersion = contentVersionByIndex.TryGetValue(index, out long content) ? content : 0;
        long persisted = persistedVersionByIndex.TryGetValue(index, out long p) ? p : 0;
        return contentVersion > persisted;
    }

    /// <summary>
    /// Advances the buffer's persisted version to <paramref name="version"/> (never
    /// backwards), marking it clean up to that content update. A newer update that
    /// bumped the content version in the meantime leaves the buffer dirty.
    /// </summary>
    private void MarkPersisted(int index, long version)
    {
        lock (syncRoot)
        {
            long current = persistedVersionByIndex.TryGetValue(index, out long p) ? p : 0;
            if (version > current)
            {
                persistedVersionByIndex[index] = version;
            }
        }
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

    /// <summary>
    /// Final safeguard before content reaches disk: clamps to
    /// <see cref="AppConstants.MaxBufferLength"/> so a buffer can never be
    /// persisted larger than the editor's enforced limit, regardless of how the
    /// in-memory content was produced. Truncation is logged (length only, never
    /// content).
    /// </summary>
    private static string ClampToMaxLength(string content)
    {
        var text = content ?? string.Empty;
        if (text.Length <= AppConstants.MaxBufferLength)
        {
            return text;
        }

        Log.ForContext<BufferService>().Warning(
            "Buffer content of {ActualLength} chars exceeds the {MaxLength} limit; truncating before write.",
            text.Length,
            AppConstants.MaxBufferLength);
        return text.Substring(0, AppConstants.MaxBufferLength);
    }

    private static async Task<bool> WriteFileAsync(string path, string content)
    {
        try
        {
            await AtomicFileWriter.WriteAllTextAsync(path, ClampToMaxLength(content), Utf8WithBom).ConfigureAwait(false);
            return true;
        }
        catch (IOException ex)
        {
            Log.ForContext<BufferService>().Warning(ex, "Failed to write buffer file {Path}.", path);
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            Log.ForContext<BufferService>().Warning(ex, "Access denied writing buffer file {Path}.", path);
            return false;
        }
    }

    private static bool WriteFileSafe(string path, string content)
    {
        try
        {
            AtomicFileWriter.WriteAllText(path, ClampToMaxLength(content), Utf8WithBom);
            return true;
        }
        catch (IOException ex)
        {
            Log.ForContext<BufferService>().Warning(ex, "Failed to write buffer file {Path}.", path);
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            Log.ForContext<BufferService>().Warning(ex, "Access denied writing buffer file {Path}.", path);
            return false;
        }
    }
}
