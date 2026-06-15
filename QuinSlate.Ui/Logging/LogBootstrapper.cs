using QuinSlate.Ui.Constants;
using Serilog;
using Serilog.Events;
using System;
using System.IO;
using System.Text;

namespace QuinSlate.Ui.Logging;

/// <summary>
/// Configures the global Serilog logger and owns the session banner and the
/// shutdown flush. The file sink is async-wrapped so log writes never block the
/// UI thread; <see cref="Shutdown"/> drains it before the process exits.
/// </summary>
public static class LogBootstrapper
{
    private const string LogsFolderName = "Logs";
    private const string LogFileBaseName = "quinslate-.log";
    private const long FileSizeLimitBytes = 10L * 1024 * 1024;
    private const int RetainedFileCountLimit = 31;
    private const int RetainedFileTimeLimitDays = 14;
    private const int AsyncBufferCapacity = 10000;
    private const int SessionIdLength = 8;

    private const string OutputTemplate =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] ({ThreadId}) {SourceContext}: {Message:lj}{NewLine}{Exception}";

#if DEBUG
    private const LogEventLevel MinimumLevel = LogEventLevel.Verbose;
#else
    private const LogEventLevel MinimumLevel = LogEventLevel.Information;
#endif

    /// <summary>
    /// Returns the <c>Logs</c> subfolder under the given application data directory.
    /// </summary>
    public static string ResolveLogDirectory(string appDataDirectory)
    {
        if (appDataDirectory == null)
        {
            throw new ArgumentNullException(nameof(appDataDirectory));
        }

        return Path.Combine(appDataDirectory, LogsFolderName);
    }

    /// <summary>
    /// Configures the global <see cref="Log.Logger"/>. Fails soft: if the file
    /// sink cannot be created the application still starts (logging is reduced to
    /// the DEBUG sink, or none).
    /// </summary>
    public static void Initialize(string appDataDirectory)
    {
        if (appDataDirectory == null)
        {
            throw new ArgumentNullException(nameof(appDataDirectory));
        }

        try
        {
            var logDirectory = ResolveLogDirectory(appDataDirectory);
            Directory.CreateDirectory(logDirectory);
            var logFilePath = Path.Combine(logDirectory, LogFileBaseName);

            var configuration = new LoggerConfiguration()
                .MinimumLevel.Is(MinimumLevel)
                .Enrich.With(new ThreadIdEnricher())
                .WriteTo.Async(
                    sink => sink.File(
                        path: logFilePath,
                        outputTemplate: OutputTemplate,
                        rollingInterval: RollingInterval.Day,
                        fileSizeLimitBytes: FileSizeLimitBytes,
                        rollOnFileSizeLimit: true,
                        retainedFileCountLimit: RetainedFileCountLimit,
                        retainedFileTimeLimit: TimeSpan.FromDays(RetainedFileTimeLimitDays),
                        encoding: new UTF8Encoding(true)),
                    bufferSize: AsyncBufferCapacity);

#if DEBUG
            configuration = configuration.WriteTo.Debug(outputTemplate: OutputTemplate);
#endif

            Log.Logger = configuration.CreateLogger();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[{AppConstants.AppName}] Logging initialisation failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Writes the session delimiter and the PC-configuration report as the first
    /// entries of the session. Safe to call before or after <see cref="Initialize"/>;
    /// it is a no-op when the logger has not been configured.
    /// </summary>
    public static void LogStartupBanner()
    {
        var logger = Log.ForContext(typeof(LogBootstrapper));
        var sessionId = Guid.NewGuid().ToString("N").Substring(0, SessionIdLength);
        logger.Information("===== {App} session {SessionId} starting =====", AppConstants.AppName, sessionId);
        foreach (var entry in EnvironmentReport.Collect())
        {
            logger.Information("  {Key}: {Value}", entry.Key, entry.Value);
        }
    }

    /// <summary>
    /// Flushes and disposes the logger. Must run before every process exit because
    /// <c>Environment.Exit</c> does not drain the async sink. Idempotent.
    /// </summary>
    public static void Shutdown()
    {
        Log.CloseAndFlush();
    }
}
