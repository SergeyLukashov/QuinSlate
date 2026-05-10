using Jott.Ui.Interop;
using Jott.Ui.Services;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using WinRT.Interop;
using Buffer = Jott.Ui.Models.Buffer;

namespace Jott.Ui;

/// <summary>
/// The main panel window. Hosts the buffer UI, owns the tray icon and global
/// hotkey, and subclasses its own <c>WndProc</c> so it can react to
/// <c>WM_HOTKEY</c> and the tray callback message.
/// </summary>
public sealed partial class MainWindow : Window
{
    /// <summary>
    /// Title of the main window. Used by secondary instances to locate the
    /// existing window via <c>FindWindow</c> when handing off activation.
    /// </summary>
    public const string WindowTitle = "JottMainWindow";

    private const int PanelDefaultWidth = 560;
    private const int PanelDefaultHeight = 680;
    private const int PanelMinWidth = 300;
    private const int PanelMinHeight = 400;
    private const int SettingsDebounceMilliseconds = 500;

    private Timer settingsDebounceTimer;
    private volatile int pendingSizeWidth;
    private volatile int pendingSizeHeight;
    private volatile int pendingPositionX;
    private volatile int pendingPositionY;
    private volatile bool isTornDown;

    private const string AboutDialogTitle = "Jott";
    private const string AboutDialogContent = "Version 1.0.0\n\nHotkeys:\nShow / Hide: Win + `";
    private const string AboutDialogCloseButton = "OK";

    private NativeMethods.WndProcDelegate newWndProc;
    private IntPtr originalWndProc;
    private IntPtr windowHandle;
    private AppWindow appWindow;

    private BufferService bufferService;
    private StartupService startupService;
    private SettingsService settingsService;
    private HotkeyManager hotkeyManager;
    private TrayIcon trayIcon;

    private bool isPanelVisible;
    private bool isAboutDialogOpen;

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
    /// </summary>
    public void Initialise(BufferService bufferService, string trayIconFilePath, StartupService startupService, SettingsService settingsService)
    {
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
        Panel.Initialise(bufferService, buffers);

        windowHandle = WindowNative.GetWindowHandle(this);
        ConfigureWindowAppearance();
        SubclassWindowProc();

        hotkeyManager = new HotkeyManager(windowHandle);
        hotkeyManager.RegisterDefaultHotkey();

        trayIcon = new TrayIcon(windowHandle);
        trayIcon.LeftClicked += OnTrayLeftClicked;
        trayIcon.RightClicked += OnTrayRightClicked;
        trayIcon.Add(trayIconFilePath, "Jott");

        Closed += OnWindowClosed;

        HidePanel();
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
        int logicalWidth = settingsService.WindowWidth > 0 ? settingsService.WindowWidth : PanelDefaultWidth;
        int logicalHeight = settingsService.WindowHeight > 0 ? settingsService.WindowHeight : PanelDefaultHeight;
        appWindow.Resize(new Windows.Graphics.SizeInt32(
            (int)Math.Round(logicalWidth * scale),
            (int)Math.Round(logicalHeight * scale)));

        if (settingsService.HasSavedPosition)
        {
            var pt = new NativeMethods.POINT { X = settingsService.WindowX, Y = settingsService.WindowY };
            if (NativeMethods.MonitorFromPoint(pt, NativeMethods.MONITOR_DEFAULTTONULL) != IntPtr.Zero)
            {
                appWindow.Move(new Windows.Graphics.PointInt32(settingsService.WindowX, settingsService.WindowY));
            }
        }

        var presenter = appWindow.Presenter as OverlappedPresenter;
        if (presenter != null)
        {
            presenter.IsResizable = true;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }

        appWindow.Changed += OnAppWindowChanged;
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (args.DidSizeChange == false && args.DidPositionChange == false)
        {
            return;
        }

        pendingSizeWidth = appWindow.Size.Width;
        pendingSizeHeight = appWindow.Size.Height;
        pendingPositionX = appWindow.Position.X;
        pendingPositionY = appWindow.Position.Y;

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
        float scale = NativeMethods.GetDpiForWindow(windowHandle) / 96.0f;
        int logicalWidth = (int)Math.Round(pendingSizeWidth / scale);
        int logicalHeight = (int)Math.Round(pendingSizeHeight / scale);
        int posX = pendingPositionX;
        int posY = pendingPositionY;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (isTornDown)
            {
                return;
            }

            settingsService.WindowWidth = logicalWidth;
            settingsService.WindowHeight = logicalHeight;
            settingsService.WindowX = posX;
            settingsService.WindowY = posY;
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

    private IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
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

        if (msg == NativeMethods.WM_HOTKEY)
        {
            if (hotkeyManager != null && wParam.ToInt32() == hotkeyManager.HotkeyIdentifier)
            {
                TogglePanelVisibility();
                return IntPtr.Zero;
            }
        }

        if (msg == NativeMethods.WM_JOTT_ACTIVATE)
        {
            DispatcherQueue.TryEnqueue(ShowPanel);
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
        DispatcherQueue.TryEnqueue(TogglePanelVisibility);
    }

    private void OnTrayRightClicked(object sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(ShowContextMenu);
    }

    private void ShowContextMenu()
    {
        if (startupService == null)
        {
            return;
        }

        bool startupEnabled = startupService.IsEnabled();
        var menu = new TrayMenu();
        uint command = menu.Show(windowHandle, startupEnabled);
        HandleMenuCommand(command, startupEnabled);
    }

    private async void HandleMenuCommand(uint command, bool startupWasEnabled)
    {
        if (command == NativeMethods.IDM_OPEN)
        {
            ShowPanel();
        }
        else if (command == NativeMethods.IDM_LAUNCH_STARTUP)
        {
            if (startupWasEnabled)
            {
                startupService.Disable();
            }
            else
            {
                startupService.Enable();
            }
        }
        else if (command == NativeMethods.IDM_ABOUT)
        {
            await ShowAboutDialog();
        }
        else if (command == NativeMethods.IDM_EXIT)
        {
            ExitApplication();
        }
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

        var dialog = new ContentDialog();
        dialog.Title = AboutDialogTitle;
        dialog.Content = AboutDialogContent;
        dialog.CloseButtonText = AboutDialogCloseButton;
        dialog.XamlRoot = Content.XamlRoot;

        isAboutDialogOpen = true;
        try
        {
            await dialog.ShowAsync();
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
            settingsService.WindowX = appWindow.Position.X;
            settingsService.WindowY = appWindow.Position.Y;
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
            HidePanel();
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

        NativeMethods.SetForegroundWindow(windowHandle);
        isPanelVisible = true;
    }

    private void HidePanel()
    {
        if (appWindow != null)
        {
            appWindow.Hide();
        }

        isPanelVisible = false;
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        args.Handled = true;
        HidePanel();
    }
}
