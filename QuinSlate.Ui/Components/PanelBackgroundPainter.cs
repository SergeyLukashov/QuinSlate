using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using Windows.UI;

namespace QuinSlate.Ui.Components;

/// <summary>
/// Paints the buffer panel's window and editor surfaces with the dithered gradient mesh, keeping the
/// flat mid-tone on every surface until both meshes can swap in together. See
/// <c>Docs/Wiki/05-BACKGROUND-GRADIENT-DITHERING.md</c>.
/// </summary>
internal sealed class PanelBackgroundPainter
{
    private const int DitheredRebuildDebounceMs = 90;

    private readonly FrameworkElement themeSource;
    private readonly Panel window;
    private readonly FrameworkElement editorElement;
    private readonly Border startupCover;
    private readonly EditorHost editorHost;
    private readonly EditorThemeColors themeColors;

    /// <summary>
    /// Debounces rebuilding the dithered background surfaces while the window is being resized.
    /// The surfaces must be re-rendered at the new native pixel size (a stretched dithered bitmap
    /// re-bands), but doing so on every <c>SizeChanged</c> tick would thrash, so the rebuild is
    /// coalesced to the end of the resize.
    /// </summary>
    private DispatcherTimer ditheredRebuildTimer;

    /// <summary>
    /// True while a one-shot retry of <see cref="ApplyDitheredBackground"/> is pending because the
    /// editor host was not laid out yet (see <see cref="ScheduleDitheredRetry"/>).
    /// </summary>
    private bool isDitheredRetryScheduled;

    /// <summary>Creates the painter.</summary>
    /// <param name="themeSource">The element whose <c>ActualTheme</c> picks the mesh.</param>
    /// <param name="window">The panel's root, painted with the window mesh.</param>
    /// <param name="editorElement">The WebView2 the editor mesh is sized to.</param>
    /// <param name="startupCover">The flat cover over the editor, kept on the mid-tone while it shows.</param>
    /// <param name="editorHost">The editor bridge the editor mesh is sent over.</param>
    /// <param name="themeColors">The editor's foreground colours, sent alongside every mesh.</param>
    public PanelBackgroundPainter(
        FrameworkElement themeSource,
        Panel window,
        FrameworkElement editorElement,
        Border startupCover,
        EditorHost editorHost,
        EditorThemeColors themeColors)
    {
        if (themeSource == null)
        {
            throw new ArgumentNullException(nameof(themeSource));
        }

        if (window == null)
        {
            throw new ArgumentNullException(nameof(window));
        }

        if (editorElement == null)
        {
            throw new ArgumentNullException(nameof(editorElement));
        }

        if (themeColors == null)
        {
            throw new ArgumentNullException(nameof(themeColors));
        }

        this.themeSource = themeSource;
        this.window = window;
        this.editorElement = editorElement;
        this.startupCover = startupCover;
        this.editorHost = editorHost;
        this.themeColors = themeColors;
    }

    /// <summary>
    /// Rebuilds the meshes once the window has stopped resizing, at the new native pixel size.
    /// </summary>
    public void ScheduleRebuild()
    {
        if (ditheredRebuildTimer == null)
        {
            ditheredRebuildTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(DitheredRebuildDebounceMs),
            };
            ditheredRebuildTimer.Tick += OnDitheredRebuildTimerTick;
        }

        ditheredRebuildTimer.Stop();
        ditheredRebuildTimer.Start();
    }

    /// <summary>
    /// Builds the dithered gradient at each surface's native pixel size and applies it to the window
    /// background (a brush) and the editor page (a PNG shown 1:1) in the same pass. The swap is
    /// all-or-nothing: until the editor page is ready and its bitmap is built, every surface keeps
    /// the flat mid-tone (the window fallback brush and the WebView2 DefaultBackgroundColor), so no
    /// window-mesh-before-editor-mesh snap can occur.
    /// </summary>
    public async void ApplyDitheredBackground()
    {
        if (editorHost == null || editorHost.IsReady == false)
        {
            ApplyFallbackBackground();
            return;
        }

        // The foreground colours do not depend on the gradient bitmap, so send them first. Gating them
        // on the bitmap would leave the page on the previous theme's text colour whenever the bitmap
        // is late (editor not yet laid out) or fails outright.
        themeColors.Apply();

        DitheredBackground background = await DitheredGradientBrushFactory.CreateElementBackgroundAsync(editorElement);
        if (background == null)
        {
            ApplyFallbackBackground();
            ScheduleDitheredRetry();
            return;
        }

        ImageBrush windowBrush = DitheredGradientBrushFactory.CreateForElement(window);
        if (windowBrush != null)
        {
            window.Background = windowBrush;
        }

        editorHost.SetBackground(background);
    }

    /// <summary>
    /// Paints the window with the flat mid-tone of the gradient mesh. Applied synchronously before
    /// the panel is first shown (and whenever the dithered mesh is not yet available), so the first
    /// composited frame is a uniform flat tone rather than the banding XAML linear-gradient
    /// fallback. The editor's own flat tone comes from the WebView2 DefaultBackgroundColor.
    /// </summary>
    public void ApplyFallbackBackground()
    {
        bool isDark = themeSource.ActualTheme == ElementTheme.Dark;
        Color mid = DitheredGradientBrushFactory.MidColor(isDark);
        window.Background = new SolidColorBrush(mid);

        // Keep the startup cover matched to the mid-tone until the page reports its first paint, so
        // dropping it is imperceptible (mid-tone -> the barely-different mesh).
        if (startupCover != null && startupCover.Visibility == Visibility.Visible)
        {
            startupCover.Background = new SolidColorBrush(mid);
        }
    }

    private void OnDitheredRebuildTimerTick(object sender, object e)
    {
        ditheredRebuildTimer.Stop();
        ApplyDitheredBackground();
    }

    /// <summary>
    /// Re-runs <see cref="ApplyDitheredBackground"/> once the editor host can provide a size, on its
    /// next <c>SizeChanged</c>.
    /// </summary>
    private void ScheduleDitheredRetry()
    {
        if (isDitheredRetryScheduled)
        {
            return;
        }

        isDitheredRetryScheduled = true;
        SizeChangedEventHandler onSized = null;
        onSized = (s, e) =>
        {
            editorElement.SizeChanged -= onSized;
            isDitheredRetryScheduled = false;
            ApplyDitheredBackground();
        };
        editorElement.SizeChanged += onSized;
    }
}
