using QuinSlate.Ui.Models;
using QuinSlate.Ui.Services;

namespace QuinSlate.Tests.Services;

public sealed class TabDefinitionStoreTests : IDisposable
{
    private const int TabCount = 5;

    private readonly string tempDirectory;
    private readonly SettingsService settingsService;

    public TabDefinitionStoreTests()
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
    public void Constructor_NullSettingsService_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new TabDefinitionStore(null, settingsService.GetTabs()));
    }

    [Fact]
    public void Constructor_NullDefinitions_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new TabDefinitionStore(settingsService, null));
    }

    [Fact]
    public void Find_ReturnsTheDefinitionForTheBuffer()
    {
        var store = new TabDefinitionStore(settingsService, settingsService.GetTabs());

        Assert.Equal("Tasks", store.Find(2).Title);
        Assert.Null(store.Find(9));
    }

    [Fact]
    public void TryApplyStripOrder_NewOrder_PersistsItAndReturnsTrue()
    {
        var store = new TabDefinitionStore(settingsService, settingsService.GetTabs());

        bool applied = store.TryApplyStripOrder(new[] { 3, 1, 5, 2, 4 }, TabCount);

        Assert.True(applied);
        Assert.Equal(new[] { 3, 1, 5, 2, 4 }, store.Tabs.Select(t => t.Id));
        Assert.Equal(new[] { 3, 1, 5, 2, 4 }, settingsService.GetTabs().Select(t => t.Id));
    }

    [Fact]
    public void TryApplyStripOrder_UnchangedOrder_ReturnsFalse()
    {
        // The strip's initial realization, and a drag dropped where it started, must not write.
        IReadOnlyList<TabDefinition> tabs = settingsService.GetTabs();
        var store = new TabDefinitionStore(settingsService, tabs);

        Assert.False(store.TryApplyStripOrder(new[] { 1, 2, 3, 4, 5 }, TabCount));
        Assert.Same(tabs, store.Tabs);
    }

    [Fact]
    public void TryApplyStripOrder_ShortStrip_ReturnsFalse()
    {
        // Mid-reorder the dragged tab is momentarily out of the strip; a four-tab order is never kept.
        IReadOnlyList<TabDefinition> tabs = settingsService.GetTabs();
        var store = new TabDefinitionStore(settingsService, tabs);

        Assert.False(store.TryApplyStripOrder(new[] { 2, 1, 3, 4 }, TabCount));
        Assert.Same(tabs, store.Tabs);
    }

    [Fact]
    public void UpdateLabel_ChangesOnlyThatTab_AndPersists()
    {
        var store = new TabDefinitionStore(settingsService, settingsService.GetTabs());

        store.UpdateLabel(2, "🔥", "Hot");

        Assert.Equal("🔥", store.Find(2).Emoji);
        Assert.Equal("Hot", store.Find(2).Title);
        Assert.Equal("Scratch", store.Find(1).Title);
        Assert.Equal("Hot", settingsService.GetTabs()[1].Title);
    }

    [Fact]
    public void UpdateLabel_KeepsTheTabOrder()
    {
        var store = new TabDefinitionStore(settingsService, settingsService.GetTabs());
        store.TryApplyStripOrder(new[] { 5, 4, 3, 2, 1 }, TabCount);

        store.UpdateLabel(3, "🔥", "Hot");

        Assert.Equal(new[] { 5, 4, 3, 2, 1 }, store.Tabs.Select(t => t.Id));
    }
}
