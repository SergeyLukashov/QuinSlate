using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using QuinSlate.Ui.Interop;
using QuinSlate.Ui.Services;
using System;
using System.Runtime.InteropServices;
using Windows.Graphics;
using WinRT;
using WinRT.Interop;
using Buffer = QuinSlate.Ui.Models.Buffer;

namespace QuinSlate.Ui.Tray;

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
    private MicaController micaController;
    private SystemBackdropConfiguration backdropConfiguration;
    private FrameworkElement backdropThemeElement;
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

        if (backdropThemeElement != null)
        {
            backdropThemeElement.Loaded -= OnBackdropElementLoaded;
            backdropThemeElement.ActualThemeChanged -= OnBackdropThemeChanged;
            backdropThemeElement = null;
        }

        if (micaController != null)
        {
            micaController.Dispose();
            micaController = null;
        }

        backdropConfiguration = null;

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
        SetupActiveMicaBackdrop();

        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(peekHwnd);
        appWindow = AppWindow.GetFromWindowId(windowId);
    }

    /// <summary>
    /// Drives Mica through a manually managed <see cref="MicaController"/> instead of
    /// the auto-managed <see cref="Microsoft.UI.Xaml.Media.MicaBackdrop"/>. The peek
    /// window is a non-activating tool window, so it never receives focus and the
    /// auto-managed backdrop would keep <c>IsInputActive = false</c> forever, causing
    /// <see cref="MicaController"/> to paint its flat inactive fallback color. By owning
    /// the <see cref="SystemBackdropConfiguration"/> we force <c>IsInputActive = true</c>
    /// permanently so Mica samples the desktop wallpaper in its active, non-uniform state.
    /// </summary>
    private void SetupActiveMicaBackdrop()
    {
        if (MicaController.IsSupported() == false)
        {
            return;
        }

        backdropConfiguration = new SystemBackdropConfiguration();
        backdropConfiguration.IsInputActive = true;

        backdropThemeElement = peekWindow.Content as FrameworkElement;
        if (backdropThemeElement != null)
        {
            // ActualTheme is read again on Loaded because, at setup time, the panel's
            // visual tree is not yet attached and ActualTheme returns the framework
            // default (Light) rather than the resolved system/app theme. Reading the
            // stale value here would make MicaController paint its Light-theme tint
            // (visibly warmer/brownish) while the main window's MicaBackdrop paints the
            // real system theme, producing the tone mismatch. ActualThemeChanged alone
            // is insufficient: it only fires on a change, not on the initial resolve.
            ApplyResolvedBackdropTheme();
            backdropThemeElement.Loaded += OnBackdropElementLoaded;
            backdropThemeElement.ActualThemeChanged += OnBackdropThemeChanged;
        }

        micaController = new MicaController();
        micaController.Kind = MicaKind.Base;
        micaController.SetSystemBackdropConfiguration(backdropConfiguration);
        micaController.AddSystemBackdropTarget(peekWindow.As<ICompositionSupportsSystemBackdrop>());
    }

    private void OnBackdropElementLoaded(object sender, RoutedEventArgs args)
    {
        ApplyResolvedBackdropTheme();
    }

    private void OnBackdropThemeChanged(FrameworkElement sender, object args)
    {
        ApplyResolvedBackdropTheme();
    }

    /// <summary>
    /// Copies the backdrop theme element's currently resolved <see cref="FrameworkElement.ActualTheme"/>
    /// into the <see cref="SystemBackdropConfiguration"/> so the manually managed
    /// <see cref="MicaController"/> computes the same Light/Dark tint defaults the main
    /// window's auto-managed <see cref="Microsoft.UI.Xaml.Media.MicaBackdrop"/> uses. No
    /// explicit tint, luminosity, or fallback values are set, so the controller keeps its
    /// system defaults and matches the main window's tone exactly.
    /// </summary>
    private void ApplyResolvedBackdropTheme()
    {
        if (backdropConfiguration == null || backdropThemeElement == null)
        {
            return;
        }

        backdropConfiguration.Theme = MapTheme(backdropThemeElement.ActualTheme);
    }

    private static SystemBackdropTheme MapTheme(ElementTheme theme)
    {
        if (theme == ElementTheme.Dark)
        {
            return SystemBackdropTheme.Dark;
        }

        if (theme == ElementTheme.Light)
        {
            return SystemBackdropTheme.Light;
        }

        return SystemBackdropTheme.Default;
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

        panel.SetRows(rows);

        NativeMethods.RECT iconRect = QueryIconRect(storedTrayHwnd, storedTrayIconId);
        (windowWidth, windowHeight) = CalculatePhysicalWindowSize();
        (targetX, targetY) = CalculatePosition(iconRect, windowWidth, windowHeight);

        appWindow.MoveAndResize(new RectInt32(targetX, targetY, windowWidth, windowHeight));

        ApplyLayeredStyle();
        SetWindowOpacity(0);

        bool slideUp = targetY < iconRect.Top;
        panel.PlayShowAnimation(slideUp);

        NativeMethods.ShowWindow(peekHwnd, NativeMethods.SW_SHOWNOACTIVATE);
        isVisible = true;

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
