using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using QuinSlate.Ui.Interop;
using QuinSlate.Ui.Services;
using Serilog;
using System;
using System.Runtime.InteropServices;
using Windows.Graphics;
using WinRT.Interop;
using Buffer = QuinSlate.Ui.Models.Buffer;

namespace QuinSlate.Ui.Tray;

/// <summary>
/// A non-activating WinUI 3 popup window — a thin frame and no title bar — that renders a
/// two-column preview of every buffer when the user hovers the tray icon. Replacing the
/// previous Win32 GDI implementation so that color emoji in tab titles render
/// correctly via WinUI 3 TextBlock.
/// </summary>
public sealed class TrayPeekWindow : IDisposable
{
    private const int BufferCount = Buffer.MaxIndex - Buffer.MinIndex + 1;

    private const int LogicalWindowWidth = 340;
    private const int LogicalHeaderHeight = 26;
    private const int LogicalSeparatorHeight = 12;
    private const int LogicalLineHeight = 26;
    private const int LogicalPaddingY = 6;
    private const int LogicalFooterSeparatorHeight = 12;
    private const int LogicalFooterHeight = 26;
    private const int LogicalWindowHeight = (BufferCount * LogicalLineHeight) + (LogicalPaddingY * 2) + LogicalHeaderHeight + LogicalSeparatorHeight + LogicalFooterSeparatorHeight + LogicalFooterHeight;
    private const int GapAboveTray = 8;
    private const int HoverCheckIntervalMs = 150;
    private const int HoverHitExpansionLogical = 12;
    private const int ShowDelayMs = 600;

    private Window peekWindow;
    private TrayPeekPanel panel;
    private AppWindow appWindow;
    private IntPtr peekHwnd;
    private NativeMethods.WndProcDelegate subclassDelegate;
    private IntPtr originalWndProc;

    private DispatcherTimer hoverTimer;
    private DispatcherTimer showDelayTimer;
    private DispatcherTimer animationTimer;
    private int animationStep;
    private int targetX;
    private int targetY;
    private int windowWidth;
    private int windowHeight;
    private const int AnimationSteps = 6;
    private const int AnimationIntervalMs = 15;
    private const int StartYOffsetLogical = 16;
    private bool isVisible;
    private bool disposed;
    private bool iconRectUnavailable;
    private float dpiScale = 1.0f;
    private IntPtr storedTrayHwnd;
    private uint storedTrayIconId;

    private BufferService storedBufferService;
    private SettingsService storedSettingsService;

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
    /// A short hover delay is introduced to match standard OS tooltip behavior.
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

        storedBufferService = bufferService;
        storedSettingsService = settingsService;
        storedTrayHwnd = trayHwnd;
        storedTrayIconId = trayIconId;
        dpiScale = NativeMethods.GetDpiForSystem() / 96.0f;

        if (isVisible)
        {
            return;
        }

        StartShowDelayTimer();
    }

    /// <summary>
    /// Hides the peek window and stops the hover-check timer.
    /// </summary>
    public void Hide()
    {
        StopShowDelayTimer();

        if (peekHwnd == IntPtr.Zero || isVisible == false)
        {
            return;
        }

        StopAnimationTimer();
        StopHoverTimer();
        NativeMethods.ShowWindow(peekHwnd, NativeMethods.SW_HIDE);
        isVisible = false;
        Log.ForContext<TrayPeekWindow>().Debug("Peek hidden.");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;

        StopShowDelayTimer();
        StopAnimationTimer();
        StopHoverTimer();

        storedBufferService = null;
        storedSettingsService = null;

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
        NativeMethods.SetRoundedCornerPreference(peekHwnd);
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

        // Keep the real window frame (border yes, title bar no) exactly as MainWindow does. The
        // frame's 1px edge is what DWM draws — and it is themed dark in dark mode. A frameless
        // window (hasBorder: false) plus a forced rounded-corner mask leaves the bare window
        // backdrop exposed along the straight edges between the masked corners, which reads as
        // intermittent near-white strips on a dark surface; the frame covers that boundary.
        presenter.SetBorderAndTitleBar(true, false);
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

    private void StartShowDelayTimer()
    {
        if (showDelayTimer != null)
        {
            return;
        }

        showDelayTimer = new DispatcherTimer();
        showDelayTimer.Interval = TimeSpan.FromMilliseconds(ShowDelayMs);
        showDelayTimer.Tick += OnShowDelayTimerTick;
        showDelayTimer.Start();
    }

    private void StopShowDelayTimer()
    {
        if (showDelayTimer == null)
        {
            return;
        }

        showDelayTimer.Stop();
        showDelayTimer.Tick -= OnShowDelayTimerTick;
        showDelayTimer = null;
    }

    private void OnShowDelayTimerTick(object sender, object e)
    {
        StopShowDelayTimer();
        ExecuteShow();
    }

    private void ExecuteShow()
    {
        if (storedBufferService == null || storedSettingsService == null || disposed)
        {
            return;
        }

        TrayPeekRow[] rows = TrayPeekRowBuilder.Build(storedBufferService, storedSettingsService);
        EnsureWindowCreated();

        if (peekHwnd == IntPtr.Zero || isVisible)
        {
            return;
        }

        if (TryQueryIconRect(storedTrayHwnd, storedTrayIconId, out NativeMethods.RECT iconRect) == false)
        {
            // Without a reliable icon rect the window cannot be placed next to the icon; skip
            // this show rather than flash it at a garbage position. The next hover retries.
            Log.ForContext<TrayPeekWindow>().Debug("Peek show skipped: tray icon rect unavailable.");
            return;
        }

        panel.SetRows(rows);

        (windowWidth, windowHeight) = CalculatePhysicalWindowSize();
        (targetX, targetY) = CalculatePosition(iconRect, windowWidth, windowHeight);

        appWindow.MoveAndResize(new RectInt32(targetX, targetY, windowWidth, windowHeight));

        ApplyLayeredStyle();
        SetWindowOpacity(0);

        bool slideUp = targetY < iconRect.Top;
        panel.PlayShowAnimation(slideUp);

        NativeMethods.ShowWindow(peekHwnd, NativeMethods.SW_SHOWNOACTIVATE);
        isVisible = true;
        Log.ForContext<TrayPeekWindow>().Debug("Peek shown.");

        StartAnimationTimer();
        StartHoverTimer();
    }

    private void StartAnimationTimer()
    {
        StopAnimationTimer();

        animationStep = 0;
        animationTimer = new DispatcherTimer();
        animationTimer.Interval = TimeSpan.FromMilliseconds(AnimationIntervalMs);
        animationTimer.Tick += OnAnimationTimerTick;
        animationTimer.Start();
    }

    private void StopAnimationTimer()
    {
        if (animationTimer == null)
        {
            return;
        }

        animationTimer.Stop();
        animationTimer.Tick -= OnAnimationTimerTick;
        animationTimer = null;
    }

    private void OnAnimationTimerTick(object sender, object e)
    {
        animationStep++;
        if (animationStep > AnimationSteps)
        {
            StopAnimationTimer();
            SetWindowOpacity(255);
            RemoveLayeredStyle();
            return;
        }

        float t = (float)animationStep / AnimationSteps;
        float ease = 1.0f - (1.0f - t) * (1.0f - t);
        byte opacity = (byte)(255 * ease);
        SetWindowOpacity(opacity);
    }

    private void ApplyLayeredStyle()
    {
        IntPtr exStyle = NativeMethods.GetWindowLongPtr(peekHwnd, NativeMethods.GWL_EXSTYLE);
        long bits = exStyle.ToInt64();
        bits |= (long)NativeMethods.WS_EX_LAYERED;
        NativeMethods.SetWindowLongPtr(peekHwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(bits));
    }

    private void RemoveLayeredStyle()
    {
        IntPtr exStyle = NativeMethods.GetWindowLongPtr(peekHwnd, NativeMethods.GWL_EXSTYLE);
        long bits = exStyle.ToInt64();
        bits &= ~(long)NativeMethods.WS_EX_LAYERED;
        NativeMethods.SetWindowLongPtr(peekHwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(bits));
    }

    private void SetWindowOpacity(byte opacity)
    {
        NativeMethods.SetLayeredWindowAttributes(peekHwnd, 0, opacity, NativeMethods.LWA_ALPHA);
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
        bool overIcon = false;
        if (TryQueryIconRect(storedTrayHwnd, storedTrayIconId, out NativeMethods.RECT iconRect))
        {
            overIcon = cursor.X >= iconRect.Left - scaledExpansion
                    && cursor.X <= iconRect.Right + scaledExpansion
                    && cursor.Y >= iconRect.Top - scaledExpansion
                    && cursor.Y <= iconRect.Bottom + scaledExpansion;
        }

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

    private bool TryQueryIconRect(IntPtr trayHwnd, uint trayIconId, out NativeMethods.RECT iconRect)
    {
        var identifier = new NativeMethods.NOTIFYICONIDENTIFIER();
        identifier.cbSize = (uint)Marshal.SizeOf(typeof(NativeMethods.NOTIFYICONIDENTIFIER));
        identifier.hWnd = trayHwnd;
        identifier.uID = trayIconId;
        identifier.guidItem = Guid.Empty;

        int hr = NativeMethods.Shell_NotifyIconGetRect(ref identifier, out iconRect);
        if (hr != NativeMethods.S_OK)
        {
            // The query failed, so iconRect is unreliable (the API may leave it unwritten).
            // Callers fail safe: skip the show, or treat the pointer as off-icon so the peek
            // hides rather than sticking on a stale rect.
            if (iconRectUnavailable == false)
            {
                iconRectUnavailable = true;
                Log.ForContext<TrayPeekWindow>().Warning("Shell_NotifyIconGetRect failed (HRESULT 0x{HResult:X8}); peek positioning and hover checks degraded until it recovers.", hr);
            }
            return false;
        }

        if (iconRectUnavailable)
        {
            iconRectUnavailable = false;
            Log.ForContext<TrayPeekWindow>().Information("Shell_NotifyIconGetRect recovered.");
        }

        return true;
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
