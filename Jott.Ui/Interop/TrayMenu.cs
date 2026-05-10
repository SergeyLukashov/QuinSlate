using System;

namespace Jott.Ui.Interop;

/// <summary>
/// Builds and shows the Win32 tray context menu using <c>CreatePopupMenu</c>
/// and <c>TrackPopupMenu</c>. Returns the selected command identifier (or
/// zero if the menu was dismissed without a selection).
/// </summary>
public sealed class TrayMenu
{
    private const string OpenLabel = "Open";
    private const string LaunchOnStartupLabel = "Launch on startup";
    private const string AboutLabel = "About";
    private const string ExitLabel = "Exit";

    /// <summary>
    /// Displays the tray context menu near the current cursor position and
    /// blocks until the user selects an item or dismisses the menu.
    /// </summary>
    /// <param name="windowHandle">The HWND that owns the menu. Used as the
    /// foreground window so the menu dismisses correctly when focus is lost.</param>
    /// <param name="startupEnabled">Whether the "Launch on startup" item is
    /// rendered with a check mark.</param>
    /// <returns>The selected command identifier, or zero if no item was chosen.</returns>
    public uint Show(IntPtr windowHandle, bool startupEnabled)
    {
        if (windowHandle == IntPtr.Zero)
        {
            throw new ArgumentException("Window handle must not be zero.", nameof(windowHandle));
        }

        NativeMethods.POINT cursor;
        if (NativeMethods.GetCursorPos(out cursor) == false)
        {
            cursor = new NativeMethods.POINT();
        }

        // The standard Win32 fix for the tray-menu-dismiss-on-click bug: the
        // owning window must be the foreground window before TrackPopupMenu,
        // and a final WM_NULL post is required after it returns.
        NativeMethods.SetForegroundWindow(windowHandle);

        var menu = NativeMethods.CreatePopupMenu();
        if (menu == IntPtr.Zero)
        {
            return 0;
        }

        try
        {
            NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, NativeMethods.IDM_OPEN, OpenLabel);
            NativeMethods.AppendMenu(menu, NativeMethods.MF_SEPARATOR, 0, null);

            var startupFlags = NativeMethods.MF_STRING | (startupEnabled ? NativeMethods.MF_CHECKED : NativeMethods.MF_UNCHECKED);
            NativeMethods.AppendMenu(menu, startupFlags, NativeMethods.IDM_LAUNCH_STARTUP, LaunchOnStartupLabel);

            NativeMethods.AppendMenu(menu, NativeMethods.MF_SEPARATOR, 0, null);
            NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, NativeMethods.IDM_ABOUT, AboutLabel);
            NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, NativeMethods.IDM_EXIT, ExitLabel);

            var flags = NativeMethods.TPM_RETURNCMD | NativeMethods.TPM_BOTTOMALIGN | NativeMethods.TPM_RIGHTALIGN;
            var command = NativeMethods.TrackPopupMenu(menu, flags, cursor.X, cursor.Y, 0, windowHandle, IntPtr.Zero);

            NativeMethods.PostMessage(windowHandle, (uint)NativeMethods.WM_NULL, IntPtr.Zero, IntPtr.Zero);

            return (uint)command;
        }
        finally
        {
            NativeMethods.DestroyMenu(menu);
        }
    }
}
