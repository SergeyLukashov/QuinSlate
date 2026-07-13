using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using QuinSlate.Ui.Services;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.UI;

namespace QuinSlate.Ui.Components;

/// <summary>
/// Hosts the CodeMirror 6 editor page inside a single <see cref="WebView2"/> and owns the
/// host&#8596;page JSON bridge. The five buffers are five CM6 <c>EditorState</c>s in one page;
/// this class exposes just the operations the panel needs (populate, activate, set text, focus,
/// background, theme, context-menu round-trip, content sync, and inline-calc evaluation).
/// </summary>
/// <remarks>
/// Messages carrying buffer text (<c>init</c>, <c>setText</c>, <c>insert</c>, <c>contentSync</c>,
/// <c>calcRequest</c>/<c>calcResult</c>) are never logged — not the payload, not a prefix, not on
/// either side. Only message names, indices, and lengths may be logged.
/// </remarks>
internal sealed class EditorHost
{
    private const string VirtualHostName = "quinslate.editor";
    private const string EditorPageUrl = "https://quinslate.editor/editor.html";
    private const string EditorAssetsRelativePath = "WebEditor";
    private const string WebView2UserDataFolderName = "WebView2";

    private static Task<CoreWebView2Environment> sharedEnvironmentTask;

    private readonly WebView2 webView;
    private readonly string appDataDirectory;

    private bool isReady;
    private bool isInitializing;

    /// <summary>Raised on the UI thread when the page and CodeMirror have initialised.</summary>
    public event EventHandler Ready;

    /// <summary>Raised when the page pushes a buffer's debounced content (or an immediate flush).</summary>
    public event EventHandler<EditorContentEventArgs> ContentSynced;

    /// <summary>Raised when a panel-level keyboard shortcut is typed inside the editor.</summary>
    public event EventHandler<string> KeyCommandReceived;

    /// <summary>Raised when the editor is right-clicked, so the host can show the shared menu.</summary>
    public event EventHandler<EditorContextMenuEventArgs> ContextMenuRequested;

    /// <summary>Raised when the WebView2 could not be created (e.g. the Runtime is missing).</summary>
    public event EventHandler CreationFailed;

    /// <summary>
    /// Raised after the page has composited a frame with the gradient and text, so the host can
    /// start the reveal sequence (uncloak, then <see cref="BeginEntrance"/>).
    /// </summary>
    public event EventHandler Painted;

    /// <summary>
    /// Raised when the page has started the startup reveal entrance (see
    /// <see cref="BeginEntrance"/>) on a frame presented while visible, so the host can drop the
    /// startup cover onto the entrance's opacity-0 first frames.
    /// </summary>
    public event EventHandler EntranceStarted;

    /// <summary>Creates the host over <paramref name="webView"/>, storing data under <paramref name="appDataDirectory"/>.</summary>
    public EditorHost(WebView2 webView, string appDataDirectory)
    {
        if (webView == null)
        {
            throw new ArgumentNullException(nameof(webView));
        }

        if (appDataDirectory == null)
        {
            throw new ArgumentNullException(nameof(appDataDirectory));
        }

        this.webView = webView;
        this.appDataDirectory = appDataDirectory;
    }

    /// <summary>Whether the page has reported it is ready to receive content operations.</summary>
    public bool IsReady => isReady;

    /// <summary>
    /// Starts creating the shared <see cref="CoreWebView2Environment"/> (the browser-process
    /// spawn — the longest single step of editor bring-up) without waiting for the window or the
    /// <see cref="WebView2"/> control to exist. Called as the very first startup work so the
    /// environment is created in parallel with settings/buffer loads and window construction;
    /// <see cref="InitializeAsync"/> awaits the same task instead of starting from cold. A faulted
    /// prewarm surfaces when awaited there and follows the normal <see cref="CreationFailed"/> path.
    /// </summary>
    public static void PrewarmEnvironment(string appDataDirectory)
    {
        if (sharedEnvironmentTask != null || appDataDirectory == null)
        {
            return;
        }

        sharedEnvironmentTask = CreateEnvironmentAsync(appDataDirectory);
    }

    private static Task<CoreWebView2Environment> CreateEnvironmentAsync(string appDataDirectory)
    {
        // The user data folder must live inside the app-data directory (required for MSIX).
        string userDataFolder = Path.Combine(appDataDirectory, WebView2UserDataFolderName);
        return CoreWebView2Environment.CreateWithOptionsAsync(null, userDataFolder, new CoreWebView2EnvironmentOptions()).AsTask();
    }

    /// <summary>
    /// Creates the WebView2 environment (user data folder inside the app-data directory, required
    /// for MSIX), applies the hardened settings, maps the editor assets over a virtual host, sets
    /// the flat mid-tone background so the first frame does not flash white, and navigates to the
    /// editor page. Never throws: a creation failure raises <see cref="CreationFailed"/> and logs.
    /// </summary>
    public async Task InitializeAsync(Color defaultBackground)
    {
        if (isInitializing)
        {
            return;
        }

        isInitializing = true;

        try
        {
            // Set before the core is created so the very first composited frame is the mid-tone,
            // never Chromium's white default.
            webView.DefaultBackgroundColor = defaultBackground;

            if (sharedEnvironmentTask == null)
            {
                sharedEnvironmentTask = CreateEnvironmentAsync(appDataDirectory);
            }

            CoreWebView2Environment environment = await sharedEnvironmentTask;
            await webView.EnsureCoreWebView2Async(environment);

            CoreWebView2 core = webView.CoreWebView2;
            ConfigureSettings(core.Settings);

            core.SetVirtualHostNameToFolderMapping(
                VirtualHostName,
                Path.Combine(AppContext.BaseDirectory, EditorAssetsRelativePath),
                CoreWebView2HostResourceAccessKind.Allow);

            core.WebMessageReceived += OnWebMessageReceived;
            core.NavigationStarting += OnNavigationStarting;
            core.NewWindowRequested += OnNewWindowRequested;
            core.ProcessFailed += OnProcessFailed;

            webView.Source = new Uri(EditorPageUrl);
        }
        catch (Exception ex)
        {
            Log.ForContext<EditorHost>().Error(ex, "Failed to create the WebView2 editor host.");
            CreationFailed?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            isInitializing = false;
        }
    }

    private static void ConfigureSettings(CoreWebView2Settings settings)
    {
        settings.AreDefaultContextMenusEnabled = false;
        settings.AreBrowserAcceleratorKeysEnabled = false;
        settings.IsZoomControlEnabled = false;
        settings.IsStatusBarEnabled = false;
        settings.IsSwipeNavigationEnabled = false;
        settings.IsPasswordAutosaveEnabled = false;
        settings.IsGeneralAutofillEnabled = false;
        settings.IsPinchZoomEnabled = false;
#if DEBUG
        settings.AreDevToolsEnabled = true;
#else
        settings.AreDevToolsEnabled = false;
#endif
    }

    private void OnNavigationStarting(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs args)
    {
        // The page can never navigate away from its own virtual host.
        if (args.Uri == null || args.Uri.StartsWith("https://" + VirtualHostName + "/", StringComparison.OrdinalIgnoreCase) == false)
        {
            args.Cancel = true;
        }
    }

    private void OnNewWindowRequested(CoreWebView2 sender, CoreWebView2NewWindowRequestedEventArgs args)
    {
        args.Handled = true;
    }

    private void OnProcessFailed(CoreWebView2 sender, CoreWebView2ProcessFailedEventArgs args)
    {
        Log.ForContext<EditorHost>().Warning(
            "WebView2 process failed ({Kind}); reloading the editor page.", args.ProcessFailedKind);

        // Host state is authoritative: on reload the page reports ready again and the panel
        // repopulates all five buffers from BufferService, so at most in-session undo is lost.
        isReady = false;
        try
        {
            webView.CoreWebView2?.Reload();
        }
        catch (Exception ex)
        {
            Log.ForContext<EditorHost>().Error(ex, "Failed to reload the editor page after a process failure.");
        }
    }

    private void OnWebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        string json;
        try
        {
            json = args.WebMessageAsJson;
        }
        catch (Exception ex)
        {
            Log.ForContext<EditorHost>().Warning(ex, "Failed to read a web message from the editor page.");
            return;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            string type = GetString(root, "type");
            DispatchMessage(type, root);
        }
        catch (JsonException ex)
        {
            Log.ForContext<EditorHost>().Warning(ex, "Malformed web message from the editor page.");
        }
    }

    private void DispatchMessage(string type, JsonElement root)
    {
        switch (type)
        {
            case "ready":
                isReady = true;
                Ready?.Invoke(this, EventArgs.Empty);
                break;
            case "painted":
                Painted?.Invoke(this, EventArgs.Empty);
                break;
            case "entranceStarted":
                EntranceStarted?.Invoke(this, EventArgs.Empty);
                break;
            case "contentSync":
                ContentSynced?.Invoke(this, new EditorContentEventArgs(GetInt(root, "index"), GetString(root, "text")));
                break;
            case "calcRequest":
                HandleCalcRequest(GetInt(root, "index"), GetString(root, "lineContent"));
                break;
            case "key":
                KeyCommandReceived?.Invoke(this, GetString(root, "command"));
                break;
            case "contextMenu":
                ContextMenuRequested?.Invoke(this, new EditorContextMenuEventArgs(
                    GetDouble(root, "x"),
                    GetDouble(root, "y"),
                    GetBool(root, "canUndo"),
                    GetBool(root, "canRedo"),
                    GetBool(root, "hasSelection")));
                break;
            default:
                break;
        }
    }

    private void HandleCalcRequest(int index, string lineContent)
    {
        // CalcService is the unchanged inline-calc brain; the page owns only the trigger and the
        // in-place rewrite. The line content is never logged.
        bool ok = CalcService.TryEvaluate(lineContent, out string result);
        PostJson(writer =>
        {
            writer.WriteString("type", "calcResult");
            writer.WriteNumber("index", index);
            writer.WriteBoolean("ok", ok);
            writer.WriteString("result", ok ? result : string.Empty);
        });
    }

    /// <summary>Populates all five buffer states once, at startup.</summary>
    public void InitBuffers(IReadOnlyList<KeyValuePair<int, string>> buffers)
    {
        PostJson(writer =>
        {
            writer.WriteString("type", "init");
            writer.WriteStartArray("buffers");
            foreach (KeyValuePair<int, string> buffer in buffers)
            {
                writer.WriteStartObject();
                writer.WriteNumber("index", buffer.Key);
                writer.WriteString("text", buffer.Value ?? string.Empty);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        });
    }

    /// <summary>Activates the buffer state for <paramref name="index"/> (tab switch).</summary>
    public void Activate(int index)
    {
        PostJson(writer =>
        {
            writer.WriteString("type", "activate");
            writer.WriteNumber("index", index);
        });
    }

    /// <summary>Replaces the text of buffer <paramref name="index"/> (clear / external rewrite).</summary>
    public void SetText(int index, string text)
    {
        PostJson(writer =>
        {
            writer.WriteString("type", "setText");
            writer.WriteNumber("index", index);
            writer.WriteString("text", text ?? string.Empty);
        });
    }

    /// <summary>Moves DOM focus to the active editor.</summary>
    public void FocusEditor()
    {
        PostJson(writer => writer.WriteString("type", "focus"));
    }

    /// <summary>
    /// Releases the page's caret-blink hold (or restarts the blink cycle when the hold is
    /// already released) so the caret shows a full solid phase from the moment it first becomes
    /// visible. Used by the non-entrance paths: the startup-cover fallback and a page reload
    /// after an editor-process failure. The normal startup path releases the hold inside
    /// <see cref="BeginEntrance"/>.
    /// </summary>
    public void ResetCaretBlink()
    {
        PostJson(writer => writer.WriteString("type", "resetBlink"));
    }

    /// <summary>
    /// Asks the page to run the startup reveal entrance: after a frame that was demonstrably
    /// presented while visible (the window has just been uncloaked with the flat startup cover
    /// still up), the editor replays its fade-and-slide entrance from opacity zero, releases the
    /// caret-blink hold, and reports <see cref="EntranceStarted"/> so the host can drop the
    /// cover onto the animation's flat first frames. Sequencing the reveal through the page —
    /// rather than trusting the uncloak instant — is what makes it immune to Chromium's
    /// present-while-cloaked throttling, which otherwise pops the text in a few frames late.
    /// </summary>
    public void BeginEntrance()
    {
        PostJson(writer => writer.WriteString("type", "entrance"));
    }

    /// <summary>Asks the page to push any pending content immediately (blur / hide / shutdown).</summary>
    public void RequestFlush()
    {
        PostJson(writer => writer.WriteString("type", "flush"));
    }

    /// <summary>Inserts text at the selection (the shared, cap-clamped paste path).</summary>
    public void InsertText(string text)
    {
        PostJson(writer =>
        {
            writer.WriteString("type", "insert");
            writer.WriteString("text", text ?? string.Empty);
        });
    }

    /// <summary>Sends a context-menu command (undo / redo / cut / copy / selectAll) to the page.</summary>
    public void SendCommand(string name)
    {
        PostJson(writer =>
        {
            writer.WriteString("type", "command");
            writer.WriteString("name", name);
        });
    }

    /// <summary>Applies editor colours (text, caret, selection, calc accent) for the current theme.</summary>
    public void SetTheme(Color text, Color caret, Color selection, Color accent)
    {
        PostJson(writer =>
        {
            writer.WriteString("type", "theme");

            // Text and caret carry alpha (WinUI's light-theme TextFillColorPrimary is #E4000000), so
            // they must serialise as rgba() — rgb() would silently promote them to fully opaque.
            writer.WriteString("text", ToCssRgba(text));
            writer.WriteString("caret", ToCssRgba(caret));
            writer.WriteString("selection", ToCssRgba(selection));
            writer.WriteString("accent", ToCssRgb(accent));
        });
    }

    /// <summary>Swaps in the dithered gradient background PNG, shown 1:1 at the given CSS size.</summary>
    public void SetBackground(DitheredBackground background)
    {
        if (background == null)
        {
            return;
        }

        PostJson(writer =>
        {
            writer.WriteString("type", "background");
            writer.WriteString("pngBase64", background.PngBase64);
            writer.WriteNumber("cssWidth", background.CssWidth);
            writer.WriteNumber("cssHeight", background.CssHeight);
        });
    }

    private void PostJson(Action<Utf8JsonWriter> writeBody)
    {
        if (webView.CoreWebView2 == null || isReady == false)
        {
            return;
        }

        string json = BuildJson(writeBody);
        try
        {
            webView.CoreWebView2.PostWebMessageAsJson(json);
        }
        catch (Exception ex)
        {
            Log.ForContext<EditorHost>().Warning(ex, "Failed to post a web message to the editor page.");
        }
    }

    private static string BuildJson(Action<Utf8JsonWriter> writeBody)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writeBody(writer);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string ToCssRgb(Color color)
    {
        return $"rgb({color.R},{color.G},{color.B})";
    }

    private static string ToCssRgba(Color color)
    {
        double alpha = color.A / 255.0;
        return string.Format(System.Globalization.CultureInfo.InvariantCulture, "rgba({0},{1},{2},{3:0.###})", color.R, color.G, color.B, alpha);
    }

    private static string GetString(JsonElement root, string name)
    {
        if (root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String)
        {
            return value.GetString();
        }

        return string.Empty;
    }

    private static int GetInt(JsonElement root, string name)
    {
        if (root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number)
        {
            return value.GetInt32();
        }

        return 0;
    }

    private static double GetDouble(JsonElement root, string name)
    {
        if (root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number)
        {
            return value.GetDouble();
        }

        return 0;
    }

    private static bool GetBool(JsonElement root, string name)
    {
        return root.TryGetProperty(name, out JsonElement value) && (value.ValueKind == JsonValueKind.True);
    }
}
