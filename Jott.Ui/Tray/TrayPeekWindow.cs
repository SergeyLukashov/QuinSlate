using Jott.Ui.Interop;
using Jott.Ui.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System;
using System.Runtime.InteropServices;
using Windows.Graphics;
using WinRT.Interop;
using Buffer = Jott.Ui.Models.Buffer;

namespace Jott.Ui.Tray;

/// <summary>
/// A borderless, non-activating WinUI 3 popup window that renders a two-column
/// preview of every buffer when the user hovers the tray icon. Replacing the
/// previous Win32 GDI implementation so that color emoji in tab titles render
/// correctly via WinUI 3 TextBlock.
/// </summary>
public sealed class TrayPeekWindow : IDisposable
{
    private const int BufferCount = Buffer.MaxIndex - Buffer.MinIndex + 1;

    private const int LogicalWindowWidth = 340;
    private const int LogicalWindowHeight = (BufferCount * LogicalLineHeight) + (LogicalPaddingY * 2);
    private const int LogicalLineHeight = 22;
    private const int LogicalPaddingY = 8;
    private const int GapAboveTray = 8;
    private const int HoverCheckIntervalMs = 150;
    private const int HoverHitExpansionLogical = 12;

    private Window peekWindow;
    private TrayPeekPanel panel;
    private AppWindow appWindow;
    private IntPtr peekHwnd;
    private NativeMethods.WndProcDelegate subclassDelegate;
    private IntPtr originalWndProc;

    private DispatcherTimer hoverTimer;
    private bool isVisible;
    private bool disposed;
    private float dpiScale = 1.0f;
    private IntPtr storedTrayHwnd;
    private uint storedTrayIconId;

    /// <summary>
    /// Creates the peek window container. The underlying WinUI 3 <see cref="Window"/>
    /// is created lazily on the first call to <see cref="Show"/>.
    /// </summary>
    public TrayPeekWindow()
    {
    }

    /// <summary>
    /// Builds the preview rows from <paramref name="bufferService"/> and
    /// <paramref name="settingsService"/>, positions the window above (or below)
    /// the tray icon, and shows it without activating the calling application.
    /// </summary>
    /// <param name="bufferService">Supplies in-memory buffer content.</param>
    /// <param name="settingsService">Supplies tab emoji and title metadata.</param>
    /// <param name="trayHwnd">The HWND that owns the tray icon.</param>
    /// <param name="trayIconId">The numeric ID passed to <c>Shell_NotifyIcon</c>.</param>
    public void Show(BufferService bufferService, SettingsService settingsService, IntPtr trayHwnd, uint trayIconId)
    {
        if (bufferService == null || settingsService == null)
        {
            return;
        }

        storedTrayHwnd = trayHwnd;
        storedTrayIconId = trayIconId;
        dpiScale = NativeMethods.GetDpiForSystem() / 96.0f;

        TrayPeekRow[] rows = TrayPeekRowBuilder.Build(bufferService, settingsService);
        EnsureWindowCreated();

        if (peekHwnd == IntPtr.Zero || isVisible)
        {
            return;
        }

        panel.SetRows(rows);

        NativeMethods.RECT iconRect = QueryIconRect(trayHwnd, trayIconId);
        (int windowWidth, int windowHeight) = CalculatePhysicalWindowSize();
        (int x, int y) = CalculatePosition(iconRect, windowWidth, windowHeight);

        appWindow.MoveAndResize(new RectInt32(x, y, windowWidth, windowHeight));

        NativeMethods.ShowWindow(peekHwnd, NativeMethods.SW_SHOWNOACTIVATE);
        isVisible = true;

        StartHoverTimer();
    }

    /// <summary>
    /// Hides the peek window and stops the hover-check timer.
    /// </summary>
    public void Hide()
    {
        if (peekHwnd == IntPtr.Zero || isVisible == false)
        {
            return;
        }

        StopHoverTimer();
        NativeMethods.ShowWindow(peekHwnd, NativeMethods.SW_HIDE);
        isVisible = false;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;

        StopHoverTimer();

        if (peekWindow != null)
        {
            if (originalWndProc != IntPtr.Zero && peekHwnd != IntPtr.Zero)
            {
                NativeMethods.SetWindowLongPtr(peekHwnd, NativeMethods.GWLP_WNDPROC, originalWndProc);
                originalWndProc = IntPtr.Zero;
            }

            subclassDelegate = null;
            peekWindow.Close();
            peekWindow = null;
            panel = null;
            appWindow = null;
            peekHwnd = IntPtr.Zero;
        }
    }

    private void EnsureWindowCreated()
    {
        if (peekWindow != null)
        {
            return;
        }

        peekWindow = new Window();

        panel = new TrayPeekPanel();
        peekWindow.Content = panel;

        peekHwnd = WindowNative.GetWindowHandle(peekWindow);
        if (peekHwnd == IntPtr.Zero)
        {
            return;
        }

        ApplyNonActivatingStyles();
        ConfigurePresenter();
        SubclassWndProc();

        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(peekHwnd);
        appWindow = AppWindow.GetFromWindowId(windowId);
    }

    private void ApplyNonActivatingStyles()
    {
        IntPtr exStyle = NativeMethods.GetWindowLongPtr(peekHwnd, NativeMethods.GWL_EXSTYLE);
        long bits = exStyle.ToInt64();
        bits |= NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW;
        bits &= ~(long)NativeMethods.WS_EX_APPWINDOW;
        NativeMethods.SetWindowLongPtr(peekHwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(bits));

        NativeMethods.SetWindowPos(
            peekHwnd,
            NativeMethods.HWND_TOPMOST,
            0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE);
    }

    private void ConfigurePresenter()
    {
        if (appWindow == null)
        {
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(peekHwnd);
            appWindow = AppWindow.GetFromWindowId(windowId);
        }

        if (appWindow == null)
        {
            return;
        }

        var presenter = OverlappedPresenter.CreateForDialog();
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        presenter.SetBorderAndTitleBar(false, false);
        appWindow.SetPresenter(presenter);
    }

    private void SubclassWndProc()
    {
        subclassDelegate = PeekWndProc;
        IntPtr newProcPtr = Marshal.GetFunctionPointerForDelegate(subclassDelegate);
        originalWndProc = NativeMethods.SetWindowLongPtr(peekHwnd, NativeMethods.GWLP_WNDPROC, newProcPtr);
    }

    private IntPtr PeekWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == NativeMethods.WM_MOUSEACTIVATE)
        {
            return new IntPtr(NativeMethods.MA_NOACTIVATE);
        }

        return NativeMethods.CallWindowProc(originalWndProc, hWnd, msg, wParam, lParam);
    }

    private void StartHoverTimer()
    {
        if (hoverTimer != null)
        {
            return;
        }

        hoverTimer = new DispatcherTimer();
        hoverTimer.Interval = TimeSpan.FromMilliseconds(HoverCheckIntervalMs);
        hoverTimer.Tick += OnHoverTimerTick;
        hoverTimer.Start();
    }

    private void StopHoverTimer()
    {
        if (hoverTimer == null)
        {
            return;
        }

        hoverTimer.Stop();
        hoverTimer.Tick -= OnHoverTimerTick;
        hoverTimer = null;
    }

    private void OnHoverTimerTick(object sender, object e)
    {
        CheckAndHideIfCursorLeft();
    }

    private void CheckAndHideIfCursorLeft()
    {
        NativeMethods.POINT cursor;
        if (NativeMethods.GetCursorPos(out cursor) == false)
        {
            return;
        }

        int scaledExpansion = (int)(HoverHitExpansionLogical * dpiScale);
        NativeMethods.RECT iconRect = QueryIconRect(storedTrayHwnd, storedTrayIconId);
        bool overIcon = cursor.X >= iconRect.Left - scaledExpansion
                     && cursor.X <= iconRect.Right + scaledExpansion
                     && cursor.Y >= iconRect.Top - scaledExpansion
                     && cursor.Y <= iconRect.Bottom + scaledExpansion;

        if (overIcon)
        {
            return;
        }

        if (peekHwnd != IntPtr.Zero)
        {
            NativeMethods.RECT peekRect;
            if (NativeMethods.GetWindowRect(peekHwnd, out peekRect))
            {
                bool overPeek = cursor.X >= peekRect.Left && cursor.X <= peekRect.Right
                             && cursor.Y >= peekRect.Top && cursor.Y <= peekRect.Bottom;
                if (overPeek)
                {
                    return;
                }
            }
        }

        Hide();
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

    private (int Width, int Height) CalculatePhysicalWindowSize()
    {
        int width = (int)(LogicalWindowWidth * dpiScale);
        int height = (int)(LogicalWindowHeight * dpiScale);
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
}
