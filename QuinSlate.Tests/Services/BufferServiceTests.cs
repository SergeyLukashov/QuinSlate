using QuinSlate.Ui.Constants;
using QuinSlate.Ui.Services;

namespace QuinSlate.Tests.Services;

public sealed class BufferServiceTests : IDisposable
{
    private readonly string tempDirectory;
    private readonly BufferService bufferService;

    public BufferServiceTests()
    {
        tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        bufferService = new BufferService(tempDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(tempDirectory))
        {
            try
            {
                Directory.Delete(tempDirectory, true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    [Fact]
    public void Constructor_NullDirectory_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new BufferService(null));
    }

    [Fact]
    public void LoadAll_CreatesDirectoryIfMissing()
    {
        Assert.False(Directory.Exists(tempDirectory));

        bufferService.LoadAll();

        Assert.True(Directory.Exists(tempDirectory));
    }

    [Fact]
    public void LoadAll_InitialisesFiveBuffersWithDefaultContent()
    {
        var buffers = bufferService.LoadAll();

        Assert.Equal(5, buffers.Count);
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(i + 1, buffers[i].Index);
            Assert.Equal(DefaultBuffers.GetDefaultContent(i + 1), buffers[i].Content);
        }
    }

    [Fact]
    public void LoadAll_ReadsExistingFilesAndInitialisesMissingWithDefaults()
    {
        Directory.CreateDirectory(tempDirectory);
        var expectedContent = "existing content";
        File.WriteAllText(Path.Combine(tempDirectory, "buffer-3.txt"), expectedContent);

        var buffers = bufferService.LoadAll();

        // Buffer 3 has existing file with content, should read that content
        Assert.Equal(expectedContent, buffers[2].Content);

        // Buffer 1 does not exist, should load default content
        Assert.Equal(DefaultBuffers.GetDefaultContent(1), buffers[0].Content);
    }

    [Fact]
    public void LoadAll_ExistingEmptyFile_LoadsAsEmpty()
    {
        Directory.CreateDirectory(tempDirectory);
        File.WriteAllText(Path.Combine(tempDirectory, "buffer-2.txt"), string.Empty);

        var buffers = bufferService.LoadAll();

        // Buffer 2 exists but is empty, should remain empty and not load default content
        Assert.Equal(string.Empty, buffers[1].Content);

        // Buffer 1 does not exist, should load default content
        Assert.Equal(DefaultBuffers.GetDefaultContent(1), buffers[0].Content);
    }

    [Fact]
    public void GetBuffer_ValidIndex_ReturnsBuffer()
    {
        bufferService.LoadAll();

        var buffer = bufferService.GetBuffer(2);

        Assert.NotNull(buffer);
        Assert.Equal(2, buffer.Index);
    }

    [Fact]
    public void GetBuffer_InvalidIndex_ReturnsNull()
    {
        bufferService.LoadAll();

        var buffer = bufferService.GetBuffer(8);

        Assert.Null(buffer);
    }

    [Fact]
    public void GetBuffer_BeforeLoadAll_ReturnsNull()
    {
        var buffer = bufferService.GetBuffer(1);

        Assert.Null(buffer);
    }

    [Fact]
    public void UpdateContent_UpdatesInMemoryContent()
    {
        bufferService.LoadAll();

        var newContent = "updated text";
        bufferService.UpdateContent(4, newContent);

        var buffer = bufferService.GetBuffer(4);
        Assert.Equal(newContent, buffer.Content);
    }

    [Fact]
    public void UpdateContent_NullContent_SetsEmptyString()
    {
        bufferService.LoadAll();

        bufferService.UpdateContent(4, null);

        var buffer = bufferService.GetBuffer(4);
        Assert.Equal(string.Empty, buffer.Content);
    }

    [Fact]
    public void FlushPendingWritesSync_WritesAllBuffersToDisk()
    {
        bufferService.LoadAll();

        bufferService.UpdateContent(1, "content 1");
        bufferService.UpdateContent(5, "content 5");

        // Flush immediately, before debounce timer fires
        bufferService.FlushPendingWritesSync();

        var content1 = File.ReadAllText(Path.Combine(tempDirectory, "buffer-1.txt"));
        var content5 = File.ReadAllText(Path.Combine(tempDirectory, "buffer-5.txt"));

        // Content contains UTF8 BOM according to Utf8WithBom encoding in service, but File.ReadAllText handles it
        Assert.Equal("content 1", content1);
        Assert.Equal("content 5", content5);
    }

    [Fact]
    public void FlushPendingWritesSync_ContentExceedingMax_TruncatesOnDisk()
    {
        bufferService.LoadAll();

        var oversized = new string('x', AppConstants.MaxBufferLength + 500);
        bufferService.UpdateContent(1, oversized);

        bufferService.FlushPendingWritesSync();

        var written = File.ReadAllText(Path.Combine(tempDirectory, "buffer-1.txt"));
        Assert.Equal(AppConstants.MaxBufferLength, written.Length);
    }

    [Fact]
    public void DebounceTimer_WritesToDiskAfterDelay()
    {
        bufferService.LoadAll();

        bufferService.UpdateContent(2, "delayed content");

        // Wait for debounce timer (300ms) + buffer (100ms)
        Thread.Sleep(500);

        var filePath = Path.Combine(tempDirectory, "buffer-2.txt");
        Assert.True(File.Exists(filePath));
        Assert.Equal("delayed content", File.ReadAllText(filePath));
    }

    [Fact]
    public void FlushPendingWritesSync_WhileDebouncedWriteInFlight_PersistsLatestContent()
    {
        bufferService.LoadAll();

        bufferService.UpdateContent(3, "first version");

        // Let the debounce timer fire so its async write is (or was) in flight,
        // then supersede the content and flush immediately.
        Thread.Sleep(350);
        bufferService.UpdateContent(3, "latest version");

        bufferService.FlushPendingWritesSync();

        var written = File.ReadAllText(Path.Combine(tempDirectory, "buffer-3.txt"));
        Assert.Equal("latest version", written);
    }

    [Fact]
    public void Writes_LeaveNoTemporaryFilesBehind()
    {
        bufferService.LoadAll();

        bufferService.UpdateContent(1, "content via debounce");
        Thread.Sleep(500);

        bufferService.UpdateContent(2, "content via flush");
        bufferService.FlushPendingWritesSync();

        Assert.Empty(Directory.GetFiles(tempDirectory, "*.tmp"));
    }

    [Fact]
    public void FlushPendingWritesSync_OnlyWritesDirtyBuffers()
    {
        bufferService.LoadAll();

        bufferService.UpdateContent(1, "only one edited");

        // Flush immediately, before any debounce fires. Only buffer 1 was edited, so the other
        // four are clean and must not be written to disk.
        bufferService.FlushPendingWritesSync();

        Assert.True(File.Exists(Path.Combine(tempDirectory, "buffer-1.txt")));
        Assert.False(File.Exists(Path.Combine(tempDirectory, "buffer-2.txt")));
        Assert.False(File.Exists(Path.Combine(tempDirectory, "buffer-5.txt")));
    }

    [Fact]
    public void FlushPendingWritesSync_SkipsBufferAlreadyPersistedByDebounce()
    {
        bufferService.LoadAll();

        bufferService.UpdateContent(2, "persisted by debounce");

        // Let the debounce timer write it, then remove the file so a redundant flush write would
        // be visible by the file reappearing.
        Thread.Sleep(500);
        var path = Path.Combine(tempDirectory, "buffer-2.txt");
        Assert.True(File.Exists(path));
        File.Delete(path);

        // Buffer 2 is clean (its persisted version matches its content version), so the flush must
        // skip it and not recreate the file.
        bufferService.FlushPendingWritesSync();

        Assert.False(File.Exists(path));
    }

    [Fact]
    public void FlushPendingWritesSync_EditAfterDebounce_RewritesBuffer()
    {
        bufferService.LoadAll();

        bufferService.UpdateContent(3, "first");
        Thread.Sleep(500);

        // A fresh edit after the debounce persisted the first version makes the buffer dirty
        // again, so the flush must write the newer content.
        bufferService.UpdateContent(3, "second");
        bufferService.FlushPendingWritesSync();

        Assert.Equal("second", File.ReadAllText(Path.Combine(tempDirectory, "buffer-3.txt")));
    }

    [Fact]
    public void DebounceTimer_RapidSuccessiveUpdates_PersistsLatestContent()
    {
        bufferService.LoadAll();

        for (var i = 1; i <= 10; i++)
        {
            bufferService.UpdateContent(4, $"revision {i}");
        }

        Thread.Sleep(500);

        var written = File.ReadAllText(Path.Combine(tempDirectory, "buffer-4.txt"));
        Assert.Equal("revision 10", written);
    }
}
