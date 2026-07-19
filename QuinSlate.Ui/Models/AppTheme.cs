namespace QuinSlate.Ui.Models;

/// <summary>
/// The user's manual theme preference. <see cref="System"/> (the default) follows the current
/// Windows app theme live; <see cref="Light"/> and <see cref="Dark"/> pin the app to that theme
/// regardless of the Windows setting.
/// </summary>
public enum AppTheme
{
    /// <summary>Follow the Windows app theme (light or dark), updating live when it changes.</summary>
    System,

    /// <summary>Always use the light theme.</summary>
    Light,

    /// <summary>Always use the dark theme.</summary>
    Dark,
}
