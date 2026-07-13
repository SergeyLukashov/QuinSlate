using Microsoft.UI.Xaml;
using QuinSlate.Ui.Components;
using QuinSlate.Ui.Constants;
using QuinSlate.Ui.Interop;
using QuinSlate.Ui.Logging;
using QuinSlate.Ui.Services;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Buffer = QuinSlate.Ui.Models.Buffer;

namespace QuinSlate.Ui;

/// <summary>
/// Application entry point. Initialises the buffer service and creates the main
/// window. A manual launch shows the panel; a Windows startup-task launch (login)
/// starts hidden so only the tray icon is visible.
/// </summary>
public partial class App : Application
{
    private const string TrayIconAssetRelativePath = "Assets\\TrayIcon.ico";
    private const string MutexName = "Local\\" + AppConstants.AppName + "SingleInstance";
    internal const int ExitCodeNormal = 0;

    private MainWindow window;
    private BufferService bufferService;
    private SettingsService settingsService;
    private StartupService startupService;
    private Mutex singleInstanceMutex;

    /// <summary>
    /// Initialises the singleton application object.
    /// </summary>
    public App()
    {
        InitializeComponent();
        GlobalExceptionHandlers.Register(this);
    }

    /// <summary>
    /// Releases the single-instance mutex. Called from the main window's
    /// teardown path so a clean shutdown surrenders the mutex before the
    /// process ends.
    /// </summary>
    public void ReleaseSingleInstanceMutex()
    {
        if (singleInstanceMutex == null)
        {
            return;
        }

        try
        {
            singleInstanceMutex.ReleaseMutex();
        }
        catch (SynchronizationLockException ex)
        {
            Log.ForContext<App>().Warning(ex, "Failed to release single-instance mutex.");
        }

        singleInstanceMutex.Dispose();
        singleInstanceMutex = null;
    }

    /// <summary>
    /// Invoked when the application is launched. Hides the window and starts
    /// only the tray icon.
    /// </summary>
    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Resolve the activation kind first: for packaged apps GetActivatedEventArgs only
        // returns the arguments on its first call, so read it before any other startup work.
        bool launchedAtStartup = StartupService.WasLaunchedAtStartup();

        bool acquired;
        singleInstanceMutex = new Mutex(true, MutexName, out acquired);
        if (acquired == false)
        {
            var existing = NativeMethods.FindWindow(null, MainWindow.WindowTitle);
            if (existing != IntPtr.Zero)
            {
                NativeMethods.PostMessage(existing, (uint)NativeMethods.WM_QUINSLATE_ACTIVATE, IntPtr.Zero, IntPtr.Zero);
            }

            singleInstanceMutex.Dispose();
            singleInstanceMutex = null;
            LogBootstrapper.Shutdown();
            Environment.Exit(ExitCodeNormal);
            return;
        }

        var appDataDirectory = ResolveAppDataDirectory();
        LogBootstrapper.Initialize(appDataDirectory);

        // The browser-process spawn is the longest single step between launch and a usable
        // editor; start it before anything else so it overlaps the loads and window bring-up.
        EditorHost.PrewarmEnvironment(appDataDirectory);

        LogBootstrapper.LogStartupBanner();

        bufferService = new BufferService(appDataDirectory);
        settingsService = new SettingsService(appDataDirectory);

        Task<IReadOnlyList<Buffer>> buffersTask = Task.Run(() => bufferService.LoadAll());
        await Task.WhenAll(settingsService.LoadAsync(), buffersTask);

        startupService = new StartupService(settingsService);

        window = new MainWindow();
        window.Initialise(bufferService, buffersTask.Result, ResolveTrayIconPath(), startupService, settingsService, launchedAtStartup);

        // First-launch startup registration is a slow out-of-process broker call
        // (StartupTask.GetAsync); it has no bearing on the UI, so it runs after the
        // window is up rather than gating it.
        await startupService.EnsureRegisteredOnFirstLaunchAsync();
    }

    private static string ResolveAppDataDirectory()
    {
        var directory = AppDataPathResolver.Resolve();
        if (Directory.Exists(directory) == false)
        {
            Directory.CreateDirectory(directory);
        }

        return directory;
    }

    private static string ResolveTrayIconPath()
    {
        var baseDirectory = AppContext.BaseDirectory;
        if (string.IsNullOrEmpty(baseDirectory))
        {
            return null;
        }

        var path = Path.Combine(baseDirectory, TrayIconAssetRelativePath);
        if (File.Exists(path) == false)
        {
            return null;
        }

        return path;
    }
}
