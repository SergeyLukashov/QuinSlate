using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using QuinSlate.Ui.Interop;
using QuinSlate.Ui.Models;
using QuinSlate.Ui.Services;
using System;
using Windows.Graphics;
using WinRT.Interop;

namespace QuinSlate.Ui.Tray;

/// <summary>
/// Builds and shows a modernized, native WinUI 3 tray context menu using
/// <see cref="MenuFlyout"/> inside a helper borderless transparent window.
/// </summary>
public sealed class TrayMenu
{
    private const string OpenLabel = "Open QuinSlate";
    private const string LaunchOnStartupLabel = "Launch on startup";
    private const string PeekPreviewLabel = "Peek preview";
    private const string ThemeLabel = "Theme";
    private const string ThemeSystemLabel = "System default";
    private const string ThemeLightLabel = "Light";
    private const string ThemeDarkLabel = "Dark";
    private const string ThemeGroupName = "QuinSlateThemeGroup";
    private const string AboutLabel = "About";
    private const string ExitLabel = "Exit";

    private const string TrayMenuPresenterStyleKey = "TrayMenuFlyoutPresenterStyle";

    private const string SegoeFluentIconsFamily = "Segoe Fluent Icons";

    // Compact design constants matching Windows 11 context menus
    private const double GlyphFontSize = 12.0;
    private const double ItemFontSize = 12.0;
    private const double ItemHeight = 28.0;
    private const double SeparatorMarginY = 2.0;

    private const string OpenGlyph = "\xE8A7";
    private const string ThemeGlyph = "\xE790";
    private const string AboutGlyph = "\xE946";
    private const string ExitGlyph = "\xE711";
    private const string CheckmarkGlyph = "\xE73E";

    private const int OffscreenX = -32000;
    private const int OffscreenY = -32000;

    private Window menuWindow;
    private Action onCloseAction;
    private bool isTornDown;

    /// <summary>
    /// Displays the modernized tray context menu near the current cursor position.
    /// </summary>
    /// <param name="onOpen">Action invoked when the Open item is clicked.</param>
    /// <param name="onToggleStartup">Action invoked when the Launch on Startup item is clicked.</param>
    /// <param name="onTogglePeek">Action invoked when the Peek Preview item is clicked.</param>
    /// <param name="onAbout">Action invoked when the About item is clicked.</param>
    /// <param name="onExit">Action invoked when the Exit item is clicked.</param>
    /// <param name="startupEnabled">Whether the "Launch on startup" item is checked.</param>
    /// <param name="peekEnabled">Whether the "Peek preview" item is checked.</param>
    /// <param name="currentTheme">The theme preference currently in effect, checked in the Theme submenu.</param>
    /// <param name="onSetTheme">Action invoked with the chosen theme when a Theme submenu item is clicked.</param>
    /// <param name="onClose">Action invoked when the tray menu is closed.</param>
    public void Show(
        Action onOpen,
        Action onToggleStartup,
        Action onTogglePeek,
        Action onAbout,
        Action onExit,
        bool startupEnabled,
        bool peekEnabled,
        AppTheme currentTheme,
        Action<AppTheme> onSetTheme,
        Action onClose = null)
    {
        onCloseAction = onClose;
        isTornDown = false;

        NativeMethods.POINT cursor;
        if (NativeMethods.GetCursorPos(out cursor) == false)
        {
            cursor = new NativeMethods.POINT();
        }

        menuWindow = new Window();

        // Use a simple transparent Grid as the root content
        var rootGrid = new Grid
        {
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent)
        };

        // The flyout (and its Theme cascade) resolves its theme against the window's content root,
        // not the ShowAt target, so setting RequestedTheme here honours a manual light/dark override.
        rootGrid.RequestedTheme = ThemeService.ToElementTheme(currentTheme);
        menuWindow.Content = rootGrid;

        IntPtr menuHwnd = WindowNative.GetWindowHandle(menuWindow);
        if (menuHwnd == IntPtr.Zero)
        {
            return;
        }

        // Configure borderless and non-taskbar styles
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(menuHwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        if (appWindow != null)
        {
            var presenter = OverlappedPresenter.CreateForDialog();
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
            presenter.SetBorderAndTitleBar(false, false);
            appWindow.SetPresenter(presenter);

            // Move the helper window completely offscreen to eliminate any visible black box or shadow
            appWindow.MoveAndResize(new RectInt32(OffscreenX, OffscreenY, 1, 1));
        }

        IntPtr exStyle = NativeMethods.GetWindowLongPtr(menuHwnd, NativeMethods.GWL_EXSTYLE);
        long bits = exStyle.ToInt64();
        bits |= NativeMethods.WS_EX_TOOLWINDOW;
        bits &= ~(long)NativeMethods.WS_EX_APPWINDOW;
        NativeMethods.SetWindowLongPtr(menuHwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(bits));

        NativeMethods.SetWindowPos(
            menuHwnd,
            NativeMethods.HWND_TOPMOST,
            0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE);

        var flyout = new MenuFlyout
        {
            MenuFlyoutPresenterStyle = GetPresenterStyle()
        };

        var openItem = new MenuFlyoutItem
        {
            Text = OpenLabel,
            Icon = new FontIcon
            {
                Glyph = OpenGlyph,
                FontFamily = new FontFamily(SegoeFluentIconsFamily),
                FontSize = GlyphFontSize
            }
        };
        openItem.Click += (s, e) =>
        {
            if (onOpen != null)
            {
                onOpen();
            }
        };
        ApplyCompactStyle(openItem);
        flyout.Items.Add(openItem);

        var separator1 = new MenuFlyoutSeparator { Margin = new Thickness(0, SeparatorMarginY, 0, SeparatorMarginY) };
        flyout.Items.Add(separator1);

        // Using standard MenuFlyoutItem with a manual checkmark icon ensures checkmarks
        // align perfectly in the exact same column as the other custom icons.
        var startupItem = new MenuFlyoutItem
        {
            Text = LaunchOnStartupLabel,
            Icon = new FontIcon
            {
                Glyph = startupEnabled ? CheckmarkGlyph : string.Empty,
                FontFamily = new FontFamily(SegoeFluentIconsFamily),
                FontSize = GlyphFontSize
            }
        };
        startupItem.Click += (s, e) =>
        {
            if (onToggleStartup != null)
            {
                onToggleStartup();
            }
        };
        ApplyCompactStyle(startupItem);
        flyout.Items.Add(startupItem);

        var peekItem = new MenuFlyoutItem
        {
            Text = PeekPreviewLabel,
            Icon = new FontIcon
            {
                Glyph = peekEnabled ? CheckmarkGlyph : string.Empty,
                FontFamily = new FontFamily(SegoeFluentIconsFamily),
                FontSize = GlyphFontSize
            }
        };
        peekItem.Click += (s, e) =>
        {
            if (onTogglePeek != null)
            {
                onTogglePeek();
            }
        };
        ApplyCompactStyle(peekItem);
        flyout.Items.Add(peekItem);

        var themeItem = new MenuFlyoutSubItem
        {
            Text = ThemeLabel,
            Icon = new FontIcon
            {
                Glyph = ThemeGlyph,
                FontFamily = new FontFamily(SegoeFluentIconsFamily),
                FontSize = GlyphFontSize
            }
        };
        themeItem.Items.Add(CreateThemeItem(ThemeSystemLabel, AppTheme.System, currentTheme, onSetTheme));
        themeItem.Items.Add(CreateThemeItem(ThemeLightLabel, AppTheme.Light, currentTheme, onSetTheme));
        themeItem.Items.Add(CreateThemeItem(ThemeDarkLabel, AppTheme.Dark, currentTheme, onSetTheme));
        ApplyCompactStyle(themeItem);
        flyout.Items.Add(themeItem);

        var separator2 = new MenuFlyoutSeparator { Margin = new Thickness(0, SeparatorMarginY, 0, SeparatorMarginY) };
        flyout.Items.Add(separator2);

        var aboutItem = new MenuFlyoutItem
        {
            Text = AboutLabel,
            Icon = new FontIcon
            {
                Glyph = AboutGlyph,
                FontFamily = new FontFamily(SegoeFluentIconsFamily),
                FontSize = GlyphFontSize
            }
        };
        aboutItem.Click += (s, e) =>
        {
            if (onAbout != null)
            {
                onAbout();
            }
        };
        ApplyCompactStyle(aboutItem);
        flyout.Items.Add(aboutItem);

        var exitItem = new MenuFlyoutItem
        {
            Text = ExitLabel,
            Icon = new FontIcon
            {
                Glyph = ExitGlyph,
                FontFamily = new FontFamily(SegoeFluentIconsFamily),
                FontSize = GlyphFontSize
            }
        };
        exitItem.Click += (s, e) =>
        {
            if (onExit != null)
            {
                onExit();
            }
        };
        ApplyCompactStyle(exitItem);
        flyout.Items.Add(exitItem);

        // When the flyout is closed, clean up the helper window
        flyout.Closed += (s, e) =>
        {
            Teardown();
        };

        // Crucial: Only show the flyout after the root Grid has been fully loaded into the visual tree.
        // Calling ShowAt synchronously before layout has completed causes a fatal COMException in WinUI 3.
        rootGrid.Loaded += (s, e) =>
        {
            float scale = NativeMethods.GetDpiForWindow(menuHwnd) / 96.0f;
            if (scale <= 0.0f)
            {
                scale = 1.0f;
            }

            // Calculate the logical offset to project the flyout back onto the actual cursor position
            double offsetX = cursor.X - OffscreenX;
            double offsetY = cursor.Y - OffscreenY;
            double logicalX = offsetX / scale;
            double logicalY = offsetY / scale;

            var options = new FlyoutShowOptions
            {
                ShowMode = FlyoutShowMode.Standard,
                Position = new Windows.Foundation.Point(logicalX, logicalY),
                Placement = FlyoutPlacementMode.TopEdgeAlignedLeft
            };
            flyout.ShowAt(rootGrid, options);
        };

        // Safeguard: Automatically close and teardown if the helper window loses focus/deactivates
        menuWindow.Activated += (s, e) =>
        {
            if (e.WindowActivationState == WindowActivationState.Deactivated)
            {
                Teardown();
            }
        };

        // Activate the helper window, which triggers XAML loading and fires the Loaded event
        menuWindow.Activate();
        NativeMethods.SetForegroundWindow(menuHwnd);
    }

    private RadioMenuFlyoutItem CreateThemeItem(string text, AppTheme value, AppTheme currentTheme, Action<AppTheme> onSetTheme)
    {
        // No Icon: RadioMenuFlyoutItem's own selection bullet occupies the reserved icon column, so
        // it lines up with the checkmark and glyphs of the sibling items. Setting IsChecked
        // programmatically does not raise Click, so onSetTheme only ever fires on a real click.
        var item = new RadioMenuFlyoutItem
        {
            Text = text,
            GroupName = ThemeGroupName,
            IsChecked = value == currentTheme
        };
        item.Click += (s, e) =>
        {
            if (onSetTheme != null)
            {
                onSetTheme(value);
            }
        };
        ApplyCompactStyle(item);
        return item;
    }

    private void ApplyCompactStyle(Control item)
    {
        item.Height = ItemHeight;
        item.MinHeight = ItemHeight;
        item.FontSize = ItemFontSize;
        item.Padding = new Thickness(8, 0, 8, 0);
    }

    private static Style GetPresenterStyle()
    {
        if (Application.Current != null &&
            Application.Current.Resources.TryGetValue(TrayMenuPresenterStyleKey, out object obj) &&
            obj is Style style)
        {
            return style;
        }
        return null;
    }

    private void Teardown()
    {
        if (isTornDown)
        {
            return;
        }
        isTornDown = true;

        if (menuWindow != null)
        {
            menuWindow.Close();
            menuWindow = null;
        }

        onCloseAction?.Invoke();
    }
}
