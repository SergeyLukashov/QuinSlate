using Microsoft.UI.Xaml;
using QuinSlate.Ui.Models;
using System;
using System.Collections.Generic;

namespace QuinSlate.Ui.Services;

/// <summary>
/// Owns the app-wide light/dark/system theme selection. WinUI 3 cannot change
/// <see cref="Application.RequestedTheme"/> after startup, so a runtime theme switch is applied
/// per window by setting <see cref="FrameworkElement.RequestedTheme"/> on each window's content
/// root. This service maps the persisted <see cref="AppTheme"/> to an <see cref="ElementTheme"/>,
/// applies it to every registered root, and persists changes.
/// </summary>
public sealed class ThemeService
{
    private readonly SettingsService settingsService;
    private readonly List<FrameworkElement> roots = new List<FrameworkElement>();

    /// <summary>
    /// Constructs the service over the given <paramref name="settingsService"/>, which is the
    /// backing store for the persisted preference.
    /// </summary>
    public ThemeService(SettingsService settingsService)
    {
        if (settingsService == null)
        {
            throw new ArgumentNullException(nameof(settingsService));
        }

        this.settingsService = settingsService;
    }

    /// <summary>The current theme preference.</summary>
    public AppTheme Current => settingsService.Theme;

    /// <summary>The current preference mapped to a WinUI <see cref="ElementTheme"/>.</summary>
    public ElementTheme CurrentElementTheme => ToElementTheme(settingsService.Theme);

    /// <summary>
    /// Maps an <see cref="AppTheme"/> to a WinUI <see cref="ElementTheme"/>. <see cref="AppTheme.System"/>
    /// maps to <see cref="ElementTheme.Default"/>, which follows the Windows app theme live.
    /// </summary>
    public static ElementTheme ToElementTheme(AppTheme theme)
    {
        if (theme == AppTheme.Light)
        {
            return ElementTheme.Light;
        }

        if (theme == AppTheme.Dark)
        {
            return ElementTheme.Dark;
        }

        return ElementTheme.Default;
    }

    /// <summary>
    /// Registers a window's content root, applies the current theme to it, and tracks it so later
    /// changes flow to it. Idempotent; a registered root that is no longer needed should be passed
    /// to <see cref="Unregister"/>.
    /// </summary>
    public void Register(FrameworkElement root)
    {
        if (root == null)
        {
            return;
        }

        if (roots.Contains(root) == false)
        {
            roots.Add(root);
        }

        root.RequestedTheme = ToElementTheme(settingsService.Theme);
    }

    /// <summary>Stops tracking a previously registered root.</summary>
    public void Unregister(FrameworkElement root)
    {
        if (root == null)
        {
            return;
        }

        roots.Remove(root);
    }

    /// <summary>
    /// Persists <paramref name="theme"/> as the new preference and applies it to every registered
    /// root immediately.
    /// </summary>
    public void Set(AppTheme theme)
    {
        settingsService.Theme = theme;
        ElementTheme mapped = ToElementTheme(theme);
        foreach (FrameworkElement root in roots)
        {
            root.RequestedTheme = mapped;
        }

        _ = settingsService.SaveAsync();
    }
}
