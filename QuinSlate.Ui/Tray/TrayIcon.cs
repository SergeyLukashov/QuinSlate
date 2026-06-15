using Microsoft.UI.Dispatching;
using QuinSlate.Ui.Interop;
using Serilog;
using System;

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
    private const int HoverPollIntervalMs = 150;
    private const int RearmQuietTicks = 10;

    private readonly IntPtr windowHandle;
    private readonly DispatcherQueue dispatcherQueue;
    private IntPtr iconHandle;
    private string tooltipText = string.Empty;
    private bool added;
    private bool disposed;
    private bool hovering;
    private bool tooltipDisarmed;
    private int ticksOutsideIcon;
    private DispatcherQueueTimer hoverPollTimer;

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
    /// Raised when the mouse pointer enters the tray icon area. Detected via
    /// the first <c>WM_MOUSEMOVE</c> callback of a hover rather than
    /// <c>NIN_POPUPOPEN</c>: explorer does not send <c>NIN_POPUPOPEN</c> while
    /// the standard tooltip is armed (<c>NIF_SHOWTIP</c> set), and the tooltip
    /// must stay armed between hovers — see <see cref="SuppressTooltipOnHover"/>.
    /// </summary>
    public event EventHandler MouseHovered;

    /// <summary>
    /// Raised when the mouse pointer leaves the tray icon area, detected by
    /// polling the cursor position against the icon rectangle.
    /// </summary>
    public event EventHandler MouseLeft;

    /// <summary>
    /// When <c>true</c>, the standard tooltip is withdrawn (<c>NIM_MODIFY</c>
    /// without <c>NIF_SHOWTIP</c>) at hover begin and restored once the pointer
    /// leaves, so a custom hover popup can be shown instead. The tooltip stays
    /// registered with non-empty text the rest of the time. This dynamic arming
    /// exists because statically suppressing the tooltip triggers a Windows 11
    /// explorer bug: after a system flyout (Quick Settings, calendar) is
    /// dismissed, hovering a suppressed-tooltip icon shows an empty tooltip
    /// box. The hover-time withdrawal cancels explorer's pending tooltip before
    /// it is displayed, in both the normal and the post-flyout glitched state.
    /// </summary>
    public bool SuppressTooltipOnHover { get; set; }

    /// <summary>
    /// Creates a tray icon bound to <paramref name="windowHandle"/>. The window
    /// receives notification messages on <see cref="NativeMethods.WM_TRAYICON"/>.
    /// Must be constructed on the UI thread so the hover poll timer can run.
    /// </summary>
    /// <param name="windowHandle">The HWND that hosts the icon's callback messages.</param>
    public TrayIcon(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            throw new ArgumentException("Window handle must not be zero.", nameof(windowHandle));
        }

        this.windowHandle = windowHandle;
        dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    }

    /// <summary>
    /// Adds the icon to the notification area. If <paramref name="iconFilePath"/>
    /// is non-null and resolvable, that file is loaded; otherwise the default
    /// system application icon is used.
    /// </summary>
    /// <param name="iconFilePath">Optional path to a <c>.ico</c> file.</param>
    /// <param name="tooltip">Tooltip text shown on hover.</param>
    /// <returns><c>true</c> if the icon was added.</returns>
    public bool Add(string iconFilePath, string tooltip)
    {
        if (added)
        {
            return true;
        }

        iconHandle = LoadIcon(iconFilePath);
        tooltipText = tooltip ?? string.Empty;

        var data = BuildData(tooltipText);
        data.uFlags = NativeMethods.NIF_MESSAGE | NativeMethods.NIF_ICON | NativeMethods.NIF_TIP;
        if (tooltipText.Length > 0)
        {
            data.uFlags |= NativeMethods.NIF_SHOWTIP;
        }

        data.hIcon = iconHandle;

        var addResult = NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_ADD, ref data);
        if (addResult == false)
        {
            var error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
            Log.ForContext<TrayIcon>().Warning("Shell_NotifyIcon NIM_ADD failed. Win32 error: {Win32Error}", error);
            return false;
        }

        data.uVersion = NativeMethods.NOTIFYICON_VERSION_4;
        NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_SETVERSION, ref data);

        added = true;
        Log.ForContext<TrayIcon>().Information("Tray icon added.");
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
        if (loWord == NativeMethods.WM_MOUSEMOVE)
        {
            OnHoverBegin();
        }
        else if (loWord == NativeMethods.NIN_SELECT)
        {
            LeftClicked?.Invoke(this, EventArgs.Empty);
        }
        else if (loWord == NativeMethods.WM_RBUTTONUP)
        {
            RightClicked?.Invoke(this, EventArgs.Empty);
        }

        return true;
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

        StopHoverPollTimer();
        hovering = false;
        tooltipDisarmed = false;
        ticksOutsideIcon = 0;

        var data = BuildData(null);
        NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_DELETE, ref data);
        added = false;
        Log.ForContext<TrayIcon>().Debug("Tray icon removed.");

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

    private void OnHoverBegin()
    {
        if (hovering || !added || dispatcherQueue == null)
        {
            return;
        }

        hovering = true;
        ticksOutsideIcon = 0;
        if (SuppressTooltipOnHover)
        {
            DisarmTooltip();
        }

        StartHoverPollTimer();
        MouseHovered?.Invoke(this, EventArgs.Empty);
    }

    private void OnHoverEnd()
    {
        hovering = false;
        MouseLeft?.Invoke(this, EventArgs.Empty);
    }

    private void DisarmTooltip()
    {
        var data = BuildData(tooltipText);
        data.uFlags = NativeMethods.NIF_TIP;
        NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_MODIFY, ref data);
        tooltipDisarmed = true;
    }

    private void RearmTooltip()
    {
        if (!tooltipDisarmed || !added)
        {
            return;
        }

        var data = BuildData(tooltipText);
        data.uFlags = NativeMethods.NIF_TIP | NativeMethods.NIF_SHOWTIP;
        NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_MODIFY, ref data);
        tooltipDisarmed = false;
    }

    private void StartHoverPollTimer()
    {
        if (hoverPollTimer == null)
        {
            hoverPollTimer = dispatcherQueue.CreateTimer();
            hoverPollTimer.Interval = TimeSpan.FromMilliseconds(HoverPollIntervalMs);
            hoverPollTimer.Tick += OnHoverPollTimerTick;
        }

        hoverPollTimer.Start();
    }

    private void StopHoverPollTimer()
    {
        if (hoverPollTimer != null)
        {
            hoverPollTimer.Stop();
        }
    }

    private void OnHoverPollTimerTick(DispatcherQueueTimer sender, object args)
    {
        if (NativeMethods.GetCursorPos(out NativeMethods.POINT cursor) && IsCursorOverIcon(cursor))
        {
            ticksOutsideIcon = 0;
            // Keep re-sending the withdrawal while hovering: after some flyout
            // interactions a single withdrawal at hover begin is not enough and
            // explorer still queues its (empty) tooltip; a repeated NIM_MODIFY
            // keeps cancelling it.
            if (SuppressTooltipOnHover && tooltipDisarmed)
            {
                DisarmTooltip();
            }

            return;
        }

        if (hovering)
        {
            OnHoverEnd();
        }

        // Re-arm only after the pointer has stayed away for a quiet period:
        // re-arming immediately after hover end makes explorer pop the now
        // available tooltip for the just-left icon.
        ticksOutsideIcon++;
        if (ticksOutsideIcon < RearmQuietTicks)
        {
            return;
        }

        RearmTooltip();
        StopHoverPollTimer();
        ticksOutsideIcon = 0;
    }

    private bool IsCursorOverIcon(NativeMethods.POINT cursor)
    {
        var identifier = new NativeMethods.NOTIFYICONIDENTIFIER();
        identifier.cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods.NOTIFYICONIDENTIFIER));
        identifier.hWnd = windowHandle;
        identifier.uID = TrayIconId;
        identifier.guidItem = Guid.Empty;

        NativeMethods.Shell_NotifyIconGetRect(ref identifier, out NativeMethods.RECT iconRect);
        return cursor.X >= iconRect.Left && cursor.X <= iconRect.Right
            && cursor.Y >= iconRect.Top && cursor.Y <= iconRect.Bottom;
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
