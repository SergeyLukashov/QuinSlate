using QuinSlate.Ui.Services;
using Serilog;
using System;
using System.Threading.Tasks;

namespace QuinSlate.Ui.Components;

/// <summary>
/// Opens a link the editor page reports a click on. The page never opens anything itself; the host
/// re-validates the scheme and hands the URI to the shell.
/// </summary>
/// <remarks>
/// The href is buffer text: it is never logged, here or on failure — only the outcome is.
/// </remarks>
internal static class EditorLinkLauncher
{
    /// <summary>Launches <paramref name="href"/> if it is an absolute http, https, or mailto URI.</summary>
    public static void Launch(string href)
    {
        if (LinkService.TryCreateLaunchUri(href, out Uri uri) == false)
        {
            Log.ForContext<EditorHost>().Warning(
                "Declined a link from the editor page: not an absolute http/https/mailto URI.");
            return;
        }

        _ = LaunchAsync(uri);
    }

    private static async Task LaunchAsync(Uri uri)
    {
        try
        {
            bool launched = await Windows.System.Launcher.LaunchUriAsync(uri);
            if (launched == false)
            {
                Log.ForContext<EditorHost>().Warning(
                    "The shell declined to launch a link from the editor ({Scheme}).", uri.Scheme);
            }
        }
        catch (Exception ex)
        {
            // Deliberately not the exception object: its message could echo the URI — buffer text —
            // into the log. The type and HRESULT identify a shell failure without it, and the stack
            // of a fire-and-forget launch says nothing this message does not.
            Log.ForContext<EditorHost>().Warning(
                "Failed to launch a link from the editor ({Scheme}): {ExceptionType} 0x{HResult:X8}.",
                uri.Scheme,
                ex.GetType().Name,
                ex.HResult);
        }
    }
}
