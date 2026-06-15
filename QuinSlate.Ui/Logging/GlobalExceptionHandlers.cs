using Microsoft.UI.Xaml;
using Serilog;
using System;
using System.Threading.Tasks;

namespace QuinSlate.Ui.Logging;

/// <summary>
/// Registers the three process-wide crash hooks so no unhandled exception goes
/// unrecorded: UI-thread, background-thread/finalizer, and unobserved task
/// exceptions. UI-thread and AppDomain crashes are logged as Fatal and the
/// logger is flushed before the process terminates.
/// </summary>
public static class GlobalExceptionHandlers
{
    /// <summary>
    /// Subscribes the handlers. Safe to call before the logger is configured —
    /// Serilog's static <see cref="Log"/> is a silent no-op until then.
    /// </summary>
    public static void Register(Application application)
    {
        if (application == null)
        {
            throw new ArgumentNullException(nameof(application));
        }

        application.UnhandledException += OnApplicationUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private static void OnApplicationUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        Log.Fatal(e.Exception, "Unhandled UI-thread exception: {Message}", e.Message);
        Log.CloseAndFlush();
    }

    private static void OnAppDomainUnhandledException(object sender, System.UnhandledExceptionEventArgs e)
    {
        var exception = e.ExceptionObject as Exception;
        Log.Fatal(exception, "Unhandled AppDomain exception (terminating: {IsTerminating}).", e.IsTerminating);
        Log.CloseAndFlush();
    }

    private static void OnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unobserved task exception.");
        e.SetObserved();
    }
}
