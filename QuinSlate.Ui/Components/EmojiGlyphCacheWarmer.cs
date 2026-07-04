using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using QuinSlate.Ui.Layout;
using QuinSlate.Ui.Models;
using Serilog;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace QuinSlate.Ui.Components;

/// <summary>
/// Pre-rasterizes every picker emoji into the process-wide glyph cache before
/// the picker is first opened, so opening and searching draw cached glyphs
/// (~sub-millisecond each) instead of paying first-time color-glyph
/// rasterization (~several milliseconds each, ~3.5 s for the full set).
///
/// The warmer hosts throwaway glyph TextBlocks in a 1×1-clipped,
/// non-interactive host inside the live window — measured to rasterize
/// despite being invisible, unlike Opacity=0, which the compositor culls —
/// and reveals them a few per frame so the render thread is never stalled by
/// a bulk rasterization. When every glyph has been revealed the host is
/// removed and all references dropped; only the process glyph cache remains.
/// </summary>
internal sealed class EmojiGlyphCacheWarmer
{
    /// <summary>
    /// Glyphs revealed per frame while warming. Cold color-glyph
    /// rasterization costs several milliseconds per glyph, so this is kept
    /// small enough (~10-15 ms of render work per frame) that warming never
    /// drops the window below a fluid frame rate while the user may already
    /// be typing.
    /// </summary>
    private const int WarmSliceSize = 2;

    private const double MillisecondsPerSecond = 1000;

    /// <summary>
    /// Delay before warming starts, so it never competes with panel startup
    /// rendering (dithered background build, first layout, show animation).
    /// </summary>
    private static readonly TimeSpan WarmStartDelay = TimeSpan.FromSeconds(2);

    private readonly Stopwatch warmStopwatch = new Stopwatch();

    private Panel hostPanel;
    private Border warmHost;
    private List<TextBlock> warmGlyphs;
    private int revealedCount;
    private bool isTicking;
    private bool isCompleted;
    private double maxFrameDeltaMs;
    private long lastFrameTimestamp;
    private bool hasFrameTimestamp;

    /// <summary>Gets whether every glyph has been rasterized and the host removed.</summary>
    internal bool IsCompleted => isCompleted;

    /// <summary>
    /// Builds the hidden warm-up host inside <paramref name="panel"/> and
    /// schedules the paced reveal after <see cref="WarmStartDelay"/>. The
    /// reveal is frame-driven, so a hidden window simply defers it until the
    /// window first renders. Subsequent calls are no-ops.
    /// </summary>
    internal void Start(Panel panel)
    {
        if (panel == null || isCompleted || warmHost != null)
        {
            return;
        }

        hostPanel = panel;
        warmGlyphs = new List<TextBlock>(EmojiData.GetAllEntries().Count);

        var canvas = new Canvas
        {
            Width = EmojiSheetLayoutCalculator.SheetWidth,
            IsHitTestVisible = false,
        };

        IReadOnlyList<EmojiEntry> entries = EmojiData.GetAllEntries();
        EmojiSheetLayout layout = EmojiSheetLayoutCalculator.ComputeSearchLayout(entries.Count);

        for (int i = 0; i < entries.Count; i++)
        {
            TextBlock glyph = EmojiGlyphFactory.CreateGlyph(entries[i].Emoji);
            EmojiGlyphFactory.PlaceGlyph(glyph, layout.Cells[i], glyph.DesiredSize);
            glyph.Visibility = Visibility.Collapsed;
            canvas.Children.Add(glyph);
            warmGlyphs.Add(glyph);
        }

        canvas.Height = layout.TotalHeight;

        warmHost = new Border
        {
            Child = canvas,
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, 1, 1) },
        };

        hostPanel.Children.Add(warmHost);

        var startTimer = hostPanel.DispatcherQueue.CreateTimer();
        startTimer.Interval = WarmStartDelay;
        startTimer.IsRepeating = false;
        startTimer.Tick += (s, e) => BeginTicking();
        startTimer.Start();
    }

    private void BeginTicking()
    {
        if (isTicking || isCompleted || warmHost == null)
        {
            return;
        }

        isTicking = true;
        hasFrameTimestamp = false;
        warmStopwatch.Start();
        CompositionTarget.Rendering += OnWarmFrameRendering;
    }

    private void OnWarmFrameRendering(object sender, object e)
    {
        TrackFrameDelta();

        int end = Math.Min(revealedCount + WarmSliceSize, warmGlyphs.Count);
        for (int i = revealedCount; i < end; i++)
        {
            warmGlyphs[i].Visibility = Visibility.Visible;
        }

        revealedCount = end;
        if (revealedCount >= warmGlyphs.Count)
        {
            FinishWarm();
        }
    }

    private void TrackFrameDelta()
    {
        long timestamp = Stopwatch.GetTimestamp();
        if (hasFrameTimestamp)
        {
            double deltaMs = (timestamp - lastFrameTimestamp) * MillisecondsPerSecond / Stopwatch.Frequency;
            if (deltaMs > maxFrameDeltaMs)
            {
                maxFrameDeltaMs = deltaMs;
            }
        }

        lastFrameTimestamp = timestamp;
        hasFrameTimestamp = true;
    }

    private void FinishWarm()
    {
        CompositionTarget.Rendering -= OnWarmFrameRendering;
        isTicking = false;
        isCompleted = true;
        warmStopwatch.Stop();

        int glyphCount = warmGlyphs.Count;
        warmHost.Child = null;
        hostPanel.Children.Remove(warmHost);
        warmHost = null;
        hostPanel = null;
        warmGlyphs = null;

        Log.ForContext<EmojiGlyphCacheWarmer>().Information(
            "Emoji glyph cache warmed in {ElapsedMs:F0} ms: {GlyphCount} glyphs, max frame delta {MaxFrameDeltaMs:F1} ms.",
            warmStopwatch.Elapsed.TotalMilliseconds,
            glyphCount,
            maxFrameDeltaMs);
    }
}
