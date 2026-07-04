using QuinSlate.Ui.Models;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace QuinSlate.Ui.Services;

/// <summary>
/// Persists non-buffer state to a single <c>settings.json</c> file under
/// <c>%AppData%\QuinSlate\</c>.
/// </summary>
public sealed partial class SettingsService
{
    private const string SettingsFileName = "settings.json";
    private const int MaxRecentEmoji = 7;

    private static readonly TabEntry[] DefaultTabEntries = new[]
    {
        new TabEntry { Id = 1, Emoji = "📋", Title = "Scratch" },
        new TabEntry { Id = 2, Emoji = "✅", Title = "Tasks"   },
        new TabEntry { Id = 3, Emoji = "💡", Title = "Ideas"   },
        new TabEntry { Id = 4, Emoji = "🔗", Title = "Links"   },
        new TabEntry { Id = 5, Emoji = "📖", Title = "Notes"   },
    };

    private readonly string settingsFilePath;
    private readonly SemaphoreSlim saveGate = new SemaphoreSlim(1, 1);
    private AppSettings settings;
    private long saveVersion;
    private long lastWrittenSaveVersion;

    /// <summary>
    /// Constructs the service rooted at <paramref name="appDataDirectory"/>
    /// (typically <c>%AppData%\QuinSlate\</c>).
    /// </summary>
    public SettingsService(string appDataDirectory)
    {
        if (appDataDirectory == null)
        {
            throw new ArgumentNullException(nameof(appDataDirectory));
        }

        settingsFilePath = Path.Combine(appDataDirectory, SettingsFileName);
        settings = new AppSettings();
    }

    /// <summary>
    /// The absolute path to the settings file, even if it does not yet exist.
    /// </summary>
    public string SettingsFilePath => settingsFilePath;

    /// <summary>
    /// Whether QuinSlate has already registered itself for startup on a prior launch.
    /// </summary>
    public bool HasRegisteredStartup
    {
        get => settings.HasRegisteredStartup;
        set => settings.HasRegisteredStartup = value;
    }

    /// <summary>
    /// Whether the one-time "QuinSlate keeps running in the tray" notice has
    /// already been shown. Set the first time the panel is hidden to the tray so
    /// the notice never repeats on later launches.
    /// </summary>
    public bool HasShownTrayNotice
    {
        get => settings.HasShownTrayNotice;
        set => settings.HasShownTrayNotice = value;
    }

    /// <summary>
    /// Last saved window width in logical pixels (DIP). Zero means use the default.
    /// </summary>
    public int WindowWidth
    {
        get => settings.WindowWidth;
        set => settings.WindowWidth = value;
    }

    /// <summary>
    /// Last saved window height in logical pixels (DIP). Zero means use the default.
    /// </summary>
    public int WindowHeight
    {
        get => settings.WindowHeight;
        set => settings.WindowHeight = value;
    }

    /// <summary>
    /// Whether a saved window position exists and should be restored on next launch.
    /// </summary>
    public bool HasSavedPosition
    {
        get => settings.HasSavedPosition;
        set => settings.HasSavedPosition = value;
    }

    /// <summary>
    /// Last saved window left position in logical pixels (DIP). Zero means use the default anchor.
    /// </summary>
    public int WindowLeft
    {
        get => settings.WindowLeft;
        set => settings.WindowLeft = value;
    }

    /// <summary>
    /// Last saved window top position in logical pixels (DIP). Zero means use the default anchor.
    /// </summary>
    public int WindowTop
    {
        get => settings.WindowTop;
        set => settings.WindowTop = value;
    }

    /// <summary>
    /// Whether the tray peek preview window is shown on hover. Enabled by default.
    /// </summary>
    public bool TrayPeekEnabled
    {
        get => settings.TrayPeekEnabled;
        set => settings.TrayPeekEnabled = value;
    }

    /// <summary>
    /// Whether the main panel is pinned above all other windows (HWND_TOPMOST).
    /// </summary>
    public bool IsPinned
    {
        get => settings.IsPinned;
        set => settings.IsPinned = value;
    }

    /// <summary>
    /// Returns the five tab definitions from settings, falling back to defaults
    /// when no <c>tabs</c> entry is present or entries are missing.
    /// </summary>
    public IReadOnlyList<TabDefinition> GetTabs()
    {
        var result = new List<TabDefinition>(DefaultTabEntries.Length);

        for (int i = 0; i < DefaultTabEntries.Length; i++)
        {
            var def = DefaultTabEntries[i];
            TabEntry entry = null;

            if (settings.Tabs != null)
            {
                foreach (var e in settings.Tabs)
                {
                    if (e != null && e.Id == def.Id)
                    {
                        entry = e;
                        break;
                    }
                }
            }

            if (entry == null)
            {
                result.Add(new TabDefinition { Id = def.Id, Emoji = def.Emoji, Title = def.Title });
            }
            else
            {
                string emoji = string.IsNullOrEmpty(entry.Emoji) ? def.Emoji : entry.Emoji;
                string title = entry.Title != null ? entry.Title : def.Title;
                result.Add(new TabDefinition { Id = entry.Id, Emoji = emoji, Title = title });
            }
        }

        return result;
    }

    /// <summary>
    /// Replaces the persisted tab definitions and triggers an async save.
    /// </summary>
    public void SetTabs(IReadOnlyList<TabDefinition> tabs)
    {
        if (tabs == null)
        {
            throw new ArgumentNullException(nameof(tabs));
        }

        var entries = new List<TabEntry>(tabs.Count);
        foreach (var tab in tabs)
        {
            entries.Add(new TabEntry { Id = tab.Id, Emoji = tab.Emoji, Title = tab.Title });
        }

        settings.Tabs = entries;
        _ = SaveAsync();
    }

    /// <summary>
    /// Returns the recently used emoji list (up to 7 entries) from settings.
    /// Returns an empty list when no recent emoji have been recorded.
    /// </summary>
    public IReadOnlyList<string> GetRecentEmoji()
    {
        if (settings.RecentEmoji == null)
        {
            return new List<string>();
        }

        return settings.RecentEmoji;
    }

    /// <summary>
    /// Prepends <paramref name="emoji"/> to the recent emoji list, deduplicates,
    /// caps the list at 8 entries, and triggers an async save.
    /// </summary>
    public void AddRecentEmoji(string emoji)
    {
        if (string.IsNullOrEmpty(emoji))
        {
            return;
        }

        if (settings.RecentEmoji == null)
        {
            settings.RecentEmoji = new List<string>();
        }

        settings.RecentEmoji.Remove(emoji);
        settings.RecentEmoji.Insert(0, emoji);

        while (settings.RecentEmoji.Count > MaxRecentEmoji)
        {
            settings.RecentEmoji.RemoveAt(settings.RecentEmoji.Count - 1);
        }

        _ = SaveAsync();
    }

    /// <summary>
    /// Loads settings from disk asynchronously. A missing or malformed file is
    /// not an error; defaults are used instead.
    /// </summary>
    public async Task LoadAsync()
    {
        if (File.Exists(settingsFilePath) == false)
        {
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(settingsFilePath, Encoding.UTF8);
            var loaded = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings);
            if (loaded != null)
            {
                settings = loaded;
            }
        }
        catch (Exception ex)
        {
            Log.ForContext<SettingsService>().Warning(ex, "Failed to load settings; using defaults.");
        }
    }

    /// <summary>
    /// Persists the current settings to disk asynchronously. The state is
    /// snapshotted synchronously on the calling (UI) thread, so later property
    /// mutations cannot tear the serialized output; concurrent saves are
    /// serialised behind a gate and stale snapshots are skipped, so the file
    /// always ends up holding the newest snapshot regardless of completion order.
    /// </summary>
    public async Task SaveAsync()
    {
        try
        {
            var json = JsonSerializer.Serialize(settings, SettingsJsonContext.Default.AppSettings);
            long version = Interlocked.Increment(ref saveVersion);

            await saveGate.WaitAsync().ConfigureAwait(false);
            try
            {
                await WriteSnapshotGatedAsync(json, version).ConfigureAwait(false);
            }
            finally
            {
                saveGate.Release();
            }
        }
        catch (Exception ex)
        {
            Log.ForContext<SettingsService>().Warning(ex, "Failed to save settings.");
        }
    }

    /// <summary>
    /// Persists the current settings to disk synchronously. Call this only in
    /// shutdown paths where the UI thread must not await (e.g. <c>Teardown</c>).
    /// Waits for any in-flight <see cref="SaveAsync"/> write to finish first so
    /// the two can never write the file concurrently.
    /// </summary>
    public void SaveSync()
    {
        try
        {
            var json = JsonSerializer.Serialize(settings, SettingsJsonContext.Default.AppSettings);
            long version = Interlocked.Increment(ref saveVersion);

            saveGate.Wait();
            try
            {
                if (version > Volatile.Read(ref lastWrittenSaveVersion))
                {
                    AtomicFileWriter.WriteAllText(settingsFilePath, json, Encoding.UTF8);
                    Volatile.Write(ref lastWrittenSaveVersion, version);
                }
            }
            finally
            {
                saveGate.Release();
            }
        }
        catch (Exception ex)
        {
            Log.ForContext<SettingsService>().Warning(ex, "Failed to save settings.");
        }
    }

    private async Task WriteSnapshotGatedAsync(string json, long version)
    {
        if (version <= Volatile.Read(ref lastWrittenSaveVersion))
        {
            return;
        }

        await AtomicFileWriter.WriteAllTextAsync(settingsFilePath, json, Encoding.UTF8).ConfigureAwait(false);
        Volatile.Write(ref lastWrittenSaveVersion, version);
    }

    /// <summary>
    /// Serialization shape for a single tab entry stored in <c>settings.json</c>.
    /// </summary>
    public sealed class TabEntry
    {
        /// <summary>1-based tab identifier.</summary>
        public int Id { get; set; }

        /// <summary>Emoji character(s) for the tab.</summary>
        public string Emoji { get; set; }

        /// <summary>Display title for the tab.</summary>
        public string Title { get; set; }
    }

    private sealed class AppSettings
    {
        public bool HasRegisteredStartup { get; set; }
        public bool HasShownTrayNotice { get; set; }
        public int WindowWidth { get; set; }
        public int WindowHeight { get; set; }
        public bool HasSavedPosition { get; set; }
        public int WindowLeft { get; set; }
        public int WindowTop { get; set; }
        public bool TrayPeekEnabled { get; set; } = true;
        public bool IsPinned { get; set; }
        public List<SettingsService.TabEntry> Tabs { get; set; }
        public List<string> RecentEmoji { get; set; }
    }

    /// <summary>
    /// Source-generated <see cref="JsonSerializerContext"/> for <see cref="AppSettings"/>.
    /// Required because the trimmed/published build disables reflection-based
    /// <see cref="JsonSerializer"/>; the generator emits the metadata at compile time.
    /// </summary>
    [JsonSerializable(typeof(AppSettings))]
    private sealed partial class SettingsJsonContext : JsonSerializerContext
    {
    }
}
