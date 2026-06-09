using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using QuinSlate.Ui.Components;
using QuinSlate.Ui.Interop;
using QuinSlate.Ui.Services;
using QuinSlate.Ui.Tray;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
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

    private bool isPanelVisible;
    private bool isWindowActive;
    private long lastDeactivatedTick;
    private const long RecentDeactivationThresholdMs = 800;
    private bool isAboutDialogOpen;
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
    /// Wires the buffer service, registers the global hotkey, installs the
    /// tray icon, and hides the window. Must be called once after construction.
    /// Calling it more than once is a no-op after the first successful call.
    /// </summary>
    public void Initialise(BufferService bufferService, string trayIconFilePath, StartupService startupService, SettingsService settingsService)
    {
        if (isInitialised)
        {
            return;
        }

        if (bufferService == null)
        {
            throw new ArgumentNullException(nameof(bufferService));
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

        IReadOnlyList<Buffer> buffers = bufferService.LoadAll();
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
        hotkeyManager.RegisterDefaultHotkey();

        trayPeekWindow = new TrayPeekWindow();

        trayIcon = new TrayIcon(windowHandle);
        trayIcon.LeftClicked += OnTrayLeftClicked;
        trayIcon.RightClicked += OnTrayRightClicked;
        trayIcon.MouseHovered += OnTrayMouseHovered;
        trayIcon.MouseLeft += OnTrayMouseLeft;

        string trayTooltip = settingsService.TrayPeekEnabled
            ? string.Empty
            : AppConstants.AppName;
        trayIcon.Add(trayIconFilePath, trayTooltip);

        Closed += OnWindowClosed;
        Panel.PinToggleRequested += OnPanelPinToggleRequested;
        Panel.CloseRequested += OnPanelCloseRequested;

        isPinned = settingsService.IsPinned;
        ApplyPinState();
        Panel.SetPinned(isPinned);

        isInitialised = true;

        // Activate must be called once to initialise the XAML island before
        // the window is hidden via AppWindow.Hide().
        this.Activate();

#if DEBUG
        ShowPanel();
#else
        HidePanel();
#endif
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

    private void OnTrayMouseHovered(object sender, EventArgs e)
    {
        if (isContextMenuOpen)
        {
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

    private void ShowContextMenu()
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

        bool startupEnabled = startupService.IsEnabled();
        bool peekEnabled = settingsService != null && settingsService.TrayPeekEnabled;

        var menu = new TrayMenu();
        menu.Show(
            onOpen: () => ShowPanel(),
            onToggleStartup: () =>
            {
                if (startupService.IsEnabled())
                {
                    startupService.Disable();
                }
                else
                {
                    startupService.Enable();
                }
            },
            onTogglePeek: () =>
            {
                bool newPeekEnabled = !settingsService.TrayPeekEnabled;
                settingsService.TrayPeekEnabled = newPeekEnabled;
                string tooltip = newPeekEnabled
                    ? string.Empty
                    : AppConstants.AppName;
                if (trayIcon != null)
                {
                    trayIcon.SetTooltip(tooltip);
                }
                _ = settingsService.SaveAsync();
            },
            onAbout: () =>
            {
                ShowPanel();
                _ = ShowAboutDialog();
            },
            onExit: () => ExitApplication(),
            startupEnabled: startupEnabled,
            peekEnabled: peekEnabled,
            onClose: () =>
            {
                isContextMenuOpen = false;
            }
        );
    }

    private async Task ShowAboutDialog()
    {
        if (isAboutDialogOpen)
        {
            return;
        }

        if (Content == null || Content.XamlRoot == null)
        {
            return;
        }

        var dialog = new AboutDialog();
        dialog.XamlRoot = Content.XamlRoot;
        if (bufferService != null)
        {
            dialog.StorageDirectory = bufferService.AppDataDirectory;
        }

        isAboutDialogOpen = true;
        try
        {
            await dialog.ShowAsync();
        }
        catch (Exception)
        {
            // Dialog dismissed by the OS or XamlRoot invalidated; no user action needed.
        }
        finally
        {
            isAboutDialogOpen = false;
        }
    }

    private void Teardown()
    {
        isTornDown = true;

        if (originalWndProc != IntPtr.Zero)
        {
            NativeMethods.SetWindowLongPtr(windowHandle, NativeMethods.GWLP_WNDPROC, originalWndProc);
        }

        newWndProc = null;

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

        if (bufferService != null)
        {
            bufferService.FlushPendingWritesSync();
        }

        ((App)Application.Current).ReleaseSingleInstanceMutex();
    }

    private void ExitApplication()
    {
        Teardown();
        Environment.Exit(App.ExitCodeNormal);
    }

    private void TogglePanelVisibility()
    {
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

    private void ShowPanel()
    {
        if (appWindow != null)
        {
            appWindow.Show();
        }

        ApplyPinState();
        NativeMethods.SetForegroundWindow(windowHandle);
        Panel.FocusActiveEditor();
        isPanelVisible = true;
        isWindowActive = true;
    }

    private void HidePanel()
    {
        if (appWindow != null)
        {
            appWindow.Hide();
        }

        isPanelVisible = false;
        isWindowActive = false;
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
