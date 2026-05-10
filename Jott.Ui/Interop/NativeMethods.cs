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

    public const uint MOD_WIN = 0x0008;
    public const uint MOD_NOREPEAT = 0x4000;

    public const int VK_OEM_3 = 0xC0;

    public const int GWLP_WNDPROC = -4;

    public const uint NIM_ADD = 0x00000000;
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
}
