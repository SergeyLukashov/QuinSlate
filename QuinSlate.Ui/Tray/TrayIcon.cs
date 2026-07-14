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

    /// <summary>
    /// Stable identity for the notification-area icon. Windows 11 keys an icon's
    /// promotion state (shown on the taskbar vs. hidden in the overflow flyout)
    /// on the icon's identity. Without a GUID that identity is derived from the
    /// executable path, which changes on every MSIX/Store update (each version
    /// installs into a new versioned <c>WindowsApps</c> folder), so the shell
    /// treats the post-update icon as new and demotes it to the overflow. A fixed
    /// <c>guidItem</c> (with <see cref="NativeMethods.NIF_GUID"/>) is path
    /// independent, so the user's chosen position survives updates. This value
    /// must never change once shipped. Exposed so the peek window can resolve the
    /// icon's rectangle by the same GUID (once <see cref="NativeMethods.NIF_GUID"/>
    /// is used, the shell no longer resolves the icon by its <c>(HWND, uID)</c>).
    /// </summary>
    public static readonly Guid TrayIconGuid = new Guid("6f9a2c14-8d3b-4e7a-9c21-5b0e1f2a7d84");

    private readonly IntPtr windowHandle;
    private readonly DispatcherQueue dispatcherQueue;
    private IntPtr iconHandle;
    private string tooltipText = string.Empty;
    private bool added;
    private bool disposed;
    private bool hovering;
    private bool tooltipDisarmed;
    private int ticksOutsideIcon;
    private bool iconRectUnavailable;
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
    /// Raised when the user clicks the body of a balloon notification shown via
    /// <see cref="ShowBalloon"/> (the <c>NIN_BALLOONUSERCLICK</c> callback).
    /// Dismissing the balloon by its close button does not raise this.
    /// </summary>
    public event EventHandler BalloonClicked;

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

        if (RegisterIcon() == false)
        {
            var error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
            Log.ForContext<TrayIcon>().Warning("Shell_NotifyIcon NIM_ADD failed. Win32 error: {Win32Error}", error);
            return false;
        }

        added = true;
        Log.ForContext<TrayIcon>().Information("Tray icon added.");
        return true;
    }

    /// <summary>
    /// Re-adds the icon after the shell has recreated the taskbar (the
    /// <c>TaskbarCreated</c> broadcast, raised for example when Explorer restarts).
    /// Explorer discards every registered icon when it restarts and stops delivering
    /// the icon's callback messages, so without re-adding, the icon silently
    /// disappears and hover, click, and peek stop working until the app is relaunched.
    /// The icon handle and tooltip text are reused; the hover/tooltip state machine is
    /// reset because the previous Explorer instance's notion of it is gone. No-op if
    /// the icon was never added or has already been removed.
    /// </summary>
    public void Reregister()
    {
        if (added == false || disposed)
        {
            return;
        }

        StopHoverPollTimer();
        hovering = false;
        tooltipDisarmed = false;
        ticksOutsideIcon = 0;

        if (RegisterIcon() == false)
        {
            var error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
            Log.ForContext<TrayIcon>().Warning("Shell_NotifyIcon NIM_ADD failed on taskbar re-registration. Win32 error: {Win32Error}", error);
            return;
        }

        Log.ForContext<TrayIcon>().Information("Tray icon re-registered after taskbar recreation.");
    }

    /// <summary>
    /// Sends the <c>NIM_ADD</c> for the current icon and tooltip state and promotes the
    /// icon to <see cref="NativeMethods.NOTIFYICON_VERSION_4"/>. Shared by the initial
    /// add and the taskbar re-registration path. Returns <c>false</c> if the
    /// <c>NIM_ADD</c> fails; the caller reads the Win32 error.
    /// </summary>
    private bool RegisterIcon()
    {
        var data = BuildData(tooltipText);
        data.uFlags = NativeMethods.NIF_MESSAGE | NativeMethods.NIF_ICON | NativeMethods.NIF_TIP | NativeMethods.NIF_GUID;
        if (tooltipText.Length > 0)
        {
            data.uFlags |= NativeMethods.NIF_SHOWTIP;
        }

        data.hIcon = iconHandle;

        if (NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_ADD, ref data) == false)
        {
            // A GUID-identified NIM_ADD fails when the GUID is still registered — to
            // the previous package version's path after a Store update, or leaked by
            // a prior instance that never removed its icon. Clear the stale
            // registration by GUID and retry the add once.
            DeleteByGuid();
            if (NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_ADD, ref data) == false)
            {
                return false;
            }
        }

        data.uVersion = NativeMethods.NOTIFYICON_VERSION_4;
        NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_SETVERSION, ref data);
        return true;
    }

    /// <summary>
    /// Removes any notification-area registration for <see cref="TrayIconGuid"/>
    /// by GUID alone. Used to clear a stale registration before retrying a failed
    /// add; safe to call when no such registration exists.
    /// </summary>
    private void DeleteByGuid()
    {
        var data = BuildData(null);
        data.uFlags = NativeMethods.NIF_GUID;
        NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_DELETE, ref data);
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
        else if (loWord == NativeMethods.NIN_BALLOONUSERCLICK)
        {
            BalloonClicked?.Invoke(this, EventArgs.Empty);
        }

        return true;
    }

    /// <summary>
    /// Shows a balloon notification anchored to the tray icon (<c>NIM_MODIFY</c>
    /// with <c>NIF_INFO</c>). On Windows 10/11 the shell surfaces this through the
    /// notification system while keeping it associated with the icon. Clicking the
    /// balloon body raises <see cref="BalloonClicked"/>. No-op if the icon has not
    /// been added or has been removed.
    /// </summary>
    /// <param name="title">The balloon title (bold first line).</param>
    /// <param name="text">The balloon body text.</param>
    public void ShowBalloon(string title, string text)
    {
        if (added == false || disposed)
        {
            return;
        }

        var data = BuildData(tooltipText);
        data.uFlags = NativeMethods.NIF_INFO | NativeMethods.NIF_GUID;
        data.szInfoTitle = title ?? string.Empty;
        data.szInfo = text ?? string.Empty;
        data.dwInfoFlags = NativeMethods.NIIF_INFO;

        if (NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_MODIFY, ref data) == false)
        {
            var error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
            Log.ForContext<TrayIcon>().Warning("Shell_NotifyIcon NIM_MODIFY (balloon) failed. Win32 error: {Win32Error}", error);
            return;
        }

        Log.ForContext<TrayIcon>().Information("Tray balloon notification shown.");
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
        data.uFlags = NativeMethods.NIF_GUID;
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
        Log.ForContext<TrayIcon>().Debug("Tray hover begin.");
        MouseHovered?.Invoke(this, EventArgs.Empty);
    }

    private void OnHoverEnd()
    {
        hovering = false;
        Log.ForContext<TrayIcon>().Debug("Tray hover end.");
        MouseLeft?.Invoke(this, EventArgs.Empty);
    }

    private void DisarmTooltip()
    {
        var data = BuildData(tooltipText);
        data.uFlags = NativeMethods.NIF_TIP | NativeMethods.NIF_GUID;
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
        data.uFlags = NativeMethods.NIF_TIP | NativeMethods.NIF_SHOWTIP | NativeMethods.NIF_GUID;
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
        identifier.guidItem = TrayIconGuid;

        int hr = NativeMethods.Shell_NotifyIconGetRect(ref identifier, out NativeMethods.RECT iconRect);
        if (hr != NativeMethods.S_OK)
        {
            // The query failed, so iconRect is unreliable — the API may leave the out value
            // unwritten (uninitialized stack memory). Trusting it can wedge hovering true
            // forever: a garbage rect that happens to bracket the cursor makes the poll believe
            // the pointer never leaves, so OnHoverEnd never fires and every later hover
            // short-circuits — peek and tooltip silently die while the icon stays visible. Fail
            // safe toward "off icon" so the hover state always recovers.
            if (iconRectUnavailable == false)
            {
                iconRectUnavailable = true;
                Log.ForContext<TrayIcon>().Warning("Shell_NotifyIconGetRect failed (HRESULT 0x{HResult:X8}); treating pointer as off-icon until it recovers.", hr);
            }
            return false;
        }

        if (iconRectUnavailable)
        {
            iconRectUnavailable = false;
            Log.ForContext<TrayIcon>().Information("Shell_NotifyIconGetRect recovered.");
        }

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
        data.guidItem = TrayIconGuid;
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
