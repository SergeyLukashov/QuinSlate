using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using QuinSlate.Ui.Components;
using QuinSlate.Ui.Layout;
using QuinSlate.Ui.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.UI;

namespace QuinSlate.EmojiPickerBench;

public sealed partial class App : Application
{
    private Window window;
    private Grid host;
    private string scenario;
    private string outFile;

    public App()
    {
        InitializeComponent();
        UnhandledException += (s, e) =>
        {
            File.AppendAllText(
                Path.Combine(AppContext.BaseDirectory, "crash.log"),
                DateTime.Now + " " + e.Exception + " | " + e.Message + Environment.NewLine);
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        string[] cmd = Environment.GetCommandLineArgs();
        scenario = cmd.Length > 1 ? cmd[1] : "cold-paced";
        outFile = cmd.Length > 2 ? cmd[2] : Path.Combine(AppContext.BaseDirectory, "results.jsonl");

        window = new Window();
        host = new Grid();
        window.Content = host;
        window.AppWindow.Resize(new Windows.Graphics.SizeInt32(480, 520));
        window.Activate();

        _ = RunAsync();
    }

    private async Task RunAsync()
    {
        try
        {
            // Let the empty window finish its own startup rendering so scenario
            // numbers measure picker cost only.
            await Task.Delay(1000);

            var result = new Dictionary<string, object>
            {
                ["scenario"] = scenario,
                ["timestampUtc"] = DateTime.UtcNow.ToString("O"),
            };

            switch (scenario)
            {
                case "cold-all": await RunColdAll(result); break;
                case "cold-paced": await RunColdPaced(result); break;
                case "warm-repeat": await RunWarmRepeat(result); break;
                case "search": await RunSearch(result); break;
                case "scroll": await RunScroll(result); break;
                case "warm-opacity": await RunWarmTrick(result, WrapOpacity); break;
                case "warm-cover": await RunWarmTrick(result, WrapCovered); break;
                case "warm-clip": await RunWarmTrick(result, WrapClipped); break;
                case "warm-offscreen": await RunWarmTrick(result, WrapOffscreen); break;
                case "warm-rtb": await RunWarmTrickRtb(result); break;
                case "warmed-open": await RunWarmedOpen(result); break;
                default: result["error"] = "unknown scenario"; break;
            }

            File.AppendAllText(outFile, JsonSerializer.Serialize(result) + Environment.NewLine);
        }
        catch (Exception ex)
        {
            File.AppendAllText(outFile, JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["scenario"] = scenario,
                ["error"] = ex.ToString(),
            }) + Environment.NewLine);
        }

        Exit();
    }

    // ---------- scenarios ----------

    private async Task RunColdAll(Dictionary<string, object> result)
    {
        var recorder = new FrameRecorder();
        recorder.Start();
        AttachSurface(BuildManualSheet());
        await recorder.WaitForSettleAsync();
        recorder.Stop();
        recorder.Report(result, "attach");
    }

    private async Task RunColdPaced(Dictionary<string, object> result)
    {
        var recorder = new FrameRecorder();
        recorder.Start();
        AttachSurface(BuildPresenterSheet(out _));
        await recorder.WaitForSettleAsync();
        recorder.Stop();
        recorder.Report(result, "attach");
    }

    private async Task RunWarmRepeat(Dictionary<string, object> result)
    {
        var first = new FrameRecorder();
        first.Start();
        AttachSurface(BuildManualSheet());
        await first.WaitForSettleAsync();
        first.Stop();
        first.Report(result, "firstAttach");

        host.Children.Clear();
        await Task.Delay(500);

        // Brand-new TextBlocks for the same emoji: if this attach is cheap, glyph
        // rasterization is cached process-wide and warming strategies can work;
        // if it is as slow as the first, the cost is per-element visual overhead.
        var second = new FrameRecorder();
        second.Start();
        AttachSurface(BuildManualSheet());
        await second.WaitForSettleAsync();
        second.Stop();
        second.Report(result, "secondAttach");
    }

    private async Task RunSearch(Dictionary<string, object> result)
    {
        var warmup = new FrameRecorder();
        warmup.Start();
        AttachSurface(BuildPresenterSheet(out EmojiSheetPresenter presenter));
        await warmup.WaitForSettleAsync();
        warmup.Stop();
        warmup.Report(result, "attach");

        string[] queries = { "s", "sm", "smi", "smil", "smi", "sm", "s", "fa", "face", "f", "heart", "" };
        var uiCosts = new List<double>();
        var recorder = new FrameRecorder();
        recorder.Start();

        foreach (string query in queries)
        {
            var sw = Stopwatch.StartNew();
            if (query.Length == 0)
            {
                presenter.ShowBrowse(0);
            }
            else
            {
                presenter.ShowMatches(query);
            }
            uiCosts.Add(sw.Elapsed.TotalMilliseconds);
            await Task.Delay(150);
        }

        await recorder.WaitForSettleAsync();
        recorder.Stop();
        recorder.Report(result, "searchPhase");
        result["searchUiCostsMs"] = uiCosts;
    }

    private async Task RunScroll(Dictionary<string, object> result)
    {
        var warmup = new FrameRecorder();
        warmup.Start();
        FrameworkElement surface = BuildPresenterSheet(out _);
        AttachSurface(surface);
        await warmup.WaitForSettleAsync();
        warmup.Stop();
        warmup.Report(result, "attach");

        var scroller = (ScrollViewer)((Grid)surface).Children[0];
        var recorder = new FrameRecorder();
        recorder.Start();

        for (int i = 0; i < 3; i++)
        {
            scroller.ChangeView(null, scroller.ScrollableHeight, null, false);
            await Task.Delay(700);
            scroller.ChangeView(null, 0, null, false);
            await Task.Delay(700);
        }

        await recorder.WaitForSettleAsync();
        recorder.Stop();
        recorder.Report(result, "scrollPhase");
    }

    /// <summary>
    /// The real app flow: the invisible paced warmer runs to completion in the
    /// window, then the picker sheet is attached â€” expected to settle fast and
    /// clean because every glyph draws from the warmed cache.
    /// </summary>
    private async Task RunWarmedOpen(Dictionary<string, object> result)
    {
        var warmPhase = new FrameRecorder();
        warmPhase.Start();

        var warmer = new EmojiGlyphCacheWarmer();
        warmer.Start(host);

        var warmClock = Stopwatch.StartNew();
        while (!warmer.IsCompleted && warmClock.ElapsedMilliseconds < 30000)
        {
            await Task.Delay(100);
        }

        warmPhase.Stop();
        warmPhase.Report(result, "warmPhase");
        result["warmerCompleted"] = warmer.IsCompleted;
        result["warmWallMs"] = Math.Round(warmClock.Elapsed.TotalMilliseconds, 0);

        await Task.Delay(300);

        var open = new FrameRecorder();
        open.Start();
        AttachSurface(BuildPresenterSheet(out _));
        await open.WaitForSettleAsync();
        open.Stop();
        open.Report(result, "attach");
    }

    /// <summary>
    /// Tests whether a hidden-render technique warms the process glyph cache:
    /// attach the sheet wrapped in the trick for a few seconds, detach, then
    /// attach a fresh, fully visible sheet. If the second attach is as cheap
    /// as warm-repeat's, the trick rasterizes despite being invisible.
    /// </summary>
    private async Task RunWarmTrick(Dictionary<string, object> result, Func<FrameworkElement, FrameworkElement> wrap)
    {
        var trickPhase = new FrameRecorder();
        trickPhase.Start();
        AttachSurface(wrap(BuildManualSheet()));
        await Task.Delay(4500);
        trickPhase.Stop();
        trickPhase.Report(result, "trickPhase");

        host.Children.Clear();
        await Task.Delay(400);

        var second = new FrameRecorder();
        second.Start();
        AttachSurface(BuildManualSheet());
        await second.WaitForSettleAsync();
        second.Stop();
        second.Report(result, "secondAttach");
    }

    private async Task RunWarmTrickRtb(Dictionary<string, object> result)
    {
        FrameworkElement sheet = BuildManualSheet();
        FrameworkElement wrapped = WrapOpacity(sheet);
        AttachSurface(wrapped);
        await Task.Delay(600);

        var trickPhase = new FrameRecorder();
        trickPhase.Start();
        try
        {
            var bitmap = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();
            await bitmap.RenderAsync(sheet);
            result["rtbPixelSize"] = bitmap.PixelWidth + "x" + bitmap.PixelHeight;
        }
        catch (Exception ex)
        {
            result["rtbError"] = ex.Message;
        }
        trickPhase.Stop();
        trickPhase.Report(result, "trickPhase");

        host.Children.Clear();
        await Task.Delay(400);

        var second = new FrameRecorder();
        second.Start();
        AttachSurface(BuildManualSheet());
        await second.WaitForSettleAsync();
        second.Stop();
        second.Report(result, "secondAttach");
    }

    private static FrameworkElement WrapOpacity(FrameworkElement sheet)
    {
        return new Border { Child = sheet, Opacity = 0.01 };
    }

    private static FrameworkElement WrapCovered(FrameworkElement sheet)
    {
        var grid = new Grid();
        grid.Children.Add(sheet);
        grid.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 32, 32, 32)),
        });
        return grid;
    }

    private static FrameworkElement WrapClipped(FrameworkElement sheet)
    {
        return new Border
        {
            Child = sheet,
            Clip = new Microsoft.UI.Xaml.Media.RectangleGeometry
            {
                Rect = new Windows.Foundation.Rect(0, 0, 1, 1),
            },
        };
    }

    private static FrameworkElement WrapOffscreen(FrameworkElement sheet)
    {
        var offscreenHost = new Canvas { Width = 1, Height = 1 };
        Canvas.SetLeft(sheet, -20000);
        offscreenHost.Children.Add(sheet);
        return offscreenHost;
    }

    // ---------- surface builders ----------

    private void AttachSurface(FrameworkElement surface)
    {
        host.Children.Clear();
        host.Children.Add(surface);
    }

    /// <summary>The real production path: presenter with collapsed build + paced reveal.</summary>
    private FrameworkElement BuildPresenterSheet(out EmojiSheetPresenter presenter)
    {
        Canvas canvas = MakeCanvas();
        ScrollViewer scroller = MakeScroller(canvas);
        presenter = new EmojiSheetPresenter(canvas, MakeHighlight(canvas), MakeHighlight(canvas), MakeHeaderStyle());
        presenter.Build();
        presenter.ShowBrowse(0);
        return Wrap(scroller);
    }

    /// <summary>Replicates the pre-paced-reveal behavior: every glyph visible from the start.</summary>
    private FrameworkElement BuildManualSheet()
    {
        Canvas canvas = MakeCanvas();
        ScrollViewer scroller = MakeScroller(canvas);

        IReadOnlyList<EmojiGroup> groups = EmojiData.GetGroups();
        var counts = new int[groups.Count];
        for (int i = 0; i < groups.Count; i++)
        {
            counts[i] = groups[i].Entries.Count;
        }

        EmojiSheetLayout layout = EmojiSheetLayoutCalculator.ComputeBrowseLayout(counts);
        Style headerStyle = MakeHeaderStyle();

        for (int i = 0; i < groups.Count; i++)
        {
            var header = new TextBlock { Text = groups[i].Name, Style = headerStyle, IsHitTestVisible = false };
            Canvas.SetLeft(header, 0);
            Canvas.SetTop(header, layout.Sections[i].HeaderTop);
            canvas.Children.Add(header);
        }

        IReadOnlyList<EmojiEntry> entries = EmojiData.GetAllEntries();
        for (int i = 0; i < entries.Count; i++)
        {
            TextBlock glyph = EmojiGlyphFactory.CreateGlyph(entries[i].Emoji);
            EmojiGlyphFactory.PlaceGlyph(glyph, layout.Cells[i], glyph.DesiredSize);
            canvas.Children.Add(glyph);
        }

        canvas.Height = layout.TotalHeight;
        return Wrap(scroller);
    }

    private static Canvas MakeCanvas()
    {
        return new Canvas
        {
            Width = EmojiSheetLayoutCalculator.SheetWidth,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
        };
    }

    private static ScrollViewer MakeScroller(Canvas canvas)
    {
        return new ScrollViewer
        {
            Height = EmojiSheetLayoutCalculator.ScrollAreaHeight,
            Width = EmojiSheetLayoutCalculator.SheetWidth,
            VerticalScrollMode = ScrollMode.Enabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = canvas,
        };
    }

    private static Border MakeHighlight(Canvas canvas)
    {
        var highlight = new Border
        {
            Width = EmojiSheetLayoutCalculator.ItemSize,
            Height = EmojiSheetLayoutCalculator.ItemSize,
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
        };
        canvas.Children.Add(highlight);
        return highlight;
    }

    private static Style MakeHeaderStyle()
    {
        var style = new Style(typeof(TextBlock));
        style.Setters.Add(new Setter(TextBlock.FontSizeProperty, 11d));
        style.Setters.Add(new Setter(TextBlock.ForegroundProperty, new SolidColorBrush(Microsoft.UI.Colors.Gray)));
        return style;
    }

    private static FrameworkElement Wrap(ScrollViewer scroller)
    {
        var grid = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        grid.Children.Add(scroller);
        return grid;
    }
}

/// <summary>
/// Records CompositionTarget.Rendering tick deltas. Subscribing keeps the XAML
/// frame loop ticking, so "settled" is detected as a run of consecutive
/// vsync-quiet deltas rather than tick silence.
/// </summary>
public sealed class FrameRecorder
{
    private const double QuietThresholdMs = 22;
    private const int QuietFramesForSettle = 45;
    private const int SettleTimeoutMs = 30000;

    private readonly List<double> deltas = new List<double>();
    private readonly List<double> timeline = new List<double>();
    private readonly Stopwatch clock = new Stopwatch();

    private long lastTickTimestamp;
    private bool hasLastTick;
    private double firstTickMs = -1;
    private int consecutiveQuiet;
    private TaskCompletionSource<bool> settleSource;
    private bool isRunning;

    public void Start()
    {
        clock.Restart();
        hasLastTick = false;
        isRunning = true;
        CompositionTarget.Rendering += OnRendering;
    }

    public void Stop()
    {
        if (isRunning)
        {
            isRunning = false;
            CompositionTarget.Rendering -= OnRendering;
        }
    }

    public async Task WaitForSettleAsync()
    {
        consecutiveQuiet = 0;
        settleSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task settled = settleSource.Task;
        Task timeout = Task.Delay(SettleTimeoutMs);
        await Task.WhenAny(settled, timeout);
    }

    private void OnRendering(object sender, object e)
    {
        long timestamp = Stopwatch.GetTimestamp();
        double sinceStartMs = clock.Elapsed.TotalMilliseconds;

        if (firstTickMs < 0)
        {
            firstTickMs = sinceStartMs;
        }

        if (hasLastTick)
        {
            double deltaMs = (timestamp - lastTickTimestamp) * 1000.0 / Stopwatch.Frequency;
            deltas.Add(deltaMs);
            timeline.Add(sinceStartMs);

            if (deltaMs < QuietThresholdMs)
            {
                consecutiveQuiet++;
                if (consecutiveQuiet >= QuietFramesForSettle && settleSource != null)
                {
                    settleSource.TrySetResult(true);
                }
            }
            else
            {
                consecutiveQuiet = 0;
            }
        }

        lastTickTimestamp = timestamp;
        hasLastTick = true;
    }

    public void Report(Dictionary<string, object> result, string prefix)
    {
        var sorted = new List<double>(deltas);
        sorted.Sort();

        double max = 0, sum = 0;
        int over33 = 0, over100 = 0;
        foreach (double d in deltas)
        {
            if (d > max) { max = d; }
            sum += d;
            if (d > 33.4) { over33++; }
            if (d > 100) { over100++; }
        }

        double settleMs = 0;
        // Settle time = timestamp of the tick that started the final quiet run.
        int quietRun = 0;
        for (int i = deltas.Count - 1; i >= 0; i--)
        {
            if (deltas[i] < QuietThresholdMs) { quietRun++; } else { break; }
        }
        int settleIndex = deltas.Count - quietRun;
        settleMs = settleIndex > 0 && settleIndex <= timeline.Count ? timeline[settleIndex - 1] : firstTickMs;

        result[prefix + "FirstTickMs"] = Math.Round(firstTickMs, 1);
        result[prefix + "SettleMs"] = Math.Round(settleMs, 1);
        result[prefix + "TickCount"] = deltas.Count;
        result[prefix + "MaxDeltaMs"] = Math.Round(max, 1);
        result[prefix + "P95DeltaMs"] = sorted.Count > 0 ? Math.Round(sorted[(int)(sorted.Count * 0.95)], 1) : 0;
        result[prefix + "MeanDeltaMs"] = deltas.Count > 0 ? Math.Round(sum / deltas.Count, 2) : 0;
        result[prefix + "FramesOver33Ms"] = over33;
        result[prefix + "FramesOver100Ms"] = over100;

        var worst = new List<double>();
        foreach (double d in sorted.GetRange(Math.Max(0, sorted.Count - 12), Math.Min(12, sorted.Count)))
        {
            worst.Add(Math.Round(d, 1));
        }
        result[prefix + "WorstDeltasMs"] = worst;
    }
}
