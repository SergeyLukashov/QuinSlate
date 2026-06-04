using QuinSlate.Ui.Services;

namespace QuinSlate.Tests.Services;

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

    [Fact]
    public async Task LoadAsync_FileWithoutTrayPeekEnabled_DefaultsToTrue()
    {
        var json = "{\"HasRegisteredStartup\":false}";
        await File.WriteAllTextAsync(settingsService.SettingsFilePath, json);

        await settingsService.LoadAsync();

        Assert.True(settingsService.TrayPeekEnabled);
    }

    [Fact]
    public void WindowLeft_Default_IsZero()
    {
        Assert.Equal(0, settingsService.WindowLeft);
    }

    [Fact]
    public void WindowTop_Default_IsZero()
    {
        Assert.Equal(0, settingsService.WindowTop);
    }

    [Fact]
    public async Task SaveAsync_PersistsWindowPosition()
    {
        settingsService.WindowLeft = 100;
        settingsService.WindowTop = 200;

        await settingsService.SaveAsync();

        var content = await File.ReadAllTextAsync(settingsService.SettingsFilePath);
        Assert.Contains("\"WindowLeft\":100", content);
        Assert.Contains("\"WindowTop\":200", content);
    }

    [Fact]
    public async Task LoadAsync_ExistingFile_RestoresWindowPosition()
    {
        var json = "{\"WindowLeft\":100,\"WindowTop\":200}";
        await File.WriteAllTextAsync(settingsService.SettingsFilePath, json);

        await settingsService.LoadAsync();

        Assert.Equal(100, settingsService.WindowLeft);
        Assert.Equal(200, settingsService.WindowTop);
    }

    [Fact]
    public void GetTabs_NoTabsInSettings_ReturnsDefaults()
    {
        var tabs = settingsService.GetTabs();

        Assert.Equal(5, tabs.Count);
        Assert.Equal(1, tabs[0].Id);
        Assert.Equal("📋", tabs[0].Emoji);
        Assert.Equal("Scratch", tabs[0].Title);
        Assert.Equal(2, tabs[1].Id);
        Assert.Equal("✅", tabs[1].Emoji);
        Assert.Equal("Tasks", tabs[1].Title);
    }

    [Fact]
    public async Task SetTabs_SavesAndRestores()
    {
        var tabs = settingsService.GetTabs();
        var modified = new List<QuinSlate.Ui.Models.TabDefinition>(tabs)
        {
            [0] = new QuinSlate.Ui.Models.TabDefinition { Id = 1, Emoji = "🔥", Title = "Hot" }
        };

        settingsService.SetTabs(modified);

        await Task.Delay(50);

        var fresh = new SettingsService(tempDirectory);
        await fresh.LoadAsync();
        var restored = fresh.GetTabs();

        Assert.Equal("🔥", restored[0].Emoji);
        Assert.Equal("Hot", restored[0].Title);
        Assert.Equal("✅", restored[1].Emoji);
        Assert.Equal("Tasks", restored[1].Title);
    }

    [Fact]
    public void AddRecentEmoji_KeepsMax7()
    {
        for (int i = 0; i < 10; i++)
        {
            settingsService.AddRecentEmoji($"E{i}");
        }

        var recent = settingsService.GetRecentEmoji();
        Assert.Equal(7, recent.Count);
        Assert.Equal("E9", recent[0]);
        Assert.Equal("E3", recent[6]);
    }

    [Fact]
    public void AddRecentEmoji_Deduplicates()
    {
        settingsService.AddRecentEmoji("⭐");
        settingsService.AddRecentEmoji("🔥");
        settingsService.AddRecentEmoji("⭐");

        var recent = settingsService.GetRecentEmoji();
        Assert.Equal(2, recent.Count);
        Assert.Equal("⭐", recent[0]);
        Assert.Equal("🔥", recent[1]);
    }

    [Fact]
    public void TrayPeekEnabled_Default_IsTrue()
    {
        Assert.True(settingsService.TrayPeekEnabled);
    }
}
