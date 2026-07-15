using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Animation;
using QuinSlate.Ui.Components;
using QuinSlate.Ui.Constants;
using QuinSlate.Ui.Interop;
using QuinSlate.Ui.Logging;
using QuinSlate.Ui.Services;
using QuinSlate.Ui.Tray;
using Serilog;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using WinRT.Interop;
using Buffer = QuinSlate.Ui.Models.Buffer;

namespace QuinSlate.Ui;

/// <summary>
/// The main panel window. Hosts the buffer UI, owns the tray icon and global
/// hotkey, and subclasses its own <c>WndProc</c> so it can react to
/// <c>WM_HOTKEY</c> and the tray callback message.
/// </summary>
public sealed partial class MainWindow : Window
{
    /// <summary>
    /// The visible window title, also used by secondary instances to locate the
    /// existing window via <c>FindWindow</c> when handing off activation.
    /// </summary>
    public const string WindowTitle = AppConstants.AppName;

    private const int PanelDefaultWidth = 660;
    private const int PanelDefaultHeight = 560;
    private const int PanelMinWidth = 280;
    private const int PanelMinHeight = 280;
    private const int PanelDefaultInset = 16;
    private const int SettingsDebounceMilliseconds = 500;
    private const double ModalScrimOpacity = 0.45;
    private const int ModalScrimFadeMilliseconds = 130;

    // Delay after the tray menu closes before the About modal is finalised (owner disabled,
    // card raised). Long enough to fall past the menu helper window's asynchronous foreground
    // reshuffle so the owner is still enabled when Windows reassigns the foreground.
    private const int AboutModalFinaliseDelayMilliseconds = 64;

    // Upper bound on how long the startup reveal waits for the editor's first paint before
    // uncloaking anyway (showing the flat cover, exactly the pre-reveal behaviour), so a slow
    // or failed editor bring-up can never leave the window invisible.
    private const int StartupRevealDeadlineMilliseconds = 800;

    // How long after the startup render settles the tray-peek warm-up runs. The warm-up's
    // first XAML-island composition momentarily steals Win32 focus (even from a disabled,
    // non-activating window — the island's inner input window is not covered by the owner's
    // WS_DISABLED), which blanks the editor caret for a frame. Right after the reveal that
    // reads as a caret flash; two seconds in, the caret is mid-blink-cycle and a one-frame
    // hide is indistinguishable from the blink itself.
    private const int PeekWarmUpDelayMilliseconds = 2000;

    // One-time notice shown the first time the panel is hidden to the tray, so the user
    // learns the app keeps running rather than having quit.
    private const string TrayNoticeTitle = AppConstants.AppName + " is still running";
    private const string TrayNoticeText = "It's tucked away in the system tray. Click the tray icon any time to bring it back.";

    private Timer settingsDebounceTimer;
    private readonly object pendingStateLock = new object();
    private int pendingSizeWidth;
    private int pendingSizeHeight;
    private int pendingPositionX;
    private int pendingPositionY;
    private volatile bool isTornDown;


    private NativeMethods.WndProcDelegate newWndProc;
    private IntPtr originalWndProc;
    private IntPtr windowHandle;
    private uint taskbarCreatedMessage;
    private AppWindow appWindow;

    // Fills the bare Win32 window surface during WM_ERASEBKGND so it never flashes white/black
    // before the WinUI compositor presents its first frame. The fill is the mid-tone of the
    // per-theme background gradient, read from the single colour source (see App.xaml).
    private WindowBackgroundBrush backgroundBrush;

    private const uint TrayIconId = 1;

    private BufferService bufferService;
    private StartupService startupService;
    private SettingsService settingsService;
    private HotkeyManager hotkeyManager;
    private TrayIcon trayIcon;
    private TrayPeekWindow trayPeekWindow;
    private AboutWindow aboutWindow;

    private bool isPanelVisible;
    private bool isWindowActive;
    private bool isStartupRevealComplete;
    private DispatcherQueueTimer startupRevealTimer;
    private DispatcherQueueTimer peekWarmUpTimer;
    private long lastDeactivatedTick;
    private const long RecentDeactivationThresholdMs = 800;
    private bool isPinned;
    private bool isInitialised;
    private bool isContextMenuOpen;

    /// <summary>
    /// Constructs the window. Call <see cref="Initialise"/> immediately
    /// afterwards to wire services and Win32 plumbing.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Wires the buffer service, registers the global hotkey, and installs the
    /// tray icon. A manual launch shows the panel; when <paramref name="startHidden"/>
    /// is <c>true</c> (a Windows startup-task login launch) only the tray icon appears.
    /// Must be called once after construction. Calling it more than once is a no-op after
    /// the first successful call.
    /// </summary>
    public void Initialise(BufferService bufferService, IReadOnlyList<Buffer> buffers, string trayIconFilePath, StartupService startupService, SettingsService settingsService, bool startHidden)
    {
        if (isInitialised)
        {
            return;
        }

        if (bufferService == null)
        {
            throw new ArgumentNullException(nameof(bufferService));
        }

        if (buffers == null)
        {
            throw new ArgumentNullException(nameof(buffers));
        }

        if (startupService == null)
        {
            throw new ArgumentNullException(nameof(startupService));
        }

        if (settingsService == null)
        {
            throw new ArgumentNullException(nameof(settingsService));
        }

        this.bufferService = bufferService;
        this.startupService = startupService;
        this.settingsService = settingsService;

        Title = WindowTitle;

        IReadOnlyList<QuinSlate.Ui.Models.TabDefinition> tabDefinitions = settingsService.GetTabs();
        Panel.Initialise(bufferService, buffers, settingsService, tabDefinitions);

        windowHandle = WindowNative.GetWindowHandle(this);
        ConfigureWindowAppearance();
        SetWindowIcon(trayIconFilePath);
        ConfigureTitleBar();
        SubclassWindowProc();

        backgroundBrush = new WindowBackgroundBrush(
            DitheredGradientBrushFactory.MidColor(false),
            DitheredGradientBrushFactory.MidColor(true));
        backgroundBrush.SetTheme(IsDarkTheme());
        if (Content is FrameworkElement contentRoot)
        {
            contentRoot.ActualThemeChanged += OnContentActualThemeChanged;
        }

        hotkeyManager = new HotkeyManager(windowHandle);
        if (hotkeyManager.RegisterDefaultHotkey() == false)
        {
            Log.ForContext<MainWindow>().Warning("Global hotkey registration failed; the panel can still be opened from the tray.");
        }

        trayPeekWindow = new TrayPeekWindow();

        trayIcon = new TrayIcon(windowHandle);
        trayIcon.LeftClicked += OnTrayLeftClicked;
        trayIcon.RightClicked += OnTrayRightClicked;
        trayIcon.MouseHovered += OnTrayMouseHovered;
        trayIcon.MouseLeft += OnTrayMouseLeft;
        trayIcon.BalloonClicked += OnTrayBalloonClicked;

        trayIcon.Add(trayIconFilePath, AppConstants.AppName);
        trayIcon.SuppressTooltipOnHover = settingsService.TrayPeekEnabled;

        // The shell discards every notification-area icon when the taskbar is recreated
        // (e.g. an Explorer restart) and broadcasts TaskbarCreated so apps can re-add
        // theirs. Without this the icon — and the hover/click/peek behaviour that rides on
        // its callback messages — silently dies until the app is relaunched.
        taskbarCreatedMessage = NativeMethods.RegisterWindowMessage(NativeMethods.TaskbarCreatedMessage);
        if (taskbarCreatedMessage == 0)
        {
            Log.ForContext<MainWindow>().Warning("RegisterWindowMessage(TaskbarCreated) failed; the tray icon will not recover from an Explorer restart.");
        }

        Closed += OnWindowClosed;
        Panel.PinToggleRequested += OnPanelPinToggleRequested;
        Panel.CloseRequested += OnPanelCloseRequested;
        Panel.EditorFirstPainted += OnPanelEditorFirstPainted;
        Panel.StartupRenderSettled += OnPanelStartupRenderSettled;

        isPinned = settingsService.IsPinned;
        ApplyPinState();
        Panel.SetPinned(isPinned);

        isInitialised = true;

        if (startHidden)
        {
            // Activate must still be called to initialise the XAML island, but on a hidden
            // (login) launch that briefly shows the bare window frame for a frame before
            // AppWindow.Hide() takes effect — a visible flash. Cloak the window via DWM first
            // so the warm-up composes off-screen; uncloak once it is hidden (cloaking and
            // SW_HIDE are independent, so uncloaking a hidden window keeps it hidden, and the
            // next ShowPanel reveals a normal uncloaked window).
            NativeMethods.SetWindowCloak(windowHandle, true);
            this.Activate();
            HidePanel();
            NativeMethods.SetWindowCloak(windowHandle, false);
            isStartupRevealComplete = true;
        }
        else
        {
            // Cloak through the first composition so the window appears once, complete. Shown
            // uncloaked, it visibly assembles itself over several frames — bare frame and tab
            // strip first, then the logo image, then the selected tab's indicator, and last the
            // editor text — a stutter of pop-ins (captured frame-by-frame). Activate still runs
            // immediately (the XAML island and the WebView2 cannot initialise without it); the
            // uncloak happens once the editor page reports its first paint, or at a bounded
            // deadline as the safety net. The editor text itself is handed off by the page's
            // reveal entrance (see OnPanelEditorFirstPainted), not by the uncloak instant.
            NativeMethods.SetWindowCloak(windowHandle, true);
            this.Activate();
            ShowPanel();
            ScheduleStartupReveal();
        }
    }

    /// <summary>
    /// Arms the startup-reveal deadline. The reveal normally runs from
    /// <see cref="OnPanelEditorFirstPainted"/>; the timer guarantees the window can never stay
    /// cloaked longer than <see cref="StartupRevealDeadlineMilliseconds"/> (past it, the panel
    /// shows the flat startup cover and the editor's entrance runs whenever its first paint
    /// finally lands).
    /// </summary>
    private void ScheduleStartupReveal()
    {
        startupRevealTimer = DispatcherQueue.CreateTimer();
        startupRevealTimer.Interval = TimeSpan.FromMilliseconds(StartupRevealDeadlineMilliseconds);
        startupRevealTimer.Tick += (sender, args) =>
        {
            RevealStartupWindow();
        };
        startupRevealTimer.Start();
    }

    /// <summary>
    /// Uncloaks the window. The panel is fully composed except the editor area, which still
    /// shows the flat startup cover; the editor content is brought in by the page's reveal
    /// entrance rather than this uncloak, because Chromium throttles frame presentation while
    /// the window is cloaked and its surface at the uncloak instant cannot be trusted (the
    /// text visibly popped in a few frames after the reveal — captured at 60 fps). Idempotent.
    /// </summary>
    private void RevealStartupWindow()
    {
        if (isStartupRevealComplete)
        {
            return;
        }

        isStartupRevealComplete = true;

        if (startupRevealTimer != null)
        {
            startupRevealTimer.Stop();
            startupRevealTimer = null;
        }

        NativeMethods.SetWindowCloak(windowHandle, false);
    }

    /// <summary>
    /// Runs when the editor page reports its first composited frame: uncloak (the reveal), then
    /// ask the page to run the reveal entrance. The entrance starts on a frame the page has
    /// demonstrably presented while visible and fades the text in from the cover's flat tone,
    /// so the hand-off contains no frame-exact races. On the deadline path the window is
    /// already uncloaked and this just runs the entrance.
    /// </summary>
    private void OnPanelEditorFirstPainted(object sender, EventArgs e)
    {
        RevealStartupWindow();
        Panel.BeginStartupEntrance();
    }

    /// <summary>
    /// Runs the deferred startup warm-ups once the startup cover has dropped (the editor's
    /// reveal entrance started, or the panel's bounded fallback fired). Until then the UI
    /// thread and GPU belong to the critical launch path — window bring-up plus the
    /// WebView2/CodeMirror cold start — and the peek window's first-ever XAML composition (see
    /// <see cref="TrayPeekWindow.WarmUp"/>) is heavy enough to visibly delay it, so it waits
    /// its turn instead of competing. The reveal call is the safety net for the fallback paths
    /// (e.g. WebView2 creation failure), where no first paint ever arrives.
    /// </summary>
    private void OnPanelStartupRenderSettled(object sender, EventArgs e)
    {
        RevealStartupWindow();

        if (settingsService == null || settingsService.TrayPeekEnabled == false)
        {
            return;
        }

        peekWarmUpTimer = DispatcherQueue.CreateTimer();
        peekWarmUpTimer.Interval = TimeSpan.FromMilliseconds(PeekWarmUpDelayMilliseconds);
        peekWarmUpTimer.Tick += (timerSender, args) =>
        {
            peekWarmUpTimer.Stop();
            peekWarmUpTimer = null;
            if (isTornDown == false && trayPeekWindow != null)
            {
                trayPeekWindow.WarmUp();
            }
        };
        peekWarmUpTimer.Start();
    }

    private void ConfigureWindowAppearance()
    {
        var windowId = Win32Interop.GetWindowIdFromWindow(windowHandle);
        appWindow = AppWindow.GetFromWindowId(windowId);
        if (appWindow == null)
        {
            return;
        }

        float scale = NativeMethods.GetDpiForWindow(windowHandle) / 96.0f;

        // Size: restore from settings or use default
        int logicalWidth = settingsService.WindowWidth > 0 ? settingsService.WindowWidth : PanelDefaultWidth;
        int logicalHeight = settingsService.WindowHeight > 0 ? settingsService.WindowHeight : PanelDefaultHeight;
        int physicalWidth = (int)Math.Round(logicalWidth * scale);
        int physicalHeight = (int)Math.Round(logicalHeight * scale);
        appWindow.Resize(new Windows.Graphics.SizeInt32(physicalWidth, physicalHeight));

        // Position: restore from settings if on screen, otherwise default to bottom-right
        bool positionRestored = false;
        if (settingsService.HasSavedPosition)
        {
            int physX = (int)Math.Round(settingsService.WindowLeft * scale);
            int physY = (int)Math.Round(settingsService.WindowTop * scale);
            if (IsWindowRectOnScreen(physX, physY, physicalWidth, physicalHeight))
            {
                appWindow.Move(new Windows.Graphics.PointInt32(physX, physY));
                positionRestored = true;
            }
        }

        if (!positionRestored)
        {
            var workArea = GetPrimaryWorkArea();
            int physInset = (int)Math.Round(PanelDefaultInset * scale);
            int defaultX = workArea.Right - physicalWidth - physInset;
            int defaultY = workArea.Bottom - physicalHeight - physInset;
            appWindow.Move(new Windows.Graphics.PointInt32(defaultX, defaultY));
        }

        var presenter = appWindow.Presenter as OverlappedPresenter;
        if (presenter != null)
        {
            presenter.IsResizable = true;
            presenter.IsMaximizable = false;

            // Keep the window minimizable so the shell sends WM_SYSCOMMAND/
            // SC_MINIMIZE when its taskbar button is clicked while active; the
            // WndProc intercepts that to hide-to-tray instead of minimizing.
            // No minimize button renders because the title bar is removed below.
            presenter.IsMinimizable = true;

            // Keep the resize border but remove the system title bar and its
            // caption buttons; the panel draws its own pin and close buttons.
            presenter.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false);
        }

        appWindow.Changed += OnAppWindowChanged;
    }

    private void SetWindowIcon(string iconFilePath)
    {
        if (appWindow == null)
        {
            return;
        }

        if (string.IsNullOrEmpty(iconFilePath))
        {
            return;
        }

        appWindow.SetIcon(iconFilePath);
    }

    private NativeMethods.RECT GetPrimaryWorkArea()
    {
        var workArea = new NativeMethods.RECT();
        NativeMethods.SystemParametersInfo(NativeMethods.SPI_GETWORKAREA, 0, ref workArea, 0);
        return workArea;
    }

    private bool IsWindowRectOnScreen(int physX, int physY, int physWidth, int physHeight)
    {
        var workingAreas = new List<NativeMethods.RECT>();
        var callback = new NativeMethods.EnumDisplayMonitorsDelegate(
            (IntPtr hMonitor, IntPtr hdc, ref NativeMethods.RECT rect, IntPtr data) =>
            {
                var info = new NativeMethods.MONITORINFO
                {
                    cbSize = (uint)Marshal.SizeOf<NativeMethods.MONITORINFO>()
                };
                if (NativeMethods.GetMonitorInfo(hMonitor, ref info))
                {
                    workingAreas.Add(info.rcWork);
                }
                return true;
            });

        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);

        foreach (var workArea in workingAreas)
        {
            if (physX < workArea.Right && physX + physWidth > workArea.Left &&
                physY < workArea.Bottom && physY + physHeight > workArea.Top)
            {
                return true;
            }
        }

        return false;
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (args.DidSizeChange == false && args.DidPositionChange == false)
        {
            return;
        }

        lock (pendingStateLock)
        {
            pendingSizeWidth = appWindow.Size.Width;
            pendingSizeHeight = appWindow.Size.Height;
            pendingPositionX = appWindow.Position.X;
            pendingPositionY = appWindow.Position.Y;
        }

        if (settingsDebounceTimer == null)
        {
            settingsDebounceTimer = new Timer(OnSettingsDebounceElapsed, null, SettingsDebounceMilliseconds, Timeout.Infinite);
        }
        else
        {
            settingsDebounceTimer.Change(SettingsDebounceMilliseconds, Timeout.Infinite);
        }
    }

    private void OnSettingsDebounceElapsed(object state)
    {
        int sizeWidth, sizeHeight, posX, posY;
        lock (pendingStateLock)
        {
            sizeWidth = pendingSizeWidth;
            sizeHeight = pendingSizeHeight;
            posX = pendingPositionX;
            posY = pendingPositionY;
        }
        float scale = NativeMethods.GetDpiForWindow(windowHandle) / 96.0f;
        int logicalWidth = (int)Math.Round(sizeWidth / scale);
        int logicalHeight = (int)Math.Round(sizeHeight / scale);
        DispatcherQueue.TryEnqueue(() =>
        {
            if (isTornDown)
            {
                return;
            }

            settingsService.WindowWidth = logicalWidth;
            settingsService.WindowHeight = logicalHeight;
            settingsService.WindowLeft = (int)Math.Round(posX / scale);
            settingsService.WindowTop = (int)Math.Round(posY / scale);
            settingsService.HasSavedPosition = true;
            _ = settingsService.SaveAsync();
        });
    }

    private void SubclassWindowProc()
    {
        newWndProc = WindowProc;
        var newWndProcPtr = Marshal.GetFunctionPointerForDelegate(newWndProc);
        originalWndProc = NativeMethods.SetWindowLongPtr(windowHandle, NativeMethods.GWLP_WNDPROC, newWndProcPtr);
    }

    private bool IsDarkTheme()
    {
        if (Content is FrameworkElement contentRoot)
        {
            return contentRoot.ActualTheme == ElementTheme.Dark;
        }
        return true;
    }

    private void OnContentActualThemeChanged(FrameworkElement sender, object args)
    {
        if (backgroundBrush != null)
        {
            backgroundBrush.SetTheme(IsDarkTheme());
        }
    }

    private IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == NativeMethods.WM_ERASEBKGND)
        {
            // Paint the bare window surface with the theme-matched background instead of letting
            // DefWindowProc erase it white. This is the surface that shows between the window
            // becoming visible and the WinUI compositor presenting its first frame — the blank
            // flash. wParam is the device context to erase into.
            if (backgroundBrush != null && backgroundBrush.Erase(hWnd, wParam))
            {
                return new IntPtr(1);
            }
        }

        if (msg == NativeMethods.WM_GETMINMAXINFO)
        {
            float scale = NativeMethods.GetDpiForWindow(hWnd) / 96.0f;
            var info = Marshal.PtrToStructure<NativeMethods.MINMAXINFO>(lParam);
            info.ptMinTrackSize = new NativeMethods.POINT
            {
                X = (int)Math.Round(PanelMinWidth * scale),
                Y = (int)Math.Round(PanelMinHeight * scale)
            };
            Marshal.StructureToPtr(info, lParam, false);
            return IntPtr.Zero;
        }

        if (msg == NativeMethods.WM_ACTIVATE)
        {
            if ((wParam.ToInt32() & 0xFFFF) == NativeMethods.WA_INACTIVE)
            {
                isWindowActive = false;
                lastDeactivatedTick = Environment.TickCount64;
            }
            else
            {
                isWindowActive = true;
            }
        }

        if (msg == NativeMethods.WM_HOTKEY)
        {
            if (hotkeyManager != null && wParam.ToInt32() == hotkeyManager.HotkeyIdentifier)
            {
                TogglePanelVisibility();
                return IntPtr.Zero;
            }
        }

        if (msg == NativeMethods.WM_SYSCOMMAND && (wParam.ToInt64() & 0xFFF0) == NativeMethods.SC_MINIMIZE)
        {
            HidePanel();
            return IntPtr.Zero;
        }

        if (msg == NativeMethods.WM_SIZE && wParam.ToInt32() == NativeMethods.SIZE_MINIMIZED && isPanelVisible)
        {
            HidePanel();
            return IntPtr.Zero;
        }

        if (msg == NativeMethods.WM_QUINSLATE_ACTIVATE)
        {
            ShowPanel();
            return IntPtr.Zero;
        }

        if (msg == NativeMethods.WM_QUERYENDSESSION)
        {
            return new IntPtr(1);
        }

        if (msg == NativeMethods.WM_ENDSESSION)
        {
            if (wParam != IntPtr.Zero && !isTornDown)
            {
                Teardown();
            }
            return IntPtr.Zero;
        }

        if (taskbarCreatedMessage != 0 && msg == taskbarCreatedMessage)
        {
            if (trayIcon != null)
            {
                trayIcon.Reregister();
            }
            return IntPtr.Zero;
        }

        if (trayIcon != null)
        {
            if (trayIcon.HandleMessage(msg, wParam, lParam))
            {
                return IntPtr.Zero;
            }
        }

        return NativeMethods.CallWindowProc(originalWndProc, hWnd, msg, wParam, lParam);
    }

    private void OnTrayLeftClicked(object sender, EventArgs e)
    {
        TogglePanelVisibility();
    }

    private void OnTrayRightClicked(object sender, EventArgs e)
    {
        ShowContextMenu();
    }

    private void OnTrayBalloonClicked(object sender, EventArgs e)
    {
        ShowPanel();
    }

    private void OnTrayMouseHovered(object sender, EventArgs e)
    {
        if (isContextMenuOpen)
        {
            Log.ForContext<MainWindow>().Debug("Tray hover ignored: context menu open.");
            return;
        }

        if (trayPeekWindow != null && settingsService != null && settingsService.TrayPeekEnabled)
        {
            trayPeekWindow.Show(bufferService, settingsService, windowHandle, TrayIconId);
        }
    }

    private void OnTrayMouseLeft(object sender, EventArgs e)
    {
        if (trayPeekWindow != null)
        {
            trayPeekWindow.Hide();
        }
    }

    private async void ShowContextMenu()
    {
        if (startupService == null)
        {
            return;
        }

        if (trayPeekWindow != null)
        {
            trayPeekWindow.Hide();
        }

        isContextMenuOpen = true;

        bool startupEnabled = await startupService.IsEnabledAsync();
        bool peekEnabled = settingsService != null && settingsService.TrayPeekEnabled;

        var menu = new TrayMenu();
        menu.Show(
            onOpen: () => ShowPanel(),
            onToggleStartup: async () =>
            {
                if (await startupService.IsEnabledAsync())
                {
                    await startupService.DisableAsync();
                }
                else
                {
                    await startupService.EnableAsync();
                }
            },
            onTogglePeek: () =>
            {
                bool newPeekEnabled = !settingsService.TrayPeekEnabled;
                settingsService.TrayPeekEnabled = newPeekEnabled;
                if (trayIcon != null)
                {
                    trayIcon.SuppressTooltipOnHover = newPeekEnabled;
                }
                _ = settingsService.SaveAsync();
            },
            onAbout: () => ShowAboutDialog(),
            onExit: () => ExitApplication(),
            startupEnabled: startupEnabled,
            peekEnabled: peekEnabled,
            onClose: () =>
            {
                isContextMenuOpen = false;

                // The tray menu's helper window is torn down now, but the foreground reshuffle its
                // destruction triggers is asynchronous and lands shortly after this. Finalise the
                // modal past that point: with the owner still enabled, Windows restores the
                // foreground to the panel (the modal backdrop) rather than skipping the disabled
                // owner and flashing an unrelated app on top. The finalise then raises About and
                // disables the owner for modal input blocking.
                if (aboutWindow != null)
                {
                    ScheduleAboutModalFinalise();
                }
            }
        );
    }

    private void ShowAboutDialog()
    {
        if (windowHandle == IntPtr.Zero)
        {
            return;
        }

        if (aboutWindow != null)
        {
            // About is already open, but the tray menu currently owns the foreground and reverts it
            // to the previously-active window as it tears down — raising About now only flashes it.
            // The menu's onClose finalise raises it once the foreground has settled.
            PrepareModalBackdrop();
            return;
        }

        // Build the About window — a heavy XAML-island construction that blocks the UI thread —
        // BEFORE revealing the panel. If the panel were shown first, that build would stall its
        // first paint and the bare window would hold a blank (white) compositor frame until the
        // build finished; building while the panel is still hidden lets the show below paint
        // promptly.
        string storageDirectory = bufferService != null ? bufferService.AppDataDirectory : null;
        aboutWindow = new AboutWindow(windowHandle, storageDirectory);
        aboutWindow.Closed += OnAboutWindowClosed;

        PrepareModalBackdrop();

        // The card was positioned in its constructor, which may have run while the owner was
        // minimized (e.g. after Win+D) and therefore off-screen. Now that PrepareModalBackdrop
        // has restored the owner, re-centre the card onto it.
        aboutWindow.CenterOnOwner();

        ShowModalScrim();
        aboutWindow.Activate();
    }

    /// <summary>
    /// Brings the panel up as the modal backdrop for the About card without giving it focus.
    /// The About window activates itself; re-activating or re-focusing the panel here would
    /// pulse its border/shadow (and caret) to the active state for a frame before About takes
    /// the foreground — the open flash this avoids.
    /// </summary>
    private void PrepareModalBackdrop()
    {
        if (isPanelVisible && IsPanelMinimized() == false)
        {
            // Already on screen but inactive (the tray menu holds the foreground). Raise it
            // beneath the card without activating it: a no-activate raise leaves the panel's
            // frame untouched so nothing flashes, while still lifting it above other windows.
            IntPtr insertAfter = isPinned ? NativeMethods.HWND_TOPMOST : NativeMethods.HWND_TOP;
            NativeMethods.SetWindowPos(windowHandle, insertAfter, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
        }
        else
        {
            // Hidden, or minimized by "Show Desktop" (Win+D): reveal/restore it so the dimmed
            // backdrop and centred card have a surface. A no-activate raise does not un-minimize
            // a window, so a minimized panel must go through ShowPanel (which restores it).
            // Showing activates the window (harmless — there is no prior visible state to pulse),
            // but skip the editor focus: the modal disables the owner and takes keyboard focus, so
            // focusing it would only flash the caret.
            ShowPanel(focusEditor: false);
        }
    }

    /// <summary>
    /// Finalises the About modal a short time after the tray menu closes, once the menu helper
    /// window's asynchronous foreground reshuffle has landed. Raises the card to the foreground
    /// (past any foreground lock) and disables the owner for modal input blocking. Deferring the
    /// owner-disable to here — rather than the <see cref="AboutWindow"/> constructor — keeps the
    /// owner enabled through the reshuffle, so Windows restores the foreground to the panel
    /// backdrop instead of skipping the disabled owner and flashing an unrelated app on top.
    /// </summary>
    private void ScheduleAboutModalFinalise()
    {
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(AboutModalFinaliseDelayMilliseconds);
        timer.Tick += (sender, args) =>
        {
            timer.Stop();
            if (aboutWindow == null)
            {
                return;
            }

            aboutWindow.BringToForeground();
            aboutWindow.DisableOwnerForModality();
        };
        timer.Start();
    }

    private void OnAboutWindowClosed(object sender, WindowEventArgs args)
    {
        if (aboutWindow != null)
        {
            aboutWindow.Closed -= OnAboutWindowClosed;
            aboutWindow = null;
        }

        HideModalScrim();
    }

    private void ShowModalScrim()
    {
        ModalScrim.Visibility = Visibility.Visible;
        FadeScrim(ModalScrimOpacity, null);
    }

    private void HideModalScrim()
    {
        FadeScrim(0, () =>
        {
            // A superseded fade still raises Completed; only collapse if About was not reopened
            // in the meantime, so the scrim stays up for the new session.
            if (aboutWindow == null)
            {
                ModalScrim.Visibility = Visibility.Collapsed;
            }
        });
    }

    private void FadeScrim(double toOpacity, Action onCompleted)
    {
        var animation = new DoubleAnimation
        {
            To = toOpacity,
            Duration = new Duration(TimeSpan.FromMilliseconds(ModalScrimFadeMilliseconds))
        };
        Storyboard.SetTarget(animation, ModalScrim);
        Storyboard.SetTargetProperty(animation, "Opacity");

        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        if (onCompleted != null)
        {
            storyboard.Completed += (sender, args) => onCompleted();
        }

        storyboard.Begin();
    }

    private void Teardown()
    {
        isTornDown = true;

        if (startupRevealTimer != null)
        {
            startupRevealTimer.Stop();
            startupRevealTimer = null;
        }

        if (peekWarmUpTimer != null)
        {
            peekWarmUpTimer.Stop();
            peekWarmUpTimer = null;
        }

        if (originalWndProc != IntPtr.Zero)
        {
            NativeMethods.SetWindowLongPtr(windowHandle, NativeMethods.GWLP_WNDPROC, originalWndProc);
        }

        newWndProc = null;

        if (aboutWindow != null)
        {
            aboutWindow.Closed -= OnAboutWindowClosed;
            aboutWindow.Close();
            aboutWindow = null;
        }

        if (backgroundBrush != null)
        {
            backgroundBrush.Dispose();
            backgroundBrush = null;
        }

        if (trayPeekWindow != null)
        {
            trayPeekWindow.Dispose();
            trayPeekWindow = null;
        }

        if (trayIcon != null)
        {
            trayIcon.Dispose();
            trayIcon = null;
        }


        if (hotkeyManager != null)
        {
            hotkeyManager.Dispose();
            hotkeyManager = null;
        }

        if (settingsDebounceTimer != null)
        {
            settingsDebounceTimer.Dispose();
            settingsDebounceTimer = null;
        }

        if (appWindow != null && settingsService != null)
        {
            float scale = NativeMethods.GetDpiForWindow(windowHandle) / 96.0f;
            settingsService.WindowWidth = (int)Math.Round(appWindow.Size.Width / scale);
            settingsService.WindowHeight = (int)Math.Round(appWindow.Size.Height / scale);
            settingsService.WindowLeft = (int)Math.Round(appWindow.Position.X / scale);
            settingsService.WindowTop = (int)Math.Round(appWindow.Position.Y / scale);
            settingsService.HasSavedPosition = true;
            settingsService.SaveSync();
        }

        // Drain any keystrokes still held by the editor extraction debounce into the buffer
        // service before it flushes, so the exit write captures the very latest edits.
        Panel.FlushPendingContent();

        if (bufferService != null)
        {
            bufferService.FlushPendingWritesSync();
        }

        ((App)Application.Current).ReleaseSingleInstanceMutex();

        Log.ForContext<MainWindow>().Information("Shutdown complete.");
        LogBootstrapper.Shutdown();
    }

    private void ExitApplication()
    {
        // Hide the window before tearing down. Teardown restores the original WndProc (dropping
        // the WM_ERASEBKGND background fill) and disposes the background brush while synchronous
        // shutdown writes run, so a still-visible window gets erased to the stock white by
        // DefWindowProc for its final frames — a white flash on exit. Hidden directly via
        // AppWindow rather than HidePanel, which would pop the "still running" tray notice.
        if (appWindow != null)
        {
            appWindow.Hide();
        }

        isPanelVisible = false;

        Teardown();
        Environment.Exit(App.ExitCodeNormal);
    }

    private void TogglePanelVisibility()
    {
        // A minimized panel (e.g. after Win+D / Show Desktop) is not visible to the user even
        // though isPanelVisible may still be set. Toggling it would only hide it further, so a
        // hotkey or tray click would never bring it back; always restore it instead.
        if (IsPanelMinimized())
        {
            ShowPanel();
            return;
        }

        if (isPanelVisible)
        {
            bool wasActive = isWindowActive ||
                             (Environment.TickCount64 - lastDeactivatedTick) < RecentDeactivationThresholdMs;
            if (wasActive)
            {
                HidePanel();
            }
            else
            {
                ShowPanel();
            }
        }
        else
        {
            ShowPanel();
        }
    }

    private bool IsPanelMinimized()
    {
        if (appWindow == null)
        {
            return false;
        }

        var presenter = appWindow.Presenter as OverlappedPresenter;
        return presenter != null && presenter.State == OverlappedPresenterState.Minimized;
    }

    private void ShowPanel()
    {
        ShowPanel(focusEditor: true);
    }

    private void ShowPanel(bool focusEditor)
    {
        if (appWindow != null)
        {
            appWindow.Show();

            // "Show Desktop" (Win+D) minimizes the window; AppWindow.Show() and
            // SetForegroundWindow do not un-minimize it, so the panel would return as a taskbar
            // button with no visible window and stay that way however many times the tray icon
            // is clicked. Restore it explicitly when minimized.
            if (IsPanelMinimized())
            {
                Log.ForContext<MainWindow>().Debug("Restoring panel from minimized state (e.g. after Show Desktop).");
                var presenter = appWindow.Presenter as OverlappedPresenter;
                presenter.Restore();
            }
        }

        ApplyPinState();
        NativeMethods.SetForegroundWindow(windowHandle);
        if (focusEditor)
        {
            Panel.FocusActiveEditor();
        }
        isPanelVisible = true;
        isWindowActive = true;
    }

    private void HidePanel()
    {
        bool wasVisible = isPanelVisible;

        if (appWindow != null)
        {
            appWindow.Hide();
        }

        isPanelVisible = false;
        isWindowActive = false;

        // A notice left over from this session must not be the first thing the panel shows when it
        // is next summoned, and the next clamp deserves an immediate answer.
        Panel.ResetLimitNotice();

        // Only on a genuine user-initiated hide of a shown panel — never the init-time hide
        // of a startup-hidden (login) launch, where the panel was never visible.
        if (wasVisible)
        {
            ShowFirstTrayNoticeIfNeeded();
        }
    }

    private void ShowFirstTrayNoticeIfNeeded()
    {
        if (settingsService == null || trayIcon == null || settingsService.HasShownTrayNotice)
        {
            return;
        }

        settingsService.HasShownTrayNotice = true;
        _ = settingsService.SaveAsync();
        trayIcon.ShowBalloon(TrayNoticeTitle, TrayNoticeText);
    }

    private void ApplyPinState()
    {
        IntPtr insertAfter = isPinned ? NativeMethods.HWND_TOPMOST : NativeMethods.HWND_NOTOPMOST;
        NativeMethods.SetWindowPos(windowHandle, insertAfter, 0, 0, 0, 0, NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE);
    }

    private void ConfigureTitleBar()
    {
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(Panel.TitleBarDragArea);
    }

    private void OnPanelPinToggleRequested(object sender, EventArgs e)
    {
        isPinned = !isPinned;
        settingsService.IsPinned = isPinned;
        ApplyPinState();
        Panel.SetPinned(isPinned);
        _ = settingsService.SaveAsync();
    }

    private void OnPanelCloseRequested(object sender, EventArgs e)
    {
        HidePanel();
    }


    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        args.Handled = true;
        HidePanel();
    }
}
