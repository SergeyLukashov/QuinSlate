using System;

namespace QuinSlate.Ui.Services;

/// <summary>
/// Validates a link the editor page asks the host to open (see
/// <c>Docs/Specs/21-LINKS.md</c>). The page detects links with the same three schemes, but the
/// href arrives over the WebView2 bridge as buffer text, so it is re-checked here: the shell is
/// only ever handed an absolute URI carrying a scheme this app is willing to launch.
/// </summary>
internal static class LinkService
{
    private static readonly string[] LaunchableSchemes =
    {
        Uri.UriSchemeHttp,
        Uri.UriSchemeHttps,
        Uri.UriSchemeMailto,
    };

    /// <summary>
    /// Attempts to turn <paramref name="href"/> into a URI safe to hand to the shell.
    /// </summary>
    /// <param name="href">The link text the editor page reported.</param>
    /// <param name="uri">The parsed absolute URI when it is launchable; otherwise <c>null</c>.</param>
    /// <returns>
    /// <c>true</c> when <paramref name="href"/> is an absolute URI whose scheme is http, https, or
    /// mailto; <c>false</c> for anything else — a relative reference, an unparsable string, or a
    /// scheme (file, javascript, ms-settings, …) this app does not launch.
    /// </returns>
    public static bool TryCreateLaunchUri(string href, out Uri uri)
    {
        uri = null;

        if (string.IsNullOrWhiteSpace(href))
        {
            return false;
        }

        if (Uri.TryCreate(href, UriKind.Absolute, out Uri parsed) == false)
        {
            return false;
        }

        // Uri lower-cases the scheme it parses, so an ordinal match covers "HTTPS://x" too.
        if (Array.IndexOf(LaunchableSchemes, parsed.Scheme) < 0)
        {
            return false;
        }

        uri = parsed;
        return true;
    }
}
