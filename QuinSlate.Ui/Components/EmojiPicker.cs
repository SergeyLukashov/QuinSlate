using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Serilog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Windows.Foundation;

namespace QuinSlate.Ui.Components;

/// <summary>
/// Hosts the <see cref="EmojiPickerView"/> inside a flyout and raises
/// <see cref="EmojiSelected"/> when the user picks an emoji. Building the view
/// builds the entire static sprite sheet, so <see cref="Prewarm"/> can pay that
/// one-time cost during idle time instead of on the first open.
/// </summary>
public sealed class EmojiPicker
{
    private const double SettleQuietFrameThresholdMs = 22;
    private const int SettleQuietFrameCount = 12;
    private const double SettleTimeoutMs = 8000;
    private const double MillisecondsPerSecond = 1000;

    private readonly EmojiSpriteAtlas spriteAtlas = new EmojiSpriteAtlas();

    private Flyout cachedFlyout;
    private EmojiPickerView cachedView;
    private bool wasPrewarmed;
    private bool hasOpenedOnce;

    /// <summary>Raised when the user selects an emoji. The argument is the selected emoji string.</summary>
    public event EventHandler<string> EmojiSelected;

    /// <summary>
    /// Builds the picker ahead of time so the first open pays no construction
    /// cost, and starts decoding the pre-rasterized sprite atlas for
    /// <paramref name="xamlRoot"/>'s rasterization scale — the sprites replace
    /// runtime colour-glyph font rasterization, the historically dominant
    /// first-open cost. The view is constructed disconnected and given a
    /// disconnected measure/arrange pass to force the remaining text and
    /// template layout. Safe to call at any time; a no-op once the picker is
    /// built, including when a first open beat the prewarm to it.
    /// </summary>
    /// <param name="xamlRoot">The XAML root whose rasterization scale the sprites are decoded for.</param>
    public void Prewarm(XamlRoot xamlRoot)
    {
        if (xamlRoot != null)
        {
            spriteAtlas.EnsureLoaded(xamlRoot.RasterizationScale);
        }

        if (cachedFlyout != null)
        {
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        EnsurePickerBuilt();
        bool disconnectedLayout = TryDisconnectedLayout(cachedView);
        wasPrewarmed = true;

        Log.ForContext<EmojiPicker>().Information(
            "Emoji picker prewarm completed in {ElapsedMs:F1} ms (disconnected layout pass: {DisconnectedLayout}).",
            stopwatch.Elapsed.TotalMilliseconds,
            disconnectedLayout);
    }

    /// <summary>
    /// Opens the emoji picker anchored to <paramref name="anchor"/>.
    /// The recent list is refreshed from <paramref name="recentEmoji"/> on every open.
    /// </summary>
    public void Open(Button anchor, IReadOnlyList<string> recentEmoji)
    {
        if (anchor == null)
        {
            return;
        }

        if (anchor.XamlRoot != null)
        {
            spriteAtlas.EnsureLoaded(anchor.XamlRoot.RasterizationScale);
        }

        bool isFirstOpen = !hasOpenedOnce;
        var stopwatch = isFirstOpen ? Stopwatch.StartNew() : null;

        EnsurePickerBuilt();

        cachedView.Reset(recentEmoji);

        FlyoutBase.SetAttachedFlyout(anchor, cachedFlyout);
        FlyoutBase.ShowAttachedFlyout(anchor);

        if (isFirstOpen)
        {
            hasOpenedOnce = true;
            Log.ForContext<EmojiPicker>().Debug(
                "Emoji picker first open UI-thread work took {ElapsedMs:F1} ms.",
                stopwatch.Elapsed.TotalMilliseconds);
            LogFirstOpenWhenSettled(stopwatch);
        }
    }

    /// <summary>
    /// Logs the first-open experience once rendering settles: the time to the
    /// first presented frame, the time until frame deltas stay below
    /// <see cref="SettleQuietFrameThresholdMs"/> for
    /// <see cref="SettleQuietFrameCount"/> consecutive frames, and the worst
    /// frame delta seen. Measuring only the UI-thread portion is misleading:
    /// the expensive part of a first open is render-thread work that happens
    /// after ShowAttachedFlyout returns and shows up as long gaps between
    /// rendering ticks.
    /// </summary>
    private void LogFirstOpenWhenSettled(Stopwatch stopwatch)
    {
        double firstTickMs = -1;
        double settleCandidateMs = 0;
        double maxDeltaMs = 0;
        long lastTimestamp = 0;
        bool hasLastTimestamp = false;
        int quietFrames = 0;

        EventHandler<object> handler = null;
        handler = (sender, args) =>
        {
            double elapsedMs = stopwatch.Elapsed.TotalMilliseconds;
            long timestamp = Stopwatch.GetTimestamp();

            if (firstTickMs < 0)
            {
                firstTickMs = elapsedMs;
                settleCandidateMs = elapsedMs;
            }

            if (hasLastTimestamp)
            {
                double deltaMs = (timestamp - lastTimestamp) * MillisecondsPerSecond / Stopwatch.Frequency;
                if (deltaMs > maxDeltaMs)
                {
                    maxDeltaMs = deltaMs;
                }

                if (deltaMs < SettleQuietFrameThresholdMs)
                {
                    quietFrames++;
                }
                else
                {
                    quietFrames = 0;
                    settleCandidateMs = elapsedMs;
                }
            }

            lastTimestamp = timestamp;
            hasLastTimestamp = true;

            if (quietFrames >= SettleQuietFrameCount || elapsedMs > SettleTimeoutMs)
            {
                CompositionTarget.Rendering -= handler;
                Log.ForContext<EmojiPicker>().Information(
                    "Emoji picker first open: first frame {FirstFrameMs:F1} ms, settled {SettleMs:F1} ms, max frame delta {MaxDeltaMs:F1} ms (prewarmed: {WasPrewarmed}, sprite atlas loaded: {AtlasLoaded}).",
                    firstTickMs,
                    settleCandidateMs,
                    maxDeltaMs,
                    wasPrewarmed,
                    spriteAtlas.IsLoaded);
            }
        };
        CompositionTarget.Rendering += handler;
    }

    private void EnsurePickerBuilt()
    {
        if (cachedFlyout != null)
        {
            return;
        }

        cachedView = new EmojiPickerView(spriteAtlas);
        cachedView.EmojiClicked += OnEmojiClicked;

        var presenterStyle = new Style(typeof(FlyoutPresenter));
        presenterStyle.Setters.Add(new Setter(Control.CornerRadiusProperty, new CornerRadius(8)));

        var transitions = new TransitionCollection
        {
            new EntranceThemeTransition
            {
                FromVerticalOffset = 8,
                FromHorizontalOffset = 0
            }
        };
        presenterStyle.Setters.Add(new Setter(UIElement.TransitionsProperty, transitions));

        cachedFlyout = new Flyout
        {
            Content = cachedView,
            Placement = FlyoutPlacementMode.Bottom,
            FlyoutPresenterStyle = presenterStyle,
            AreOpenCloseAnimationsEnabled = false,
        };
    }

    private static bool TryDisconnectedLayout(EmojiPickerView view)
    {
        try
        {
            view.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            view.Arrange(new Rect(new Point(0, 0), view.DesiredSize));
            return true;
        }
        catch (Exception ex)
        {
            Log.ForContext<EmojiPicker>().Debug(
                ex, "Disconnected emoji picker layout pass failed; prewarm degraded to construct-only.");
            return false;
        }
    }

    private void OnEmojiClicked(object sender, string emoji)
    {
        if (cachedFlyout != null)
        {
            cachedFlyout.Hide();
        }

        EmojiSelected?.Invoke(this, emoji);
    }
}
