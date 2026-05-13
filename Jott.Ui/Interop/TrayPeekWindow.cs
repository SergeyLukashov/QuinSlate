using Jott.Ui.Services;
using System;
using System.Runtime.InteropServices;
using Buffer = Jott.Ui.Models.Buffer;

namespace Jott.Ui.Interop;

/// <summary>
/// A borderless, non-interactive Win32 popup window that renders a one-line
/// preview of every buffer when the user hovers the tray icon.
/// </summary>
public sealed class TrayPeekWindow : IDisposable
{
    private const string WindowClassName = "JottPeek";
    private const string WindowTitle = "";

    private const int BufferCount = Buffer.MaxIndex - Buffer.MinIndex + 1;
    private const string EmptyLabel = "(empty)";
    private const string LinePrefixFormat = "{0} · ";

    private const int PaddingX = 12;
    private const int PaddingY = 8;
    private const int LineHeight = 20;
    private const int GapAboveTray = 8;
    private const uint HoverTimerId = 1;
    private const uint HoverCheckIntervalMs = 150;
    private const int HoverHitExpansion = 12;

    private const int TransparentMode = 1;

    private const uint BackgroundColor = 0x00282828;
    private const uint TextColor = 0x00E0E0E0;
    private const uint MutedTextColor = 0x00888888;

    private const string FontFaceName = "Segoe UI";
    private const int FontHeight = -13;
    private const int FontWeightNormal = 400;

    // classRegistered and wndProcDelegate are both static so the delegate is
    // never garbage collected while the class registration is alive. A single
    // registration shared across instances is intentional: all TrayPeekWindow
    // instances share the same class name and the same WndProc entry point,
    // which dispatches per-HWND via the instance pointer stored in the field.
    private static bool classRegistered;
    private static NativeMethods.WndProcDelegate staticWndProcDelegate;

    private IntPtr hwnd;
    private string[] lines;
    private bool[] lineIsEmpty;
    private bool disposed;
    private bool isVisible;
    private float dpiScale = 1.0f;
    private IntPtr storedTrayHwnd;
    private uint storedTrayIconId;

    /// <summary>
    /// Creates the peek window. The underlying Win32 window is created lazily on
    /// the first call to <see cref="Show"/>.
    /// </summary>
    public TrayPeekWindow()
    {
        lines = new string[BufferCount];
        lineIsEmpty = new bool[BufferCount];
    }

    /// <summary>
    /// Builds the preview lines from <paramref name="bufferService"/>, positions
    /// the window above (or below) the tray icon, and shows it.
    /// </summary>
    /// <param name="bufferService">The buffer service supplying in-memory content.</param>
    /// <param name="trayHwnd">The HWND that owns the tray icon.</param>
    /// <param name="trayIconId">The numeric ID passed to <c>Shell_NotifyIcon</c>.</param>
    public void Show(BufferService bufferService, IntPtr trayHwnd, uint trayIconId)
    {
        if (bufferService == null)
        {
            return;
        }

        storedTrayHwnd = trayHwnd;
        storedTrayIconId = trayIconId;
        dpiScale = NativeMethods.GetDpiForSystem() / 96.0f;

        BuildLines(bufferService);
        EnsureWindowCreated();

        if (hwnd == IntPtr.Zero || isVisible)
        {
            return;
        }

        var iconRect = QueryIconRect(trayHwnd, trayIconId);
        var windowSize = CalculateWindowSize();
        var position = CalculatePosition(iconRect, windowSize.Width, windowSize.Height);

        NativeMethods.MoveWindow(hwnd, position.X, position.Y, windowSize.Width, windowSize.Height, false);
        NativeMethods.InvalidateRect(hwnd, IntPtr.Zero, true);
        NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOWNOACTIVATE);
        isVisible = true;
    }

    /// <summary>
    /// Hides the peek window.
    /// </summary>
    public void Hide()
    {
        if (hwnd == IntPtr.Zero || isVisible == false)
        {
            return;
        }

        NativeMethods.ShowWindow(hwnd, NativeMethods.SW_HIDE);
        isVisible = false;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        if (hwnd != IntPtr.Zero)
        {
            NativeMethods.DestroyWindow(hwnd);
            hwnd = IntPtr.Zero;
        }

        if (classRegistered)
        {
            NativeMethods.UnregisterClass(WindowClassName, NativeMethods.GetModuleHandle(null));
            classRegistered = false;
        }

        disposed = true;
    }

    private void EnsureWindowCreated()
    {
        if (hwnd != IntPtr.Zero)
        {
            return;
        }

        EnsureClassRegistered();

        uint exStyle = NativeMethods.WS_EX_TOPMOST | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE;
        uint style = NativeMethods.WS_POPUP;

        hwnd = NativeMethods.CreateWindowEx(
            exStyle,
            WindowClassName,
            WindowTitle,
            style,
            0, 0, 100, 100,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero);
    }

    private void EnsureClassRegistered()
    {
        if (classRegistered)
        {
            return;
        }

        staticWndProcDelegate = WndProc;

        var wc = new NativeMethods.WNDCLASSEX();
        wc.cbSize = (uint)Marshal.SizeOf(typeof(NativeMethods.WNDCLASSEX));
        wc.style = 0;
        wc.lpfnWndProc = staticWndProcDelegate;
        wc.cbClsExtra = 0;
        wc.cbWndExtra = 0;
        wc.hInstance = NativeMethods.GetModuleHandle(null);
        wc.hIcon = IntPtr.Zero;
        wc.hCursor = IntPtr.Zero;
        wc.hbrBackground = IntPtr.Zero;
        wc.lpszMenuName = null;
        wc.lpszClassName = WindowClassName;
        wc.hIconSm = IntPtr.Zero;

        NativeMethods.RegisterClassEx(ref wc);
        classRegistered = true;
    }

    private void BuildLines(BufferService bufferService)
    {
        for (int i = Buffer.MinIndex; i <= Buffer.MaxIndex; i++)
        {
            int slot = i - Buffer.MinIndex;
            var buffer = bufferService.GetBuffer(i);
            string content = buffer != null ? buffer.Content : string.Empty;

            if (string.IsNullOrEmpty(content))
            {
                lines[slot] = string.Format(LinePrefixFormat, i) + EmptyLabel;
                lineIsEmpty[slot] = true;
            }
            else
            {
                string firstLine = content;
                int newlineIndex = content.IndexOfAny(new[] { '\r', '\n' });
                if (newlineIndex >= 0)
                {
                    firstLine = content.Substring(0, newlineIndex);
                }

                lines[slot] = string.Format(LinePrefixFormat, i) + firstLine;
                lineIsEmpty[slot] = false;
            }
        }
    }

    private NativeMethods.RECT QueryIconRect(IntPtr trayHwnd, uint trayIconId)
    {
        var identifier = new NativeMethods.NOTIFYICONIDENTIFIER();
        identifier.cbSize = (uint)Marshal.SizeOf(typeof(NativeMethods.NOTIFYICONIDENTIFIER));
        identifier.hWnd = trayHwnd;
        identifier.uID = trayIconId;
        identifier.guidItem = Guid.Empty;

        NativeMethods.RECT iconRect;
        NativeMethods.Shell_NotifyIconGetRect(ref identifier, out iconRect);
        return iconRect;
    }

    private (int Width, int Height) CalculateWindowSize()
    {
        int width = (int)(260 * dpiScale);
        int height = (int)(((BufferCount * LineHeight) + (PaddingY * 2)) * dpiScale);
        return (width, height);
    }

    private (int X, int Y) CalculatePosition(NativeMethods.RECT iconRect, int windowWidth, int windowHeight)
    {
        int scaledGap = (int)(GapAboveTray * dpiScale);
        int iconCentreX = (iconRect.Left + iconRect.Right) / 2;
        int x = iconCentreX - (windowWidth / 2);
        int y = iconRect.Top - scaledGap - windowHeight;

        var workArea = new NativeMethods.RECT();
        NativeMethods.SystemParametersInfo(NativeMethods.SPI_GETWORKAREA, 0, ref workArea, 0);

        if (y < workArea.Top)
        {
            y = iconRect.Bottom + scaledGap;
        }

        if (x + windowWidth > workArea.Right)
        {
            x = workArea.Right - windowWidth;
        }

        if (x < workArea.Left)
        {
            x = workArea.Left;
        }

        return (x, y);
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        const uint WM_PAINT = 0x000F;
        const uint WM_ERASEBKGND = 0x0014;

        if (msg == WM_ERASEBKGND)
        {
            return new IntPtr(1);
        }

        if (msg == WM_PAINT)
        {
            Paint(hWnd);
            return IntPtr.Zero;
        }

        if (msg == NativeMethods.WM_SHOWWINDOW)
        {
            bool isShowing = wParam != IntPtr.Zero;
            if (isShowing)
            {
                NativeMethods.SetTimer(hWnd, HoverTimerId, HoverCheckIntervalMs, IntPtr.Zero);
            }
            else
            {
                NativeMethods.KillTimer(hWnd, HoverTimerId);
            }
        }

        if (msg == NativeMethods.WM_TIMER && (uint)wParam.ToInt64() == HoverTimerId)
        {
            CheckAndHideIfCursorLeft(hWnd);
            return IntPtr.Zero;
        }

        return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private void CheckAndHideIfCursorLeft(IntPtr hWnd)
    {
        NativeMethods.POINT cursor;
        if (NativeMethods.GetCursorPos(out cursor) == false)
        {
            return;
        }

        int scaledHitExpansion = (int)(HoverHitExpansion * dpiScale);
        var iconRect = QueryIconRect(storedTrayHwnd, storedTrayIconId);
        bool overIcon = cursor.X >= iconRect.Left - scaledHitExpansion
                     && cursor.X <= iconRect.Right + scaledHitExpansion
                     && cursor.Y >= iconRect.Top - scaledHitExpansion
                     && cursor.Y <= iconRect.Bottom + scaledHitExpansion;

        if (overIcon)
        {
            return;
        }

        NativeMethods.RECT peekRect;
        if (NativeMethods.GetWindowRect(hWnd, out peekRect))
        {
            bool overPeek = cursor.X >= peekRect.Left && cursor.X <= peekRect.Right
                         && cursor.Y >= peekRect.Top && cursor.Y <= peekRect.Bottom;
            if (overPeek)
            {
                return;
            }
        }

        Hide();
    }

    private void Paint(IntPtr hWnd)
    {
        NativeMethods.PAINTSTRUCT ps;
        var hdc = NativeMethods.BeginPaint(hWnd, out ps);

        if (hdc == IntPtr.Zero)
        {
            return;
        }

        try
        {
            NativeMethods.GetClientRect(hWnd, out NativeMethods.RECT drawRect);

            var bgBrush = NativeMethods.CreateSolidBrush(BackgroundColor);
            NativeMethods.FillRect(hdc, ref drawRect, bgBrush);
            NativeMethods.DeleteObject(bgBrush);

            int scaledPaddingX = (int)(PaddingX * dpiScale);
            int scaledPaddingY = (int)(PaddingY * dpiScale);
            int scaledLineHeight = (int)(LineHeight * dpiScale);
            int scaledFontHeight = (int)(FontHeight * dpiScale);

            var lf = new NativeMethods.LOGFONT();
            lf.lfHeight = scaledFontHeight;
            lf.lfWidth = 0;
            lf.lfEscapement = 0;
            lf.lfOrientation = 0;
            lf.lfWeight = FontWeightNormal;
            lf.lfItalic = 0;
            lf.lfUnderline = 0;
            lf.lfStrikeOut = 0;
            lf.lfCharSet = 0;
            lf.lfOutPrecision = 0;
            lf.lfClipPrecision = 0;
            lf.lfQuality = 0;
            lf.lfPitchAndFamily = 0;
            lf.lfFaceName = FontFaceName;

            var hFont = NativeMethods.CreateFontIndirect(ref lf);
            var oldFont = NativeMethods.SelectObject(hdc, hFont);

            NativeMethods.SetBkMode(hdc, TransparentMode);
            NativeMethods.SetBkColor(hdc, BackgroundColor);

            const uint DT_LEFT = 0x00000000;
            const uint DT_VCENTER = 0x00000004;
            const uint DT_SINGLELINE = 0x00000020;
            const uint DT_NOPREFIX = 0x00000800;
            const uint DT_END_ELLIPSIS = 0x00008000;

            for (int i = 0; i < BufferCount; i++)
            {
                if (lines[i] == null)
                {
                    continue;
                }

                uint textColor = lineIsEmpty[i] ? MutedTextColor : TextColor;
                NativeMethods.SetTextColor(hdc, textColor);

                int top = scaledPaddingY + (i * scaledLineHeight);
                var lineRect = new NativeMethods.RECT
                {
                    Left = scaledPaddingX,
                    Top = top,
                    Right = drawRect.Right - scaledPaddingX,
                    Bottom = top + scaledLineHeight
                };

                NativeMethods.DrawText(hdc, lines[i], -1, ref lineRect, DT_LEFT | DT_VCENTER | DT_SINGLELINE | DT_NOPREFIX | DT_END_ELLIPSIS);
            }

            NativeMethods.SelectObject(hdc, oldFont);
            NativeMethods.DeleteObject(hFont);
        }
        finally
        {
            NativeMethods.EndPaint(hWnd, ref ps);
        }
    }
}
