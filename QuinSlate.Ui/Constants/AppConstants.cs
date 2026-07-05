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
    /// The maximum number of characters a single buffer (tab) may hold. This
    /// caps the editor's <c>MaxLength</c>, bounds paste truncation, and acts as
    /// the final safeguard before content is written to disk.
    /// <para>
    /// Kept at 50,000 to stay well clear of the <c>RichEditBox</c> render
    /// ceiling: that control does not virtualize and stops painting glyphs past a
    /// fixed rendered height (~260k px), while still keeping the characters in the
    /// document and selectable. A larger cap let realistic buffers grow tall
    /// enough to hit that ceiling, leaving the tail of long tabs invisible. See
    /// <c>Docs/Investigations/03-RICHEDITBOX-TALL-DOCUMENT-RENDER-CEILING.md</c>.
    /// </para>
    /// </summary>
    public const int MaxBufferLength = 50_000;
}
