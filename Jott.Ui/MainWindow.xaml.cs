using Jott.Ui.Interop;
using Jott.Ui.Services;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
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

    private const int PanelWidth = 420;
    private const int PanelHeight = 520;

    private const string AboutDialogTitle = "Jott";
    private const string AboutDialogContent = "Version 1.0.0\n\nHotkeys:\nShow / Hide: Win + `";
    private const string AboutDialogCloseButton = "OK";

    private NativeMethods.WndProcDelegate newWndProc;
    private IntPtr originalWndProc;
    private IntPtr windowHandle;
    private AppWindow appWindow;

    private BufferService bufferService;
    private StartupService startupService;
    private HotkeyManager hotkeyManager;
    private TrayIcon trayIcon;

    private bool isPanelVisible;

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
    public void Initialise(BufferService bufferService, string trayIconFilePath, StartupService startupService)
    {
        if (bufferService == null)
        {
            throw new ArgumentNullException(nameof(bufferService));
        }

        if (startupService == null)
        {
            throw new ArgumentNullException(nameof(startupService));
        }

        this.bufferService = bufferService;
        this.startupService = startupService;

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

        appWindow.Resize(new Windows.Graphics.SizeInt32(PanelWidth, PanelHeight));

        var presenter = appWindow.Presenter as OverlappedPresenter;
        if (presenter != null)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }
    }

    private void SubclassWindowProc()
    {
        newWndProc = WindowProc;
        var newWndProcPtr = Marshal.GetFunctionPointerForDelegate(newWndProc);
        originalWndProc = NativeMethods.SetWindowLongPtr(windowHandle, NativeMethods.GWLP_WNDPROC, newWndProcPtr);
    }

    private IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
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
        if (Content == null || Content.XamlRoot == null)
        {
            return;
        }

        var dialog = new ContentDialog();
        dialog.Title = AboutDialogTitle;
        dialog.Content = AboutDialogContent;
        dialog.CloseButtonText = AboutDialogCloseButton;
        dialog.XamlRoot = Content.XamlRoot;

        await dialog.ShowAsync();
    }

    private void Teardown()
    {
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
        Teardown();
    }
}
