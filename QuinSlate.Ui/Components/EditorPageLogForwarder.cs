using Serilog;
using Serilog.Events;

namespace QuinSlate.Ui.Components;

/// <summary>
/// Forwards "log" messages posted by the editor page (WebEditor/build/src/pageLog.js) into the
/// application's Serilog pipeline, under a source context that identifies the page. The page never
/// puts buffer text into a log message; lengths are nonetheless clamped defensively here.
/// </summary>
internal static class EditorPageLogForwarder
{
    private const string PageSourceContext = "QuinSlate.Ui.WebEditor.EditorPage";
    private const string LevelDebug = "debug";
    private const string LevelInformation = "information";
    private const string LevelWarning = "warning";
    private const string LevelError = "error";
    private const int MaxForwardedLength = 4096;

    /// <summary>Writes one page log entry, appending the page's stack trace when one was sent.</summary>
    public static void Forward(string level, string message, string stack)
    {
        ILogger logger = Log.ForContext(Serilog.Core.Constants.SourceContextPropertyName, PageSourceContext);
        LogEventLevel eventLevel = MapLevel(level);
        if (string.IsNullOrEmpty(stack))
        {
            logger.Write(eventLevel, "{PageMessage}", Clamp(message));
        }
        else
        {
            logger.Write(eventLevel, "{PageMessage}\n{PageStack}", Clamp(message), Clamp(stack));
        }
    }

    /// <summary>Maps the page's level string to a Serilog level; anything unrecognised is Information.</summary>
    public static LogEventLevel MapLevel(string level)
    {
        switch (level)
        {
            case LevelDebug:
                return LogEventLevel.Debug;
            case LevelInformation:
                return LogEventLevel.Information;
            case LevelWarning:
                return LogEventLevel.Warning;
            case LevelError:
                return LogEventLevel.Error;
            default:
                return LogEventLevel.Information;
        }
    }

    /// <summary>Bounds a page-supplied string so a malformed message cannot bloat the log file.</summary>
    public static string Clamp(string text)
    {
        if (text == null)
        {
            return string.Empty;
        }

        return text.Length > MaxForwardedLength ? text.Substring(0, MaxForwardedLength) : text;
    }
}
