using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using QuinSlate.Ui.Interop;
using System;
using System.Runtime.InteropServices;
using Windows.Graphics;
using Windows.System;
using WinRT.Interop;

namespace QuinSlate.Ui.Components;

/// <summary>
/// A small, borderless, owned top-level window that hosts the <see cref="AboutView"/>
/// card and behaves like a classic modal dialog. Living in its own window (rather than
/// a <c>ContentDialog</c> bounded to the main window's content area) lets the card always
/// render at its natural size, even when the main window has been resized smaller than it.
/// </summary>
/// <remarks>
/// The window disables its owner while open (modal input blocking), paints its bare Win32
/// surface during <c>WM_ERASEBKGND</c> so it never flashes black before the WinUI compositor
/// presents, fades in from a layered window so the appearance is smooth, and exposes the
/// card's header as a caption region so the window can be dragged by it.
/// </remarks>
public sealed class AboutWindow : Window
{
    /// <summary>
    /// The card's fixed size in DIPs. The card lays out to exactly this size — a single-line
    /// data path keeps its height constant — so the window sizes itself to these constants
    /// instead of measuring the content. The body row absorbs any sub-pixel slack, keeping the
    /// footer flush at the bottom.
    /// </summary>
    private const double CardWidthLogical = 480;
    private const double CardHeightLogical = 440;

    /// <summary>
    /// Height in DIPs of the card's draggable header bar. Must match the <c>AboutBarHeight</c>
    /// resource in <c>AboutResources.xaml</c>.
    /// </summary>
    private const double HeaderBarHeightLogical = 44;

    private const int AnimationSteps = 8;
    private const int AnimationIntervalMs = 16;
    private const byte FullyTransparentAlpha = 0;
    private const byte FullyOpaqueAlpha = 255;

    /// <summary>
    /// Width in DIPs reserved on the right of the header for the close button, kept out of the
    /// draggable caption region so the button still receives clicks.
    /// </summary>
    private const double CloseButtonZoneLogical = 56;

    private readonly AboutView view;
    private readonly IntPtr ownerHwnd;
    private readonly IntPtr hwnd;
    private readonly AppWindow appWindow;
    private readonly float scale;
    private readonly WindowBackgroundBrush backgroundBrush;

    private InputNonClientPointerSource nonClientPointerSource;
    private NativeMethods.WndProcDelegate subclassProc;
    private IntPtr originalWndProc;
    private DispatcherTimer fadeTimer;
    private int animationStep;
    private bool fadeStarted;
    private bool ownerDisabled;
    private bool closed;

    /// <summary>
    /// Creates the About window owned by <paramref name="ownerHwnd"/> (so it floats above
    /// the main window and closes with it), centred over it, and modal against it.
    /// </summary>
    /// <param name="ownerHwnd">HWND of the main window that owns this dialog.</param>
    /// <param name="storageDirectory">Live data directory to display in the card.</param>
    public AboutWindow(IntPtr ownerHwnd, string storageDirectory)
    {
        this.ownerHwnd = ownerHwnd;

        view = new AboutView();
        view.StorageDirectory = storageDirectory;
        view.CloseRequested += OnCloseRequested;
        view.Loaded += OnViewLoaded;
        AddEscapeAccelerator();
        Content = view;

        hwnd = WindowNative.GetWindowHandle(this);
        WindowId windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        appWindow = AppWindow.GetFromWindowId(windowId);

        scale = NativeMethods.GetDpiForWindow(ownerHwnd) / 96.0f;

        backgroundBrush = new WindowBackgroundBrush(
            DitheredGradientBrushFactory.MidColor(false),
            DitheredGradientBrushFactory.MidColor(true));
        backgroundBrush.SetTheme(view.ActualTheme != ElementTheme.Light);

        SubclassWndProc();
        ConfigurePresenter();
        ApplyOwnedToolWindowStyles();
        NativeMethods.SetRoundedCornerPreference(hwnd);
        BeginHiddenForFade();
        ApplySizeAndPosition();
        UpdateDragRegion();

        // The owner is deliberately NOT disabled here. Disabling it before this dialog wins the
        // foreground lets the tray menu's foreground reshuffle skip the disabled owner and flash an
        // unrelated app on top; the launcher disables it via DisableOwnerForModality once that
        // reshuffle has settled.
        Closed += OnClosed;
    }

    /// <summary>
    /// Re-asserts this window as the foreground window. <see cref="Window.Activate"/> on an
    /// already-shown window does not reliably re-raise it, so without this a repeat About request
    /// would leave the modal — and the owner it floats above — buried behind other apps' windows.
    /// </summary>
    public void BringToForeground()
    {
        IntPtr foreground = NativeMethods.GetForegroundWindow();
        if (foreground == hwnd)
        {
            return;
        }

        // SetForegroundWindow is refused — or instantly reverted — when another process owns the
        // foreground lock. Temporarily attaching this thread's input to the foreground window's
        // thread lifts that lock, so the modal is raised reliably instead of only flashing.
        uint thisThread = NativeMethods.GetCurrentThreadId();
        uint foregroundThread = foreground == IntPtr.Zero
            ? 0u
            : NativeMethods.GetWindowThreadProcessId(foreground, out _);

        bool attached = foregroundThread != 0u
            && foregroundThread != thisThread
            && NativeMethods.AttachThreadInput(thisThread, foregroundThread, true);

        NativeMethods.SetForegroundWindow(hwnd);

        if (attached)
        {
            NativeMethods.AttachThreadInput(thisThread, foregroundThread, false);
        }
    }

    private void AddEscapeAccelerator()
    {
        var escape = new KeyboardAccelerator { Key = VirtualKey.Escape };
        escape.Invoked += OnEscapeInvoked;
        view.KeyboardAccelerators.Add(escape);

        // Suppress WinUI's automatic accelerator tooltip ("Escape"), which otherwise appears
        // over the controls in the card whenever they are hovered or focused.
        view.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;
    }

    private void OnEscapeInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        Close();
    }

    private void OnCloseRequested(object sender, EventArgs e)
    {
        Close();
    }

    private void SubclassWndProc()
    {
        subclassProc = WindowProc;
        IntPtr newProcPtr = Marshal.GetFunctionPointerForDelegate(subclassProc);
        originalWndProc = NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWLP_WNDPROC, newProcPtr);
    }

    private IntPtr WindowProc(IntPtr windowHandle, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == NativeMethods.WM_ERASEBKGND)
        {
            if (backgroundBrush != null && backgroundBrush.Erase(windowHandle, wParam))
            {
                return new IntPtr(1);
            }
        }

        return NativeMethods.CallWindowProc(originalWndProc, windowHandle, msg, wParam, lParam);
    }

    /// <summary>
    /// Declares the header (minus the close-button zone on the right) as a caption region so the
    /// borderless window can be dragged by it. WinUI hosts its content in a child island HWND, so
    /// a top-level WM_NCHITTEST never sees the header — caption regions are the supported route.
    /// </summary>
    private void UpdateDragRegion()
    {
        if (appWindow == null)
        {
            return;
        }

        if (nonClientPointerSource == null)
        {
            nonClientPointerSource = InputNonClientPointerSource.GetForWindowId(appWindow.Id);
        }

        int headerHeight = (int)Math.Round(HeaderBarHeightLogical * scale);
        int captionWidth = (int)Math.Round((CardWidthLogical - CloseButtonZoneLogical) * scale);
        if (headerHeight <= 0 || captionWidth <= 0)
        {
            return;
        }

        var caption = new RectInt32(0, 0, captionWidth, headerHeight);
        nonClientPointerSource.SetRegionRects(NonClientRegionKind.Caption, new RectInt32[] { caption });
    }

    private void ConfigurePresenter()
    {
        if (appWindow == null)
        {
            return;
        }

        var presenter = OverlappedPresenter.CreateForDialog();
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.SetBorderAndTitleBar(false, false);
        appWindow.SetPresenter(presenter);
    }

    /// <summary>
    /// Makes the window owned by the main window and removes its taskbar button so it
    /// behaves like a dialog rather than a peer application window.
    /// </summary>
    private void ApplyOwnedToolWindowStyles()
    {
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWLP_HWNDPARENT, ownerHwnd);
        SetExStyleBits(NativeMethods.WS_EX_TOOLWINDOW, NativeMethods.WS_EX_APPWINDOW);
    }

    /// <summary>
    /// Marks the window layered and fully transparent so the fade-in starts from invisible.
    /// This also hides the brief gap before the WinUI compositor presents its first frame.
    /// </summary>
    private void BeginHiddenForFade()
    {
        SetExStyleBits(NativeMethods.WS_EX_LAYERED, 0);
        NativeMethods.SetLayeredWindowAttributes(hwnd, 0, FullyTransparentAlpha, NativeMethods.LWA_ALPHA);
    }

    /// <summary>
    /// Sets and clears the given extended-style bits of this window in one call.
    /// </summary>
    private void SetExStyleBits(uint bitsToSet, uint bitsToClear)
    {
        IntPtr exStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE);
        long bits = exStyle.ToInt64();
        bits |= bitsToSet;
        bits &= ~(long)bitsToClear;
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(bits));
    }

    private void ApplySizeAndPosition()
    {
        int width = (int)Math.Round(CardWidthLogical * scale);
        int height = (int)Math.Round(CardHeightLogical * scale);
        MoveCenteredOnOwner(width, height);
    }

    /// <summary>
    /// Disables the owner window so this dialog blocks input to it (classic Win32 modal
    /// behaviour). Called by the launcher after the tray menu has fully torn down — not from the
    /// constructor — so the owner stays enabled through the menu's foreground reshuffle and Windows
    /// does not skip it (which would flash an unrelated app above the panel). Idempotent.
    /// </summary>
    public void DisableOwnerForModality()
    {
        DisableOwner();
    }

    private void DisableOwner()
    {
        if (ownerHwnd == IntPtr.Zero || ownerDisabled)
        {
            return;
        }

        NativeMethods.EnableWindow(ownerHwnd, false);
        ownerDisabled = true;
    }

    private void OnViewLoaded(object sender, RoutedEventArgs e)
    {
        // The window is already sized and positioned (the card is a fixed size); fade it in once
        // the content has laid out so the first painted frame is the finished card.
        if (fadeStarted)
        {
            return;
        }

        fadeStarted = true;
        StartFadeIn();
    }

    /// <summary>
    /// Positions the window of the given physical size centred over the owner window.
    /// </summary>
    private void MoveCenteredOnOwner(int width, int height)
    {
        if (appWindow == null)
        {
            return;
        }

        int x;
        int y;
        if (NativeMethods.GetWindowRect(ownerHwnd, out NativeMethods.RECT ownerRect))
        {
            int centreX = (ownerRect.Left + ownerRect.Right) / 2;
            int centreY = (ownerRect.Top + ownerRect.Bottom) / 2;
            x = centreX - (width / 2);
            y = centreY - (height / 2);
        }
        else
        {
            x = 0;
            y = 0;
        }

        appWindow.MoveAndResize(new RectInt32(x, y, width, height));
    }

    private void StartFadeIn()
    {
        animationStep = 0;
        fadeTimer = new DispatcherTimer();
        fadeTimer.Interval = TimeSpan.FromMilliseconds(AnimationIntervalMs);
        fadeTimer.Tick += OnFadeTick;
        fadeTimer.Start();
    }

    private void OnFadeTick(object sender, object e)
    {
        animationStep++;
        if (animationStep >= AnimationSteps)
        {
            StopFadeTimer();
            NativeMethods.SetLayeredWindowAttributes(hwnd, 0, FullyOpaqueAlpha, NativeMethods.LWA_ALPHA);

            // Drop the layered style once fully opaque so ClearType rendering is not degraded.
            SetExStyleBits(0, NativeMethods.WS_EX_LAYERED);
            return;
        }

        float t = (float)animationStep / AnimationSteps;
        float ease = 1.0f - (1.0f - t) * (1.0f - t);
        byte opacity = (byte)(FullyOpaqueAlpha * ease);
        NativeMethods.SetLayeredWindowAttributes(hwnd, 0, opacity, NativeMethods.LWA_ALPHA);
    }

    private void StopFadeTimer()
    {
        if (fadeTimer == null)
        {
            return;
        }

        fadeTimer.Stop();
        fadeTimer.Tick -= OnFadeTick;
        fadeTimer = null;
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        if (closed)
        {
            return;
        }

        closed = true;

        StopFadeTimer();

        view.CloseRequested -= OnCloseRequested;
        view.Loaded -= OnViewLoaded;

        if (ownerDisabled && ownerHwnd != IntPtr.Zero)
        {
            NativeMethods.EnableWindow(ownerHwnd, true);
            NativeMethods.SetForegroundWindow(ownerHwnd);
            ownerDisabled = false;
        }

        if (originalWndProc != IntPtr.Zero)
        {
            NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWLP_WNDPROC, originalWndProc);
            originalWndProc = IntPtr.Zero;
        }

        subclassProc = null;

        if (backgroundBrush != null)
        {
            backgroundBrush.Dispose();
        }
    }
}
