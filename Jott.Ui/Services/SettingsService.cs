using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Jott.Ui.Services;

/// <summary>
/// Persists non-buffer state to a single <c>settings.json</c> file under
/// <c>%AppData%\Jott\</c>.
/// </summary>
public sealed class SettingsService
{
    private const string SettingsFileName = "settings.json";

    private readonly string settingsFilePath;
    private AppSettings settings;

    /// <summary>
    /// Constructs the service rooted at <paramref name="appDataDirectory"/>
    /// (typically <c>%AppData%\Jott\</c>).
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
    /// Whether Jott has already registered itself for startup on a prior launch.
    /// </summary>
    public bool HasRegisteredStartup
    {
        get => settings.HasRegisteredStartup;
        set => settings.HasRegisteredStartup = value;
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
            var loaded = JsonSerializer.Deserialize<AppSettings>(json);
            if (loaded != null)
            {
                settings = loaded;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"SettingsService: failed to load settings — {ex.Message}");
        }
    }

    /// <summary>
    /// Persists the current settings to disk asynchronously.
    /// </summary>
    public async Task SaveAsync()
    {
        try
        {
            var json = JsonSerializer.Serialize(settings);
            await File.WriteAllTextAsync(settingsFilePath, json, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"SettingsService: failed to save settings — {ex.Message}");
        }
    }

    private sealed class AppSettings
    {
        public bool HasRegisteredStartup { get; set; }
    }
}
