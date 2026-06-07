using System;
using Windows.UI;

namespace QuinSlate.Ui.Interop;

/// <summary>
/// Owns the solid GDI brush used to paint a window's client area in response to
/// <see cref="NativeMethods.WM_ERASEBKGND"/>.
/// </summary>
/// <remarks>
/// When a WinUI 3 window first becomes visible the OS erases its client area with the window
/// class brush (white) before the WinUI compositor presents its first frame — a blank flash that
/// no <c>SystemBackdrop</c> can hide, because the erase happens below the entire WinUI composition.
/// Filling that erase with a theme-matched colour instead removes the flash. The brush is recreated
/// when the theme changes so the fill always matches the current light/dark background.
/// </remarks>
public sealed class WindowBackgroundBrush : IDisposable
{
    private readonly uint lightColor;
    private readonly uint darkColor;
    private IntPtr brush;
    private bool hasTheme;
    private bool isDark;

    /// <summary>
    /// Creates a background brush that fills with <paramref name="lightColor"/> in the light theme
    /// and <paramref name="darkColor"/> in the dark theme. Call <see cref="SetTheme"/> before use.
    /// </summary>
    public WindowBackgroundBrush(Color lightColor, Color darkColor)
    {
        this.lightColor = ToColorRef(lightColor);
        this.darkColor = ToColorRef(darkColor);
    }

    /// <summary>
    /// Selects the colour for the given theme, recreating the underlying GDI brush only when the
    /// theme actually changes. Must be called at least once before <see cref="Erase"/> paints.
    /// </summary>
    public void SetTheme(bool useDarkTheme)
    {
        if (hasTheme && useDarkTheme == isDark)
        {
            return;
        }

        hasTheme = true;
        isDark = useDarkTheme;

        IntPtr replacement = NativeMethods.CreateSolidBrush(useDarkTheme ? darkColor : lightColor);
        IntPtr previous = brush;
        brush = replacement;
        if (previous != IntPtr.Zero)
        {
            NativeMethods.DeleteObject(previous);
        }
    }

    /// <summary>
    /// Paints the client area of <paramref name="windowHandle"/> onto <paramref name="deviceContext"/>
    /// (the HDC supplied with <see cref="NativeMethods.WM_ERASEBKGND"/>). Returns <c>true</c> when it
    /// painted, so the caller can report the message as handled and suppress the default white erase.
    /// </summary>
    public bool Erase(IntPtr windowHandle, IntPtr deviceContext)
    {
        if (brush == IntPtr.Zero)
        {
            return false;
        }

        NativeMethods.GetClientRect(windowHandle, out NativeMethods.RECT clientRect);
        NativeMethods.FillRect(deviceContext, ref clientRect, brush);
        return true;
    }

    /// <summary>Releases the underlying GDI brush.</summary>
    public void Dispose()
    {
        if (brush != IntPtr.Zero)
        {
            NativeMethods.DeleteObject(brush);
            brush = IntPtr.Zero;
        }
    }

    private static uint ToColorRef(Color color)
    {
        return (uint)(color.R | (color.G << 8) | (color.B << 16));
    }
}
