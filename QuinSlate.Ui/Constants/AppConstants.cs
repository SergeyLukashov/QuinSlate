namespace QuinSlate.Ui.Constants;

/// <summary>
/// Contains application-wide constant values used across the codebase.
/// </summary>
public static class AppConstants
{
    /// <summary>
    /// The official name of the application.
    /// </summary>
    public const string AppName = "QuinSlate";

    /// <summary>
    /// The maximum number of characters a single buffer (tab) may hold. Enforced
    /// in the editor by the CodeMirror transaction filter (counted in CRLF form),
    /// and again as the final safeguard before content is written to disk.
    /// <para>
    /// Originally lowered to 50,000 to stay clear of the <c>RichEditBox</c> render
    /// ceiling. The editor is now CodeMirror 6 in a WebView2, which virtualizes
    /// rendering, so the ceiling no longer applies and the cap was raised to
    /// 1,000,000. What remains is a sanity bound on a scratch buffer, not a
    /// rendering limit. See
    /// <c>Docs/Specs/17-EDITOR-CODEMIRROR-MIGRATION.md</c> and
    /// <c>Docs/Investigations/03-RICHEDITBOX-TALL-DOCUMENT-RENDER-CEILING.md</c>.
    /// </para>
    /// </summary>
    public const int MaxBufferLength = 1_000_000;
}
