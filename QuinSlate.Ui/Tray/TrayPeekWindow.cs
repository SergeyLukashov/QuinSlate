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

    // The nominal content height in DIPs. Since TrayPeekPanel's middle buffer-list region is
    // star-sized, this no longer has to match the client height pixel-for-pixel: any few-pixel
    // difference (DPI rounding, DWM frame inset) is absorbed by that star region rather than
    // clipping the footer. It still drives the window's on-screen size, so keep it a faithful
    // sum of the panel's fixed vertical bands.
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
    private const byte OpacityTransparent = 0;
    private const byte OpacityOpaque = 255;
    private bool isVisible;
    private bool warmUpInFlight;
    private bool disposed;
    private bool iconRectUnavailable;
    private float dpiScale = 1.0f;
    private IntPtr storedTrayHwnd;
    private uint storedTrayIconId;

    private BufferService storedBufferService;
    private SettingsService storedSettingsService;

    /// <summary>
    /// Creates the peek window container. The underlying WinUI 3 <see cref="Window"/> is
    /// created by <see cref="WarmUp"/> at startup, or lazily on the first call to
    /// <see cref="Show"/> if no warm-up ran (e.g. peek was enabled from the tray menu later).
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

        // dpiScale is resolved from the tray icon's monitor in ExecuteShow, once the icon
        // rect is known, so it reflects the taskbar monitor's scale on a mixed-DPI setup.
        if (isVisible)
        {
            return;
        }

        StartShowDelayTimer();
    }

    /// <summary>
    /// Creates the peek window and runs its XAML island through a first composition pass at
    /// startup, off the hover path. The window is shown non-activated and fully transparent
    /// (it is permanently layered with alpha 0 at rest, so nothing is painted on screen) and
    /// hidden again once its content has loaded. Without this, the island's first-ever
    /// bring-up — window class registration, content load, compositor/swap-chain creation —
    /// all runs when the user first hovers the tray icon; on slow integrated GPUs that cold
    /// start both lags the first peek and is the window where the layered-fade crash was
    /// observed (see Docs/Investigations/04-TRAY-PEEK-HOVER-FAILFAST-CRASH.md). Safe to call
    /// once after the main window is up; a hover arriving mid-warm-up simply takes over the
    /// already-created window.
    /// </summary>
    public void WarmUp()
    {
        if (disposed || peekWindow != null)
        {
            return;
        }

        EnsureWindowCreated();
        if (peekHwnd == IntPtr.Zero)
        {
            return;
        }

        warmUpInFlight = true;
        panel.Loaded += OnWarmUpPanelLoaded;
        NativeMethods.ShowWindow(peekHwnd, NativeMethods.SW_SHOWNOACTIVATE);
        Log.ForContext<TrayPeekWindow>().Debug("Peek warm-up started.");
    }

    private void OnWarmUpPanelLoaded(object sender, RoutedEventArgs e)
    {
        if (panel != null)
        {
            panel.Loaded -= OnWarmUpPanelLoaded;
        }

        if (warmUpInFlight == false)
        {
            return;
        }

        warmUpInFlight = false;
        NativeMethods.ShowWindow(peekHwnd, NativeMethods.SW_HIDE);
        Log.ForContext<TrayPeekWindow>().Debug("Peek warm-up complete; window hidden.");
    }

    private void CancelWarmUp()
    {
        if (warmUpInFlight == false)
        {
            return;
        }

        warmUpInFlight = false;
        if (panel != null)
        {
            panel.Loaded -= OnWarmUpPanelLoaded;
        }
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

        CancelWarmUp();
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

        // WS_EX_LAYERED is set once here and never removed. It used to be added before each
        // show and stripped when the entrance fade finished, but flipping the style bit while
        // the XAML island is mid-composition intermittently fail-fasts the process inside
        // Microsoft.UI.Xaml.dll (stowed exception 0xc000027b / E_UNEXPECTED) on slower GPUs —
        // see Docs/Investigations/04-TRAY-PEEK-HOVER-FAILFAST-CRASH.md. A permanently layered
        // window renders identically; only the style *transition* raced the renderer.
        bits |= NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_LAYERED;
        bits &= ~(long)NativeMethods.WS_EX_APPWINDOW;
        NativeMethods.SetWindowLongPtr(peekHwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(bits));

        // A layered window is not displayed until SetLayeredWindowAttributes is called, so the
        // alpha must be initialised here; transparent is the correct resting state (every show
        // fades in from 0).
        SetWindowOpacity(OpacityTransparent);

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

        // A hover during the startup warm-up takes over the window: the warm-up's pending
        // hide-on-loaded must not fire later and hide a peek the user is looking at. The
        // window may still be in its warm-up show (transparent), which the normal flow below
        // handles — it repositions, replays the fade from alpha 0, and re-shows.
        CancelWarmUp();

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

        // Scale to the DPI of the monitor the tray icon sits on, not the system (primary
        // monitor) DPI. On a mixed-DPI setup the taskbar can be on a monitor whose scale
        // differs from the primary's, and system DPI would mis-size and mis-place the peek.
        dpiScale = NativeMethods.GetDpiForRect(iconRect) / 96.0f;

        panel.SetRows(rows);

        (windowWidth, windowHeight) = CalculatePhysicalWindowSize();
        (targetX, targetY) = CalculatePosition(iconRect, windowWidth, windowHeight);

        appWindow.MoveAndResize(new RectInt32(targetX, targetY, windowWidth, windowHeight));

        SetWindowOpacity(OpacityTransparent);

        bool slideUp = targetY < iconRect.Top;
        panel.PlayShowAnimation(slideUp);

        NativeMethods.ShowWindow(peekHwnd, NativeMethods.SW_SHOWNOACTIVATE);
        AssertTopmost();
        isVisible = true;
        Log.ForContext<TrayPeekWindow>().Debug("Peek shown.");

        StartAnimationTimer();
        StartHoverTimer();
    }

    /// <summary>
    /// Re-establishes the window's topmost z-order. Required on every show: "Show Desktop"
    /// (Win+D) and other shell actions that hide all windows clear <c>WS_EX_TOPMOST</c>, and
    /// the peek window is created once and reused, so the topmost flag applied at creation is
    /// not enough — without re-asserting it, a demoted peek reappears behind other windows and
    /// stays there until the app restarts.
    /// </summary>
    private void AssertTopmost()
    {
        IntPtr exStyle = NativeMethods.GetWindowLongPtr(peekHwnd, NativeMethods.GWL_EXSTYLE);
        bool isTopmost = (exStyle.ToInt64() & NativeMethods.WS_EX_TOPMOST) != 0;
        if (isTopmost == false)
        {
            Log.ForContext<TrayPeekWindow>().Debug("Peek had lost topmost z-order (e.g. after Show Desktop); re-asserting.");
        }

        NativeMethods.SetWindowPos(
            peekHwnd,
            NativeMethods.HWND_TOPMOST,
            0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
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
            SetWindowOpacity(OpacityOpaque);
            return;
        }

        float t = (float)animationStep / AnimationSteps;
        float ease = 1.0f - (1.0f - t) * (1.0f - t);
        byte opacity = (byte)(OpacityOpaque * ease);
        SetWindowOpacity(opacity);
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

        // Round the outer height UP. Truncating (as the width still does) can leave the client
        // area a pixel or two shorter than the content, which previously clipped the footer at
        // low DPI. With the panel's star-sized middle region absorbing any surplus, rounding up
        // only ever adds a sub-pixel of slack to that region and never clips header or footer.
        int height = (int)Math.Ceiling(LogicalWindowHeight * dpiScale);
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
