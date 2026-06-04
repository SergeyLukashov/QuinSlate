using QuinSlate.Ui.Interop;
using System;
using System.Diagnostics;

namespace QuinSlate.Ui.Tray;

/// <summary>
/// Win32 <c>Shell_NotifyIcon</c> wrapper. WinUI 3 has no native tray API so all
/// behaviour here is P/Invoke. The icon must be removed via <c>NIM_DELETE</c>
/// before its host window is destroyed; otherwise a ghost icon remains until
/// Explorer is restarted.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private const uint TrayIconId = 1;

    private readonly IntPtr windowHandle;
    private IntPtr iconHandle;
    private bool added;
    private bool disposed;

    /// <summary>
    /// Raised when the user left-clicks the tray icon.
    /// </summary>
    public event EventHandler LeftClicked;

    /// <summary>
    /// Raised when the user right-clicks the tray icon. Handlers should
    /// surface the tray context menu.
    /// </summary>
    public event EventHandler RightClicked;

    /// <summary>
    /// Raised when the mouse pointer enters the tray icon area
    /// (<c>NIN_POPUPOPEN</c>).
    /// </summary>
    public event EventHandler MouseHovered;

    /// <summary>
    /// Raised when the mouse pointer leaves the tray icon area
    /// (<c>NIN_POPUPCLOSE</c>).
    /// </summary>
    public event EventHandler MouseLeft;

    /// <summary>
    /// Creates a tray icon bound to <paramref name="windowHandle"/>. The window
    /// receives notification messages on <see cref="NativeMethods.WM_TRAYICON"/>.
    /// </summary>
    /// <param name="windowHandle">The HWND that hosts the icon's callback messages.</param>
    public TrayIcon(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            throw new ArgumentException("Window handle must not be zero.", nameof(windowHandle));
        }

        this.windowHandle = windowHandle;
    }

    /// <summary>
    /// Adds the icon to the notification area. If <paramref name="iconFilePath"/>
    /// is non-null and resolvable, that file is loaded; otherwise the default
    /// system application icon is used.
    /// </summary>
    /// <param name="iconFilePath">Optional path to a <c>.ico</c> file.</param>
    /// <param name="tooltip">Optional tooltip text shown on hover.</param>
    /// <returns><c>true</c> if the icon was added.</returns>
    public bool Add(string iconFilePath, string tooltip)
    {
        if (added)
        {
            return true;
        }

        iconHandle = LoadIcon(iconFilePath);

        var data = BuildData(tooltip);
        data.uFlags = NativeMethods.NIF_MESSAGE | NativeMethods.NIF_ICON | NativeMethods.NIF_TIP;
        if (!string.IsNullOrEmpty(tooltip))
        {
            data.uFlags |= NativeMethods.NIF_SHOWTIP;
        }

        data.hIcon = iconHandle;

        var addResult = NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_ADD, ref data);
        if (addResult == false)
        {
            var error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
            Debug.WriteLine($"[{AppConstants.AppName}] Shell_NotifyIcon NIM_ADD failed. Win32 error: {error}");
            return false;
        }

        data.uVersion = NativeMethods.NOTIFYICON_VERSION_4;
        NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_SETVERSION, ref data);

        added = true;
        return true;
    }

    /// <summary>
    /// Routes a window message to the tray icon. Returns <c>true</c> if the
    /// message was a tray notification (and was handled).
    /// </summary>
    /// <param name="msg">The Win32 message id.</param>
    /// <param name="wParam">wParam from the WndProc.</param>
    /// <param name="lParam">lParam from the WndProc.</param>
    public bool HandleMessage(uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg != NativeMethods.WM_TRAYICON)
        {
            return false;
        }

        var loWord = (uint)(lParam.ToInt64() & 0xFFFF);
        if (loWord == NativeMethods.NIN_SELECT)
        {
            LeftClicked?.Invoke(this, EventArgs.Empty);
        }
        else if (loWord == NativeMethods.WM_RBUTTONUP)
        {
            RightClicked?.Invoke(this, EventArgs.Empty);
        }
        else if (loWord == NativeMethods.NIN_POPUPOPEN)
        {
            MouseHovered?.Invoke(this, EventArgs.Empty);
        }
        else if (loWord == NativeMethods.NIN_POPUPCLOSE)
        {
            MouseLeft?.Invoke(this, EventArgs.Empty);
        }

        return true;
    }

    /// <summary>
    /// Updates the tooltip text shown on hover. Pass an empty string to suppress
    /// the tooltip (e.g. when the peek preview window is enabled instead).
    /// </summary>
    /// <param name="tooltip">The new tooltip text, or empty to disable.</param>
    public void SetTooltip(string tooltip)
    {
        if (!added)
        {
            return;
        }

        var data = BuildData(tooltip);
        data.uFlags = NativeMethods.NIF_TIP;
        if (!string.IsNullOrEmpty(tooltip))
        {
            data.uFlags |= NativeMethods.NIF_SHOWTIP;
        }

        NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_MODIFY, ref data);
    }

    /// <summary>
    /// Removes the icon from the notification area. Safe to call multiple times.
    /// Must run before the host window is destroyed to avoid a ghost icon.
    /// </summary>
    public void Remove()
    {
        if (added == false)
        {
            return;
        }

        var data = BuildData(null);
        NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_DELETE, ref data);
        added = false;

        if (iconHandle != IntPtr.Zero)
        {
            NativeMethods.DestroyIcon(iconHandle);
            iconHandle = IntPtr.Zero;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        Remove();
        disposed = true;
    }

    private NativeMethods.NOTIFYICONDATA BuildData(string tooltip)
    {
        var data = new NativeMethods.NOTIFYICONDATA();
        data.cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods.NOTIFYICONDATA));
        data.hWnd = windowHandle;
        data.uID = TrayIconId;
        data.uCallbackMessage = (uint)NativeMethods.WM_TRAYICON;
        data.szTip = tooltip ?? string.Empty;
        data.szInfo = string.Empty;
        data.szInfoTitle = string.Empty;
        return data;
    }

    private static IntPtr LoadIcon(string iconFilePath)
    {
        var width = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSMICON);
        var height = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYSMICON);

        if (string.IsNullOrEmpty(iconFilePath) == false && System.IO.File.Exists(iconFilePath))
        {
            var loaded = NativeMethods.LoadImage(IntPtr.Zero, iconFilePath, NativeMethods.IMAGE_ICON, width, height, NativeMethods.LR_LOADFROMFILE);
            if (loaded != IntPtr.Zero)
            {
                return loaded;
            }
        }

        return NativeMethods.LoadIcon(IntPtr.Zero, NativeMethods.IDI_APPLICATION);
    }
}
