using System;
using System.Runtime.InteropServices;

namespace Jott.Ui.Interop;

/// <summary>
/// Centralised P/Invoke signatures, structures, and Win32 constants used by Jott.
/// Per architecture rules, this is the only file that may declare <c>[DllImport]</c>.
/// </summary>
internal static class NativeMethods
{
    public const int WM_NULL = 0x0000;
    public const int WM_GETMINMAXINFO = 0x0024;
    public const int WM_HOTKEY = 0x0312;
    public const int WM_LBUTTONUP = 0x0202;
    public const int WM_RBUTTONUP = 0x0205;
    private const int WM_APP = 0x8000;
    public const int WM_TRAYICON = WM_APP + 1;
    public const uint WM_JOTT_ACTIVATE = (uint)(WM_APP + 2);

    public const uint MF_STRING = 0x00000000;
    public const uint MF_SEPARATOR = 0x00000800;
    public const uint MF_CHECKED = 0x00000008;
    public const uint MF_UNCHECKED = 0x00000000;

    public const uint TPM_BOTTOMALIGN = 0x0020;
    public const uint TPM_RIGHTALIGN = 0x0008;
    public const uint TPM_RETURNCMD = 0x0100;

    public const uint IDM_OPEN = 1;
    public const uint IDM_LAUNCH_STARTUP = 2;
    public const uint IDM_ABOUT = 3;
    public const uint IDM_EXIT = 4;
    public const uint IDM_PEEK_PREVIEW = 5;

    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;
    public const uint MOD_NOREPEAT = 0x4000;

    public const int VK_Q = 0x51;

    public const int GWLP_WNDPROC = -4;
    public const int GWL_EXSTYLE = -20;

    public const uint WS_EX_APPWINDOW = 0x00040000;

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

    public const uint MONITOR_DEFAULTTONULL = 0;

    public const uint SPI_GETWORKAREA = 0x0030;

    public const uint NIN_POPUPOPEN = 0x0406;
    public const uint NIN_POPUPCLOSE = 0x0407;

    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;

    public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    public static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);

    public const uint WS_POPUP = 0x80000000;
    public const uint WS_EX_TOPMOST = 0x00000008;
    public const uint WS_EX_TOOLWINDOW = 0x00000080;
    public const uint WS_EX_NOACTIVATE = 0x08000000;

    public const int SW_SHOWNOACTIVATE = 4;
    public const int SW_HIDE = 0;

    public const uint WM_MOUSEMOVE = 0x0200;
    public const uint WM_MOUSEACTIVATE = 0x0021;
    public const uint WM_SHOWWINDOW = 0x0018;
    public const uint WM_TIMER = 0x0113;
    public const uint WM_QUERYENDSESSION = 0x0011;
    public const uint WM_ENDSESSION = 0x0016;

    public const int MA_NOACTIVATE = 3;

    public static readonly IntPtr IDI_APPLICATION = new IntPtr(32512);

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

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool AppendMenu(IntPtr hMenu, uint uFlags, uint uIDNewItem, string lpNewItem);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int TrackPopupMenu(IntPtr hMenu, uint uFlags, int x, int y, int nReserved, IntPtr hWnd, IntPtr prcRect);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

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
    /// Returns the monitor handle for the monitor nearest to <paramref name="pt"/>,
    /// or <see cref="IntPtr.Zero"/> when <c>MONITOR_DEFAULTTONULL</c> is passed and
    /// the point is not on any monitor.
    /// </summary>
    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

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
    /// Describes a Win32 window class. Used with <see cref="RegisterClassEx"/>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public WndProcDelegate lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    /// <summary>
    /// Contains paint information passed to <see cref="BeginPaint"/>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct PAINTSTRUCT
    {
        public IntPtr hdc;
        public bool fErase;
        public RECT rcPaint;
        public bool fRestore;
        public bool fIncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] rgbReserved;
    }

    /// <summary>
    /// Defines the attributes of a logical font for use with <see cref="CreateFontIndirect"/>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct LOGFONT
    {
        public int lfHeight;
        public int lfWidth;
        public int lfEscapement;
        public int lfOrientation;
        public int lfWeight;
        public byte lfItalic;
        public byte lfUnderline;
        public byte lfStrikeOut;
        public byte lfCharSet;
        public byte lfOutPrecision;
        public byte lfClipPrecision;
        public byte lfQuality;
        public byte lfPitchAndFamily;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string lfFaceName;
    }

    /// <summary>
    /// Returns the bounding rectangle of a notification icon in screen coordinates.
    /// </summary>
    [DllImport("shell32.dll")]
    public static extern int Shell_NotifyIconGetRect(ref NOTIFYICONIDENTIFIER identifier, out RECT iconLocation);

    /// <summary>
    /// Registers a window class for use with <see cref="CreateWindowEx"/>.
    /// </summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

    /// <summary>
    /// Creates a window with extended style, class, and other attributes.
    /// </summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr CreateWindowEx(uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    /// <summary>
    /// Destroys the specified window.
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool DestroyWindow(IntPtr hWnd);

    /// <summary>
    /// Shows or hides a window according to <paramref name="nCmdShow"/>.
    /// </summary>
    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    /// <summary>
    /// Changes the position and size of the specified window.
    /// </summary>
    [DllImport("user32.dll")]
    public static extern bool MoveWindow(IntPtr hWnd, int x, int y, int nWidth, int nHeight, bool bRepaint);

    /// <summary>
    /// Prepares the specified window for painting and returns a display context.
    /// </summary>
    [DllImport("user32.dll")]
    public static extern IntPtr BeginPaint(IntPtr hWnd, out PAINTSTRUCT lpPaint);

    /// <summary>
    /// Marks the end of painting for the specified window.
    /// </summary>
    [DllImport("user32.dll")]
    public static extern bool EndPaint(IntPtr hWnd, ref PAINTSTRUCT lpPaint);

    /// <summary>
    /// Creates a logical font from the specified <see cref="LOGFONT"/> structure.
    /// </summary>
    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateFontIndirect(ref LOGFONT lplf);

    /// <summary>
    /// Selects a GDI object into a device context, returning the previously selected object.
    /// </summary>
    [DllImport("gdi32.dll")]
    public static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);

    /// <summary>
    /// Deletes a logical GDI object and frees any associated system resources.
    /// </summary>
    [DllImport("gdi32.dll")]
    public static extern bool DeleteObject(IntPtr ho);

    /// <summary>
    /// Sets the background mix mode for text and hatched brush drawing.
    /// </summary>
    [DllImport("gdi32.dll")]
    public static extern int SetBkMode(IntPtr hdc, int mode);

    /// <summary>
    /// Sets the foreground (text) colour for the specified device context.
    /// </summary>
    [DllImport("gdi32.dll")]
    public static extern uint SetTextColor(IntPtr hdc, uint color);

    /// <summary>
    /// Sets the background colour for the specified device context.
    /// </summary>
    [DllImport("gdi32.dll")]
    public static extern uint SetBkColor(IntPtr hdc, uint color);

    /// <summary>
    /// Draws formatted text in the specified rectangle using the given format flags.
    /// </summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int DrawText(IntPtr hdc, string lpchText, int cchText, ref RECT lprc, uint uFormat);

    /// <summary>
    /// Adds a rectangle to the specified window's update region, scheduling a repaint.
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);

    /// <summary>
    /// Calls the default window procedure for messages not handled by the application.
    /// </summary>
    [DllImport("user32.dll")]
    public static extern IntPtr DefWindowProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    /// <summary>
    /// Fills a rectangle using the specified brush.
    /// </summary>
    [DllImport("user32.dll")]
    public static extern bool FillRect(IntPtr hDC, ref RECT lprc, IntPtr hbr);

    /// <summary>
    /// Creates a logical brush that has the specified solid colour.
    /// </summary>
    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateSolidBrush(uint crColor);

    /// <summary>
    /// Returns the handle to the desktop window.
    /// </summary>
    [DllImport("user32.dll")]
    public static extern IntPtr GetDesktopWindow();

    /// <summary>
    /// Retrieves the dimensions of the bounding rectangle of the specified window.
    /// </summary>
    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    /// <summary>
    /// Retrieves the client-area rectangle of the specified window. The coordinates
    /// are always relative to the client area's upper-left corner (0, 0).
    /// </summary>
    [DllImport("user32.dll")]
    public static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    /// <summary>
    /// Creates a timer that fires <see cref="WM_TIMER"/> to <paramref name="hWnd"/>
    /// every <paramref name="uElapse"/> milliseconds.
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SetTimer(IntPtr hWnd, uint nIDEvent, uint uElapse, IntPtr lpTimerFunc);

    /// <summary>
    /// Destroys the timer created by <see cref="SetTimer"/>.
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool KillTimer(IntPtr hWnd, uint uIDEvent);

    /// <summary>
    /// Returns the system DPI, which equals the DPI of the primary monitor.
    /// </summary>
    [DllImport("user32.dll")]
    public static extern uint GetDpiForSystem();

    /// <summary>
    /// Returns the module handle for the specified module, or the calling process's
    /// module handle when <paramref name="moduleName"/> is <c>null</c>.
    /// </summary>
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr GetModuleHandle(string moduleName);

    /// <summary>
    /// Unregisters a window class created with <see cref="RegisterClassEx"/>.
    /// Must be called after all windows of the class are destroyed.
    /// </summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool UnregisterClass(string lpClassName, IntPtr hInstance);

    /// <summary>
    /// Changes the size, position, and Z order of a window.
    /// Pass <see cref="HWND_TOPMOST"/> or <see cref="HWND_NOTOPMOST"/> as
    /// <paramref name="hWndInsertAfter"/> with <see cref="SWP_NOMOVE"/> |
    /// <see cref="SWP_NOSIZE"/> to set topmost state without moving or resizing.
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
}
