using Microsoft.UI.Xaml;
using QuinSlate.Ui.Interop;
using QuinSlate.Ui.Services;
using System;
using System.IO;
using System.Threading;

namespace QuinSlate.Ui;

/// <summary>
/// Application entry point. Initialises the buffer service, creates the
/// main window, and starts the panel hidden so only the tray icon is visible.
/// </summary>
public partial class App : Application
{
    private const string AppDataFolderName = AppConstants.AppName;
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
            System.Diagnostics.Debug.WriteLine($"[{AppConstants.AppName}] ReleaseSingleInstanceMutex: {ex.Message}");
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
            Environment.Exit(ExitCodeNormal);
            return;
        }

        var appDataDirectory = ResolveAppDataDirectory();
        bufferService = new BufferService(appDataDirectory);

        settingsService = new SettingsService(appDataDirectory);
        await settingsService.LoadAsync();

        startupService = new StartupService(settingsService);
        await startupService.EnsureRegisteredOnFirstLaunchAsync();

        window = new MainWindow();
        window.Initialise(bufferService, ResolveTrayIconPath(), startupService, settingsService);
    }

    private static string ResolveAppDataDirectory()
    {
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var directory = Path.Combine(roaming, AppDataFolderName);
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
