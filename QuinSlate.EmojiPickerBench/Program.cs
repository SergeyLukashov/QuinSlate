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
    private EmojiSpriteAtlas atlas;

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

            atlas = new EmojiSpriteAtlas();

            var result = new Dictionary<string, object>
            {
                ["scenario"] = scenario,
                ["timestampUtc"] = DateTime.UtcNow.ToString("O"),
            };

            switch (scenario)
            {
                case "cold-all": await RunColdAll(result); break;
                case "cold-paced": await RunColdOpen(result); break;
                case "warm-repeat": await RunWarmRepeat(result); break;
                case "search": await RunSearch(result); break;
                case "scroll": await RunScroll(result); break;
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

    /// <summary>
    /// Attaches a fully visible sprite sheet the moment atlas decoding is
    /// kicked off — the realistic worst case of an open racing the prewarm
    /// (sprite sources pop in when the decode lands).
    /// </summary>
    private async Task RunColdAll(Dictionary<string, object> result)
    {
        var recorder = new FrameRecorder();
        recorder.Start();
        StartAtlasLoad();
        AttachSurface(BuildManualSheet());
        await recorder.WaitForSettleAsync();
        recorder.Stop();
        recorder.Report(result, "attach");
        result["atlasLoaded"] = atlas.IsLoaded;
    }

    /// <summary>The real production open path (presenter sheet), same cold race.</summary>
    private async Task RunColdOpen(Dictionary<string, object> result)
    {
        var recorder = new FrameRecorder();
        recorder.Start();
        StartAtlasLoad();
        AttachSurface(BuildPresenterSheet(out _));
        await recorder.WaitForSettleAsync();
        recorder.Stop();
        recorder.Report(result, "attach");
        result["atlasLoaded"] = atlas.IsLoaded;
    }

    private async Task RunWarmRepeat(Dictionary<string, object> result)
    {
        await LoadAtlasAsync(result);

        var first = new FrameRecorder();
        first.Start();
        AttachSurface(BuildManualSheet());
        await first.WaitForSettleAsync();
        first.Stop();
        first.Report(result, "firstAttach");

        host.Children.Clear();
        await Task.Delay(500);

        // Brand-new Image elements over the same decoded sprites: measures the
        // pure per-element attach cost with no decode in the mix.
        var second = new FrameRecorder();
        second.Start();
        AttachSurface(BuildManualSheet());
        await second.WaitForSettleAsync();
        second.Stop();
        second.Report(result, "secondAttach");
    }

    private async Task RunSearch(Dictionary<string, object> result)
    {
        await LoadAtlasAsync(result);

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
                presenter.ShowBrowse();
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
        await LoadAtlasAsync(result);

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
    /// The real app flow: the atlas decode runs to completion first (the
    /// prewarm), then the picker sheet is attached — expected to settle fast
    /// and clean because every sprite draws a decoded bitmap.
    /// </summary>
    private async Task RunWarmedOpen(Dictionary<string, object> result)
    {
        await LoadAtlasAsync(result);
        await Task.Delay(300);

        var open = new FrameRecorder();
        open.Start();
        AttachSurface(BuildPresenterSheet(out _));
        await open.WaitForSettleAsync();
        open.Stop();
        open.Report(result, "attach");
    }

    // ---------- atlas ----------

    private void StartAtlasLoad()
    {
        atlas.EnsureLoaded(host.XamlRoot.RasterizationScale);
    }

    private async Task LoadAtlasAsync(Dictionary<string, object> result)
    {
        var loadClock = Stopwatch.StartNew();
        StartAtlasLoad();
        while (!atlas.IsLoaded && loadClock.ElapsedMilliseconds < 30000)
        {
            await Task.Delay(50);
        }

        result["atlasLoaded"] = atlas.IsLoaded;
        result["atlasLoadWallMs"] = Math.Round(loadClock.Elapsed.TotalMilliseconds, 0);
    }

    // ---------- surface builders ----------

    private void AttachSurface(FrameworkElement surface)
    {
        host.Children.Clear();
        host.Children.Add(surface);
    }

    /// <summary>The real production path: presenter over the shared sprite atlas.</summary>
    private FrameworkElement BuildPresenterSheet(out EmojiSheetPresenter presenter)
    {
        Canvas canvas = MakeCanvas();
        ScrollViewer scroller = MakeScroller(canvas);
        presenter = new EmojiSheetPresenter(canvas, MakeHighlight(canvas), MakeHighlight(canvas), MakeHeaderStyle(), atlas);
        presenter.Build();
        presenter.ShowBrowse();
        return Wrap(scroller);
    }

    /// <summary>A hand-rolled sheet of sprite Images, bypassing the presenter.</summary>
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
            Image sprite = EmojiSpriteFactory.CreateSprite();
            sprite.Source = atlas.GetSprite(i);
            EmojiSpriteFactory.PlaceSprite(sprite, layout.Cells[i]);
            canvas.Children.Add(sprite);
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
