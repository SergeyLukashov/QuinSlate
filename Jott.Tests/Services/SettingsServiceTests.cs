using Jott.Ui.Services;

namespace Jott.Tests.Services;

public sealed class SettingsServiceTests : IDisposable
{
    private readonly string tempDirectory;
    private readonly SettingsService settingsService;

    public SettingsServiceTests()
    {
        tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDirectory);
        settingsService = new SettingsService(tempDirectory);
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
        Assert.Throws<ArgumentNullException>(() => new SettingsService(null));
    }

    [Fact]
    public void SettingsFilePath_ReturnsCorrectPath()
    {
        var expectedPath = Path.Combine(tempDirectory, "settings.json");
        Assert.Equal(expectedPath, settingsService.SettingsFilePath);
    }

    [Fact]
    public async Task LoadAsync_MissingFile_DoesNotThrowAndKeepsDefaults()
    {
        // File does not exist yet
        await settingsService.LoadAsync();

        Assert.False(settingsService.HasRegisteredStartup);
    }

    [Fact]
    public async Task SaveAsync_PersistsSettings()
    {
        settingsService.HasRegisteredStartup = true;

        await settingsService.SaveAsync();

        Assert.True(File.Exists(settingsService.SettingsFilePath));
        var content = await File.ReadAllTextAsync(settingsService.SettingsFilePath);
        Assert.Contains("\"HasRegisteredStartup\":true", content);
    }

    [Fact]
    public async Task LoadAsync_ExistingFile_RestoresSettings()
    {
        var json = "{\"HasRegisteredStartup\":true}";
        await File.WriteAllTextAsync(settingsService.SettingsFilePath, json);

        await settingsService.LoadAsync();

        Assert.True(settingsService.HasRegisteredStartup);
    }

    [Fact]
    public async Task LoadAsync_MalformedJson_DoesNotThrowAndKeepsDefaults()
    {
        await File.WriteAllTextAsync(settingsService.SettingsFilePath, "not valid json");

        await settingsService.LoadAsync();

        // Should handle exception internally and keep default (false)
        Assert.False(settingsService.HasRegisteredStartup);
    }

    [Fact]
    public void WindowWidth_Default_IsZero()
    {
        Assert.Equal(0, settingsService.WindowWidth);
    }

    [Fact]
    public void WindowHeight_Default_IsZero()
    {
        Assert.Equal(0, settingsService.WindowHeight);
    }

    [Fact]
    public async Task SaveAsync_PersistsWindowSize()
    {
        settingsService.WindowWidth = 560;
        settingsService.WindowHeight = 680;

        await settingsService.SaveAsync();

        var content = await File.ReadAllTextAsync(settingsService.SettingsFilePath);
        Assert.Contains("\"WindowWidth\":560", content);
        Assert.Contains("\"WindowHeight\":680", content);
    }

    [Fact]
    public async Task LoadAsync_ExistingFile_RestoresWindowSize()
    {
        var json = "{\"WindowWidth\":560,\"WindowHeight\":680}";
        await File.WriteAllTextAsync(settingsService.SettingsFilePath, json);

        await settingsService.LoadAsync();

        Assert.Equal(560, settingsService.WindowWidth);
        Assert.Equal(680, settingsService.WindowHeight);
    }

    [Fact]
    public async Task LoadAsync_FileWithoutWindowSize_DefaultsToZero()
    {
        var json = "{\"HasRegisteredStartup\":false}";
        await File.WriteAllTextAsync(settingsService.SettingsFilePath, json);

        await settingsService.LoadAsync();

        Assert.Equal(0, settingsService.WindowWidth);
        Assert.Equal(0, settingsService.WindowHeight);
    }
}
