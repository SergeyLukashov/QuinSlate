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
    /// </summary>
    public const int MaxBufferLength = 100_000;
}
