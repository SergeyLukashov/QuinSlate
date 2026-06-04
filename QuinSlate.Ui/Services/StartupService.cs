using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace QuinSlate.Ui.Services;

/// <summary>
/// Manages QuinSlate's Windows startup registration via the current-user run key
/// (<c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>).
/// No elevated privileges are required.
/// </summary>
public sealed class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppRegistryValueName = AppConstants.AppName;

    private readonly SettingsService settingsService;

    /// <summary>
    /// Constructs the service with a reference to the settings service used to
    /// persist the first-launch flag.
    /// </summary>
    public StartupService(SettingsService settingsService)
    {
        if (settingsService == null)
        {
            throw new ArgumentNullException(nameof(settingsService));
        }

        this.settingsService = settingsService;
    }

    /// <summary>
    /// Returns <c>true</c> when the "QuinSlate" value exists in the current-user run
    /// key. Reads the registry on every call — no caching.
    /// </summary>
    public bool IsEnabled()
    {
        using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false))
        {
            if (key == null)
            {
                return false;
            }

            return key.GetValue(AppRegistryValueName) != null;
        }
    }

    /// <summary>
    /// Writes the current executable path to the run key so QuinSlate starts with
    /// Windows. Logs and does not mask failures.
    /// </summary>
    public void Enable()
    {
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(executablePath))
        {
            Debug.WriteLine("StartupService: cannot enable startup — executable path is unavailable.");
            return;
        }

        try
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true))
            {
                if (key == null)
                {
                    Debug.WriteLine($"StartupService: cannot enable startup — registry key '{RunKeyPath}' could not be opened for writing.");
                    return;
                }

                key.SetValue(AppRegistryValueName, executablePath, RegistryValueKind.String);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"StartupService: failed to enable startup registration — {ex.Message}");
        }
    }

    /// <summary>
    /// Removes the "QuinSlate" value from the run key if it exists. Logs and does
    /// not mask failures.
    /// </summary>
    public void Disable()
    {
        try
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true))
            {
                if (key == null)
                {
                    return;
                }

                if (key.GetValue(AppRegistryValueName) != null)
                {
                    key.DeleteValue(AppRegistryValueName);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"StartupService: failed to disable startup registration — {ex.Message}");
        }
    }

    /// <summary>
    /// Registers QuinSlate for startup on the very first launch. Subsequent launches
    /// leave the registry value as the user left it, allowing opt-out via the
    /// tray menu to persist.
    /// </summary>
    public async Task EnsureRegisteredOnFirstLaunchAsync()
    {
        if (settingsService.HasRegisteredStartup)
        {
            return;
        }

        Enable();
        settingsService.HasRegisteredStartup = true;
        await settingsService.SaveAsync();
    }
}
