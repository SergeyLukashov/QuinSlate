using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using QuinSlate.Ui.Interop;
using Serilog;
using System;

namespace QuinSlate.Ui.Tray;

/// <summary>
/// Runs the tray peek window's one-time startup warm-up: the freshly created window is shown
/// non-activated and fully transparent so the XAML island's first-ever composition happens at
/// startup instead of on the hover path, and is hidden again once that composition has
/// demonstrably finished. The hide deliberately does NOT happen on <c>Loaded</c>: Loaded only
/// means layout ran, while the render thread's first present can still be in flight, and an
/// <c>SW_HIDE</c> landing mid-first-present fail-fasts the process inside Microsoft.UI.Xaml.dll
/// (stowed exception 0xc000027b / E_UNEXPECTED) on slow or starved machines — see
/// Docs/Investigations/04-TRAY-PEEK-HOVER-FAILFAST-CRASH.md. Instead the hide waits for the
/// first <see cref="CompositionTarget.Rendered"/> tick after Loaded (the frame containing the
/// panel has been rendered and submitted) and then for the compositor to confirm a commit
/// cycle via <see cref="Microsoft.UI.Composition.Compositor.RequestCommitAsync"/>, after which
/// the first present is no longer in flight.
/// </summary>
public sealed class TrayPeekWarmUp
{
    private readonly TrayPeekPanel panel;
    private readonly IntPtr hwnd;

    private bool inFlight;
    private bool renderedHooked;

    /// <summary>
    /// Raised on the UI thread exactly once per started warm-up, when it either completes
    /// (window hidden again) or is cancelled by a hover takeover / teardown.
    /// </summary>
    public event EventHandler Completed;

    /// <summary>
    /// Creates a warm-up for the given peek panel and its window handle. The window must
    /// already be created, layered with alpha 0, and sized to its real dimensions.
    /// </summary>
    /// <param name="panel">The peek window's root panel, not yet loaded.</param>
    /// <param name="hwnd">The peek window's HWND.</param>
    public TrayPeekWarmUp(TrayPeekPanel panel, IntPtr hwnd)
    {
        this.panel = panel;
        this.hwnd = hwnd;
    }

    /// <summary>
    /// Shows the window non-activated (invisible at alpha 0) and begins waiting for its first
    /// composition to finish.
    /// </summary>
    public void Start()
    {
        inFlight = true;
        panel.Loaded += OnPanelLoaded;
        NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOWNOACTIVATE);
        Log.ForContext<TrayPeekWarmUp>().Debug("Peek warm-up started.");
    }

    /// <summary>
    /// Cancels a warm-up that has not completed yet, detaching its hooks without hiding the
    /// window (a hover takeover reuses it shown), and raises <see cref="Completed"/> so the
    /// host can lift any warm-up-scoped state. Safe to call repeatedly or after completion.
    /// </summary>
    public void Cancel()
    {
        if (inFlight == false)
        {
            return;
        }

        inFlight = false;
        panel.Loaded -= OnPanelLoaded;
        DetachRenderedHandler();
        Completed?.Invoke(this, EventArgs.Empty);
    }

    private void OnPanelLoaded(object sender, RoutedEventArgs e)
    {
        panel.Loaded -= OnPanelLoaded;

        if (inFlight == false)
        {
            return;
        }

        renderedHooked = true;
        CompositionTarget.Rendered += OnFrameRendered;
    }

    private async void OnFrameRendered(object sender, RenderedEventArgs e)
    {
        DetachRenderedHandler();

        if (inFlight == false)
        {
            return;
        }

        // The frame containing the loaded panel has been rendered and submitted; now wait for
        // the compositor to confirm a commit cycle, i.e. the first present is no longer in
        // flight. Rendered alone is not enough (it reports the UI thread's render pass, not
        // the compositor's), and waiting for further Rendered ticks could stall forever on an
        // idle thread since Rendered only reports frames, it does not force them. The await
        // also moves the hide out of the frame-event dispatch into a normal dispatcher
        // continuation.
        await CompositionTarget.GetCompositorForCurrentThread().RequestCommitAsync();

        if (inFlight == false)
        {
            return;
        }

        inFlight = false;
        NativeMethods.ShowWindow(hwnd, NativeMethods.SW_HIDE);
        Log.ForContext<TrayPeekWarmUp>().Debug("Peek warm-up complete; window hidden.");
        Completed?.Invoke(this, EventArgs.Empty);
    }

    private void DetachRenderedHandler()
    {
        if (renderedHooked)
        {
            renderedHooked = false;
            CompositionTarget.Rendered -= OnFrameRendered;
        }
    }
}
