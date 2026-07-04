using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace QuinSlate.Ui.Services;

/// <summary>
/// Writes a file atomically: the content goes to a temporary sibling file first,
/// is flushed through to disk, and is only then moved over the target in a single
/// rename. A crash or power loss mid-write can therefore never leave a truncated
/// target file — the next read sees either the old content or the new, never a
/// partial write. Callers must serialise their own writes per target path; the
/// temporary file is opened exclusively, so overlapping writers fail fast with an
/// <see cref="IOException"/> instead of corrupting each other.
/// </summary>
internal static class AtomicFileWriter
{
    private const string TempFileSuffix = ".tmp";

    /// <summary>
    /// Asynchronously writes <paramref name="content"/> to <paramref name="path"/>
    /// via the atomic temp-file-and-rename sequence. Failures propagate to the
    /// caller after the temporary file has been cleaned up.
    /// </summary>
    public static async Task WriteAllTextAsync(string path, string content, Encoding encoding)
    {
        string tempPath = path + TempFileSuffix;
        try
        {
            using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, encoding))
            {
                await writer.WriteAsync(content).ConfigureAwait(false);
                await writer.FlushAsync().ConfigureAwait(false);

                // Force the bytes to disk before the rename publishes the file, so the
                // swap can never make an unflushed (potentially empty) file the target.
                stream.Flush(flushToDisk: true);
            }

            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            TryDeleteTempFile(tempPath);
            throw;
        }
    }

    /// <summary>
    /// Synchronous variant of <see cref="WriteAllTextAsync"/> for shutdown flush
    /// paths where awaiting is not possible.
    /// </summary>
    public static void WriteAllText(string path, string content, Encoding encoding)
    {
        string tempPath = path + TempFileSuffix;
        try
        {
            using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, encoding))
            {
                writer.Write(content);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            TryDeleteTempFile(tempPath);
            throw;
        }
    }

    private static void TryDeleteTempFile(string tempPath)
    {
        try
        {
            File.Delete(tempPath);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
