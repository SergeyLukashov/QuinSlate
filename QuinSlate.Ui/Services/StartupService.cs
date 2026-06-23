using Serilog;
using System;
using System.Threading.Tasks;
using Windows.ApplicationModel;

namespace QuinSlate.Ui.Services;

/// <summary>
/// Manages QuinSlate's "launch at login" registration via the packaged-app
/// <see cref="StartupTask"/> API. QuinSlate ships as an MSIX-packaged desktop app,
/// so the legacy <c>HKCU\...\Run</c> registry key cannot be used: package registry
/// virtualization redirects the write into a private hive that Windows never reads at
/// login, and the bare packaged executable cannot be activated without package identity.
/// The startup task itself is declared as a <c>windows.startupTask</c> extension in
/// <c>Package.appxmanifest</c> (TaskId <c>QuinSlateStartupTask</c>).
/// </summary>
public sealed class StartupService
{
    private const string StartupTaskId = "QuinSlateStartupTask";

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
    /// Returns <c>true</c> when the startup task is currently enabled. Reads the live
    /// task state on every call — no caching — so it always reflects changes the user
    /// made through Task Manager or the Settings Startup page.
    /// </summary>
    public async Task<bool> IsEnabledAsync()
    {
        var task = await GetStartupTaskAsync();
        if (task == null)
        {
            return false;
        }

        return task.State == StartupTaskState.Enabled
            || task.State == StartupTaskState.EnabledByPolicy;
    }

    /// <summary>
    /// Requests that QuinSlate start with Windows. For a packaged desktop app this
    /// shows no consent dialog. Windows will not override a choice the user made in
    /// Task Manager (<see cref="StartupTaskState.DisabledByUser"/>); in that case the
    /// request is a no-op and the resulting state is logged.
    /// </summary>
    public async Task EnableAsync()
    {
        var task = await GetStartupTaskAsync();
        if (task == null)
        {
            return;
        }

        try
        {
            StartupTaskState newState = await task.RequestEnableAsync();
            if (newState == StartupTaskState.Enabled || newState == StartupTaskState.EnabledByPolicy)
            {
                Log.ForContext<StartupService>().Information("Startup registration enabled.");
            }
            else
            {
                Log.ForContext<StartupService>().Warning("Startup enable request did not take effect; task state is {State}.", newState);
            }
        }
        catch (Exception ex)
        {
            Log.ForContext<StartupService>().Error(ex, "Failed to enable startup registration.");
        }
    }

    /// <summary>
    /// Disables the startup task if it is currently enabled. Logs and does not mask
    /// failures.
    /// </summary>
    public async Task DisableAsync()
    {
        var task = await GetStartupTaskAsync();
        if (task == null)
        {
            return;
        }

        try
        {
            task.Disable();
            Log.ForContext<StartupService>().Information("Startup registration disabled.");
        }
        catch (Exception ex)
        {
            Log.ForContext<StartupService>().Error(ex, "Failed to disable startup registration.");
        }
    }

    /// <summary>
    /// Enables startup on the very first launch so QuinSlate is on by default.
    /// Subsequent launches leave the task state as the user left it, allowing an
    /// opt-out through the tray menu to persist.
    /// </summary>
    public async Task EnsureRegisteredOnFirstLaunchAsync()
    {
        if (settingsService.HasRegisteredStartup)
        {
            return;
        }

        await EnableAsync();
        settingsService.HasRegisteredStartup = true;
        await settingsService.SaveAsync();
    }

    /// <summary>
    /// Returns <c>true</c> when this process was activated by the Windows startup task
    /// (the user logging in) rather than launched manually. Reads the activation kind via
    /// <see cref="Microsoft.Windows.AppLifecycle.AppInstance.GetActivatedEventArgs"/>;
    /// a <see cref="Microsoft.Windows.AppLifecycle.ExtendedActivationKind.StartupTask"/>
    /// kind means a login launch. Any failure (e.g. no package identity on a dev run) is
    /// treated as a manual launch so the window still appears.
    /// </summary>
    public static bool WasLaunchedAtStartup()
    {
        try
        {
            Microsoft.Windows.AppLifecycle.AppActivationArguments activationArgs =
                Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent().GetActivatedEventArgs();
            if (activationArgs == null)
            {
                return false;
            }

            return activationArgs.Kind == Microsoft.Windows.AppLifecycle.ExtendedActivationKind.StartupTask;
        }
        catch (Exception ex)
        {
            Log.ForContext<StartupService>().Warning(ex, "Failed to read activation kind; assuming manual launch.");
            return false;
        }
    }

    /// <summary>
    /// Resolves the manifest-declared startup task. Returns <c>null</c> when the task
    /// cannot be retrieved — for example when running without package identity — so
    /// callers degrade gracefully instead of throwing.
    /// </summary>
    private static async Task<StartupTask> GetStartupTaskAsync()
    {
        try
        {
            return await StartupTask.GetAsync(StartupTaskId);
        }
        catch (Exception ex)
        {
            Log.ForContext<StartupService>().Error(ex, "Failed to resolve startup task '{TaskId}'.", StartupTaskId);
            return null;
        }
    }
}
