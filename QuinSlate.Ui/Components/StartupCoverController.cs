using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;

namespace QuinSlate.Ui.Components;

/// <summary>
/// Runs the flat startup cover over the editor: reports the page's first paint, drops the cover
/// once the reveal entrance starts (or a bounded fallback fires), and reports the moment the
/// launch-critical rendering has settled.
/// </summary>
internal sealed class StartupCoverController
{
    private const int StartupCoverFallbackMs = 2500;

    private readonly Border cover;
    private readonly EditorHost editorHost;

    /// <summary>
    /// Fallback that drops the startup cover even if the reveal entrance never starts (e.g. the
    /// page never paints), so the editor area can never stay stuck showing the flat cover.
    /// </summary>
    private DispatcherTimer startupCoverTimer;

    /// <summary>
    /// Whether <see cref="RenderSettled"/> has already been raised, so repeat paint
    /// notifications from the page (every background rebuild reposts one) raise it only once.
    /// </summary>
    private bool hasStartupRenderSettled;

    /// <summary>
    /// Whether <see cref="FirstPainted"/> has already been raised (the page reposts a
    /// paint notification after every background rebuild; only the first one is the startup
    /// first paint).
    /// </summary>
    private bool hasSeenFirstPaint;

    /// <summary>
    /// Whether the page ran the startup reveal entrance. When it did, the entrance already
    /// released the caret-blink hold; the fallback cover drop must release it itself.
    /// </summary>
    private bool hasEntranceRun;

    /// <summary>Raised once, when the editor page reports its first composited frame.</summary>
    public event EventHandler FirstPainted;

    /// <summary>
    /// Raised once, when the startup cover drops: the editor's reveal entrance has started (or the
    /// bounded fallback fired, including on a WebView2 creation failure).
    /// </summary>
    public event EventHandler RenderSettled;

    /// <summary>
    /// Creates the controller for <paramref name="cover"/> and starts listening to
    /// <paramref name="editorHost"/>'s paint, entrance, and creation-failure notifications.
    /// </summary>
    public StartupCoverController(Border cover, EditorHost editorHost)
    {
        if (editorHost == null)
        {
            throw new ArgumentNullException(nameof(editorHost));
        }

        this.cover = cover;
        this.editorHost = editorHost;

        editorHost.Painted += OnEditorPainted;
        editorHost.EntranceStarted += OnEditorEntranceStarted;
        editorHost.CreationFailed += OnEditorCreationFailed;
    }

    /// <summary>Whether the cover has dropped and <see cref="RenderSettled"/> has been raised.</summary>
    public bool HasRenderSettled => hasStartupRenderSettled;

    /// <summary>
    /// Arms the bounded fallback that drops the cover should the reveal entrance never start. Does
    /// nothing once armed, or when the cover is already down.
    /// </summary>
    public void StartFallback()
    {
        if (startupCoverTimer != null || cover == null || cover.Visibility != Visibility.Visible)
        {
            return;
        }

        startupCoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(StartupCoverFallbackMs) };
        startupCoverTimer.Tick += (s, e) => DropStartupCover();
        startupCoverTimer.Start();
    }

    private void OnEditorPainted(object sender, EventArgs e)
    {
        if (hasSeenFirstPaint)
        {
            return;
        }

        hasSeenFirstPaint = true;
        FirstPainted?.Invoke(this, EventArgs.Empty);
    }

    private void OnEditorEntranceStarted(object sender, EventArgs e)
    {
        hasEntranceRun = true;
        DropStartupCover();
    }

    /// <summary>
    /// Degrades to the flat panel when the WebView2 could not be created at all: the cover (and
    /// the deferred warm-ups behind <see cref="RenderSettled"/>) must not wait for a first
    /// paint that will never come.
    /// </summary>
    private void OnEditorCreationFailed(object sender, EventArgs e)
    {
        DropStartupCover();
    }

    /// <summary>
    /// Hides the flat startup cover once the editor page has started its reveal entrance (or after
    /// a bounded fallback delay), revealing the gradient + text without the black WebView2 swapchain
    /// ever showing. Idempotent.
    /// </summary>
    private void DropStartupCover()
    {
        if (startupCoverTimer != null)
        {
            startupCoverTimer.Stop();
            startupCoverTimer = null;
        }

        if (cover != null)
        {
            cover.Visibility = Visibility.Collapsed;
        }

        if (hasStartupRenderSettled)
        {
            return;
        }

        hasStartupRenderSettled = true;

        // On the normal path the reveal entrance already released the page's caret-blink hold.
        // When the cover drops without an entrance (the bounded fallback), release it here so
        // the caret can never stay frozen solid.
        if (hasEntranceRun == false && editorHost != null)
        {
            editorHost.ResetCaretBlink();
        }

        RenderSettled?.Invoke(this, EventArgs.Empty);
    }
}
