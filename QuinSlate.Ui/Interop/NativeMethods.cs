using System;
using System.Runtime.InteropServices;

namespace QuinSlate.Ui.Interop;

/// <summary>
/// Centralised P/Invoke signatures, structures, and Win32 constants used by QuinSlate.
/// Per architecture rules, this is the only file that may declare <c>[DllImport]</c>.
/// </summary>
internal static class NativeMethods
{
    /// <summary>HRESULT for a successful call (<c>S_OK</c>).</summary>
    public const int S_OK = 0;

    public const int WM_GETMINMAXINFO = 0x0024;
    public const int WM_HOTKEY = 0x0312;
    public const int WM_MOUSEMOVE = 0x0200;
    public const int WM_RBUTTONUP = 0x0205;
    private const int WM_APP = 0x8000;
    public const int WM_TRAYICON = WM_APP + 1;
    public const uint WM_QUINSLATE_ACTIVATE = (uint)(WM_APP + 2);

    /// <summary>
    /// Name of the message the shell broadcasts to every top-level window when the
    /// taskbar is created or recreated (for example after Explorer restarts). The
    /// numeric id is process-specific and must be resolved at runtime with
    /// <see cref="RegisterWindowMessage"/>; notification-area icons are discarded on
    /// taskbar recreation and must be re-added in response to this message.
    /// </summary>
    public const string TaskbarCreatedMessage = "TaskbarCreated";

    public const int VK_Q = 0x51;

    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_NOREPEAT = 0x4000;

    public const int GWLP_WNDPROC = -4;
    public const int GWLP_HWNDPARENT = -8;
    public const int GWL_EXSTYLE = -20;

    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;

    public const uint NIM_ADD = 0x00000000;
    public const uint NIM_MODIFY = 0x00000001;
    public const uint NIM_DELETE = 0x00000002;
    public const uint NIM_SETVERSION = 0x00000004;

    public const uint NIF_MESSAGE = 0x00000001;
    public const uint NIF_ICON = 0x00000002;
    public const uint NIF_TIP = 0x00000004;
    public const uint NIF_SHOWTIP = 0x00000080;

    public const uint NOTIFYICON_VERSION_4 = 4;

    public const uint IMAGE_ICON = 1;
    public const uint LR_LOADFROMFILE = 0x00000010;

    public const int SM_CXSMICON = 49;
    public const int SM_CYSMICON = 50;

    public const uint SPI_GETWORKAREA = 0x0030;

    public const uint NIN_SELECT = 0x0400;

    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOACTIVATE = 0x0010;

    public static readonly IntPtr HWND_TOP = new IntPtr(0);
    public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    public static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);

    public const uint WS_EX_APPWINDOW = 0x00040000;
    public const uint WS_EX_TOOLWINDOW = 0x00000080;
    public const uint WS_EX_NOACTIVATE = 0x08000000;
    public const uint WS_EX_LAYERED = 0x00080000;

    public const uint LWA_ALPHA = 0x00000002;

    public const int SW_SHOWNOACTIVATE = 4;
    public const int SW_HIDE = 0;

    public const uint WM_MOUSEACTIVATE = 0x0021;
    public const uint WM_QUERYENDSESSION = 0x0011;
    public const uint WM_ENDSESSION = 0x0016;

    public const int MA_NOACTIVATE = 3;

    public const uint WM_ACTIVATE = 0x0006;
    public const int WA_INACTIVE = 0;

    public const uint WM_SIZE = 0x0005;
    public const int SIZE_MINIMIZED = 1;

    public const uint WM_SYSCOMMAND = 0x0112;
    public const int SC_MINIMIZE = 0xF020;

    public const uint WM_ERASEBKGND = 0x0014;

    public static readonly IntPtr IDI_APPLICATION = new IntPtr(32512);

    public static readonly IntPtr IDC_ARROW = new IntPtr(32512);

    /// <summary>
    /// Defines a rectangle by its left, top, right, and bottom edges.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    /// <summary>
    /// Contains information about a display monitor, including the monitor bounds
    /// and the working area (excluding taskbar and docked toolbars).
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    /// <summary>
    /// Callback invoked by <see cref="EnumDisplayMonitors"/> for each monitor in the virtual screen.
    /// Return <c>true</c> to continue enumeration; return <c>false</c> to stop.
    /// </summary>
    public delegate bool EnumDisplayMonitorsDelegate(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct NOTIFYICONDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;

        public uint dwState;
        public uint dwStateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;

        public uint uVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;

        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    public delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    /// <summary>
    /// Error returned by <see cref="GetCurrentPackageFullName"/> when the calling
    /// process has no package identity (i.e. the app is running unpackaged).
    /// </summary>
    public const int APPMODEL_ERROR_NO_PACKAGE = 15700;

    /// <summary>
    /// Retrieves the full package name of the calling process. Returns
    /// <see cref="APPMODEL_ERROR_NO_PACKAGE"/> when the process has no package
    /// identity, which is how an unpackaged launch is detected at runtime.
    /// </summary>
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetCurrentPackageFullName(ref int packageFullNameLength, System.Text.StringBuilder packageFullName);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr SetWindowLong32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    public static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
    {
        if (IntPtr.Size == 8)
        {
            return SetWindowLongPtr64(hWnd, nIndex, dwNewLong);
        }

        return SetWindowLong32(hWnd, nIndex, dwNewLong);
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetWindowLong32(IntPtr hWnd, int nIndex);

    /// <summary>
    /// Retrieves information about a window. On 64-bit processes uses
    /// <c>GetWindowLongPtrW</c>; on 32-bit processes falls back to
    /// <c>GetWindowLongW</c>.
    /// </summary>
    public static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
    {
        if (IntPtr.Size == 8)
        {
            return GetWindowLongPtr64(hWnd, nIndex);
        }

        return GetWindowLong32(hWnd, nIndex);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr LoadImage(IntPtr hInst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool DestroyIcon(IntPtr hIcon);

    /// <summary>
    /// Loads a cursor resource. For a system cursor such as <see cref="IDC_ARROW"/>,
    /// pass <see cref="IntPtr.Zero"/> as <paramref name="hInstance"/>; the returned handle
    /// is shared and OS-cached, so it must not be passed to <c>DestroyCursor</c>.
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr LoadCursor(IntPtr hInstance, IntPtr lpCursorName);

    /// <summary>
    /// Sets the cursor shape for the current state. Windows resets the cursor on the next
    /// <c>WM_SETCURSOR</c> (the next mouse move), so the set shape persists only until then.
    /// </summary>
    [DllImport("user32.dll")]
    public static extern IntPtr SetCursor(IntPtr hCursor);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int nIndex);

    /// <summary>System metric index: primary screen width in pixels.</summary>
    public const int SM_CXSCREEN = 0;

    /// <summary>System metric index: primary screen height in pixels.</summary>
    public const int SM_CYSCREEN = 1;

    /// <summary>System metric index: number of display monitors.</summary>
    public const int SM_CMONITORS = 80;

    /// <summary>
    /// Memory status returned by <see cref="GlobalMemoryStatusEx"/>. Only
    /// <see cref="ullTotalPhys"/> is consumed; the remaining fields are required
    /// for the correct struct layout.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    /// <summary>
    /// Retrieves information about the system's physical and virtual memory.
    /// <see cref="MEMORYSTATUSEX.dwLength"/> must be set to the struct size before the call.
    /// </summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    /// <summary>
    /// Returns the handle of the foreground window (the one the user is currently working with),
    /// or <see cref="IntPtr.Zero"/> if there is none.
    /// </summary>
    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    /// <summary>
    /// Returns the id of the thread that created the given window (and, via the out parameter, its
    /// process id). Used to attach input queues so the foreground can be reassigned reliably.
    /// </summary>
    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    /// <summary>
    /// Attaches or detaches the input processing of one thread to another. Briefly attaching to the
    /// current foreground thread lets <see cref="SetForegroundWindow"/> succeed past the foreground
    /// lock; the attachment must be released again immediately afterwards.
    /// </summary>
    [DllImport("user32.dll")]
    public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    /// <summary>
    /// Returns the id of the calling thread.
    /// </summary>
    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    /// <summary>
    /// Enables or disables mouse and keyboard input to the given window. Disabling the
    /// owner window while a dialog is open gives classic Win32 modal behaviour: clicks on
    /// the owner are rejected and the dialog stays in front. Returns the window's previous
    /// enabled state.
    /// </summary>
    [DllImport("user32.dll")]
    public static extern bool EnableWindow(IntPtr hWnd, bool bEnable);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

    /// <summary>
    /// Defines a new window message that is guaranteed to be unique throughout the
    /// system. Registering the same string from any process yields the same value, so
    /// it is the documented way to obtain the id of the shell's
    /// <see cref="TaskbarCreatedMessage"/> broadcast. Returns 0 on failure.
    /// </summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern uint RegisterWindowMessage(string lpString);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    /// <summary>
    /// Contains information about a window's maximised size and position, and its
    /// minimum and maximum tracking sizes. Passed via <c>lParam</c> of
    /// <c>WM_GETMINMAXINFO</c>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    /// <summary>
    /// Returns the dots-per-inch (DPI) value for the monitor associated with
    /// <paramref name="hWnd"/>. Returns 96 if the window handle is invalid.
    /// </summary>
    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr hWnd);

    /// <summary>
    /// Retrieves the coordinates of a window's client area, in client-area pixels
    /// (the top-left corner is always (0, 0)).
    /// </summary>
    [DllImport("user32.dll")]
    public static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    /// <summary>
    /// Fills the rectangle <paramref name="lprc"/> on device context <paramref name="hDC"/>
    /// using the GDI brush <paramref name="hbr"/>. Used to paint the window background during
    /// <see cref="WM_ERASEBKGND"/> so the bare Win32 surface never shows white before the WinUI
    /// compositor presents its first frame.
    /// </summary>
    [DllImport("user32.dll")]
    public static extern int FillRect(IntPtr hDC, ref RECT lprc, IntPtr hbr);

    /// <summary>
    /// Creates a solid GDI brush of the given color. <paramref name="color"/> is a COLORREF
    /// (<c>0x00BBGGRR</c>). The brush must be released with <see cref="DeleteObject"/>.
    /// </summary>
    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateSolidBrush(uint color);

    /// <summary>
    /// Deletes a GDI object (such as a brush created by <see cref="CreateSolidBrush"/>),
    /// freeing the resources it uses.
    /// </summary>
    [DllImport("gdi32.dll")]
    public static extern bool DeleteObject(IntPtr hObject);

    /// <summary>
    /// Enumerates display monitors that intersect the given clipping rectangle,
    /// invoking <paramref name="lpfnEnum"/> for each monitor found.
    /// Pass <see cref="IntPtr.Zero"/> for both <c>hdc</c> and <c>lprcClip</c> to
    /// enumerate all monitors in the virtual screen.
    /// </summary>
    [DllImport("user32.dll")]
    public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, EnumDisplayMonitorsDelegate lpfnEnum, IntPtr dwData);

    /// <summary>
    /// Retrieves information about a display monitor. The caller must set
    /// <see cref="MONITORINFO.cbSize"/> before calling, or the function returns <c>false</c>.
    /// </summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    /// <summary>
    /// Retrieves or sets a system-wide parameter. When <paramref name="uiAction"/> is
    /// <see cref="SPI_GETWORKAREA"/>, <paramref name="pvParam"/> receives the working area
    /// rectangle of the primary monitor in physical pixels.
    /// </summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref RECT pvParam, uint fWinIni);

    /// <summary>
    /// Identifies a notification icon by its host window handle, icon ID, and
    /// optional GUID. Used with <see cref="Shell_NotifyIconGetRect"/>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct NOTIFYICONIDENTIFIER
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public Guid guidItem;
    }

    /// <summary>
    /// Returns the bounding rectangle of a notification icon in screen coordinates.
    /// </summary>
    [DllImport("shell32.dll")]
    public static extern int Shell_NotifyIconGetRect(ref NOTIFYICONIDENTIFIER identifier, out RECT iconLocation);

    /// <summary>
    /// Shows or hides a window according to <paramref name="nCmdShow"/>.
    /// </summary>
    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    /// <summary>
    /// Sets the opacity and transparency color key of a layered window.
    /// </summary>
    /// <param name="hwnd">A handle to the layered window.</param>
    /// <param name="crKey">A COLORREF structure that specifies the transparency color key.</param>
    /// <param name="bAlpha">Alpha value used to describe the opacity of the layered window.</param>
    /// <param name="dwFlags">Actions to take.</param>
    /// <returns><c>true</c> if the function succeeds.</returns>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

    /// <summary>
    /// Retrieves the dimensions of the bounding rectangle of the specified window.
    /// </summary>
    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    /// <summary>
    /// Sets a Desktop Window Manager attribute. See <see cref="SetRoundedCornerPreference"/>
    /// for the rounded-corner use case.
    /// </summary>
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    /// <summary>
    /// Forces Windows 11 rounded corners on a borderless window so the window itself owns the
    /// single rounded outline. The hosted content must not draw its own rounded border: a second
    /// rounded contour never aligns exactly with the window's and reads as a detached edge.
    /// </summary>
    public static void SetRoundedCornerPreference(IntPtr hwnd)
    {
        int preference = DWMWCP_ROUND;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
    }

    /// <summary>
    /// Returns the system DPI, which equals the DPI of the primary monitor.
    /// </summary>
    [DllImport("user32.dll")]
    public static extern uint GetDpiForSystem();

    /// <summary>
    /// Changes the size, position, and Z order of a window.
    /// Pass <see cref="HWND_TOPMOST"/> or <see cref="HWND_NOTOPMOST"/> as
    /// <paramref name="hWndInsertAfter"/> with <see cref="SWP_NOMOVE"/> |
    /// <see cref="SWP_NOSIZE"/> to set topmost state without moving or resizing.
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
}
