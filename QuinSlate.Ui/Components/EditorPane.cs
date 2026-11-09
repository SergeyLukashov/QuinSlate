using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using QuinSlate.Ui.Services;
using System;
using System.Collections.Generic;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Buffer = QuinSlate.Ui.Models.Buffer;

namespace QuinSlate.Ui.Components;

/// <summary>
/// The buffer panel's editor area: the single <see cref="EditorHost"/> (a WebView2 hosting
/// CodeMirror 6, one CM6 state per buffer) and what the panel does with it — bringing it up behind
/// the startup cover, activating and focusing buffers, moving text between it and the buffer
/// service, its context menu, its background and colours, and the "this tab is full" notice.
/// </summary>
internal sealed class EditorPane
{
    // Context-menu command names sent to the editor page.
    private const string EditorCommandUndo = "undo";
    private const string EditorCommandRedo = "redo";
    private const string EditorCommandCut = "cut";
    private const string EditorCommandCopy = "copy";
    private const string EditorCommandSelectAll = "selectAll";

    private readonly WebView2 webView;
    private readonly Border startupCoverElement;
    private readonly Panel root;
    private readonly FrameworkElement themeSource;
    private readonly EditorContextMenu editorContextMenu;

    /// <summary>
    /// The throttled "this tab is full" notice, shown when the editor page clamps an edit at the
    /// character cap.
    /// </summary>
    private readonly LimitNotice limitNotice;

    private BufferService bufferService;
    private IReadOnlyList<Buffer> initialBuffers;
    private Func<int> activeBufferIndex;
    private Action<int, bool> bufferEmptinessChanged;

    private EditorHost editorHost;

    /// <summary>
    /// Kept for the pane's lifetime: it roots the system-colour subscription that re-sends the
    /// editor's colours on an accent change.
    /// </summary>
    private EditorThemeColors themeColors;

    private PanelBackgroundPainter backgroundPainter;
    private StartupCoverController startupCover;

    /// <summary>
    /// Buffer index last handed to <see cref="EditorHost.Activate"/>, so a selection change that
    /// resolves to the same buffer does not re-activate it (the page replays the tab-content
    /// entrance animation on every activation).
    /// </summary>
    private int lastActivatedBufferIndex = -1;

    private bool wantFocusOnReady;

    /// <summary>Raised for each panel shortcut the CodeMirror keymap forwards over the bridge.</summary>
    public event EventHandler<string> KeyCommandReceived;

    /// <summary>Raised once, when the editor page reports its first composited frame.</summary>
    public event EventHandler FirstPainted;

    /// <summary>Raised once, when the startup cover drops and the launch-critical rendering is done.</summary>
    public event EventHandler RenderSettled;

    /// <summary>Creates the pane over the panel's editor elements. <see cref="CreateHost"/> brings the editor up.</summary>
    /// <param name="webView">The WebView2 the editor page runs in.</param>
    /// <param name="startupCoverElement">The flat cover shown over the WebView2 at startup.</param>
    /// <param name="root">The panel's root, painted with the window mesh.</param>
    /// <param name="themeSource">The element whose <c>ActualTheme</c> the editor follows.</param>
    /// <param name="limitNoticeView">The surface of the "this tab is full" notice.</param>
    public EditorPane(WebView2 webView, Border startupCoverElement, Panel root, FrameworkElement themeSource, LimitNoticeView limitNoticeView)
    {
        if (webView == null)
        {
            throw new ArgumentNullException(nameof(webView));
        }

        if (root == null)
        {
            throw new ArgumentNullException(nameof(root));
        }

        if (themeSource == null)
        {
            throw new ArgumentNullException(nameof(themeSource));
        }

        this.webView = webView;
        this.startupCoverElement = startupCoverElement;
        this.root = root;
        this.themeSource = themeSource;

        editorContextMenu = new EditorContextMenu(
            onUndo: () => editorHost?.SendCommand(EditorCommandUndo),
            onRedo: () => editorHost?.SendCommand(EditorCommandRedo),
            onCut: () => editorHost?.SendCommand(EditorCommandCut),
            onCopy: () => editorHost?.SendCommand(EditorCommandCopy),
            onPaste: OnEditorPasteRequested,
            onSelectAll: () => editorHost?.SendCommand(EditorCommandSelectAll));

        limitNotice = new LimitNotice(limitNoticeView);
    }

    /// <summary>The editor bridge; null until <see cref="CreateHost"/> has run.</summary>
    public EditorHost Host => editorHost;

    /// <summary>
    /// Creates the editor host and subscribes to it. The page itself is not started until
    /// <see cref="Start"/>.
    /// </summary>
    /// <param name="bufferService">Where edits made in the page are written.</param>
    /// <param name="initialBuffers">The buffers handed to the page once it is ready.</param>
    /// <param name="activeBufferIndex">Reads the selected tab's buffer index.</param>
    /// <param name="bufferEmptinessChanged">Told, per synced edit, whether that buffer is now empty.</param>
    public void CreateHost(
        BufferService bufferService,
        IReadOnlyList<Buffer> initialBuffers,
        Func<int> activeBufferIndex,
        Action<int, bool> bufferEmptinessChanged)
    {
        this.bufferService = bufferService;
        this.initialBuffers = initialBuffers;
        this.activeBufferIndex = activeBufferIndex;
        this.bufferEmptinessChanged = bufferEmptinessChanged;

        editorHost = new EditorHost(webView, bufferService.AppDataDirectory);
        themeColors = new EditorThemeColors(themeSource, editorHost);
        backgroundPainter = new PanelBackgroundPainter(themeSource, root, webView, startupCoverElement, editorHost, themeColors);
        editorHost.Ready += OnEditorReady;
        editorHost.ContentSynced += OnEditorContentSynced;
        editorHost.KeyCommandReceived += (s, command) => KeyCommandReceived?.Invoke(this, command);
        editorHost.ContextMenuRequested += OnEditorContextMenuRequested;

        // Subscribes to the page's paint, entrance, and creation-failure notifications, in that order.
        startupCover = new StartupCoverController(startupCoverElement, editorHost);
        startupCover.FirstPainted += (s, e) => FirstPainted?.Invoke(this, EventArgs.Empty);
        startupCover.RenderSettled += (s, e) => RenderSettled?.Invoke(this, EventArgs.Empty);
        editorHost.LimitReached += OnEditorLimitReached;
    }

    /// <summary>Starts bringing the editor page up, on the flat mid-tone of the current theme.</summary>
    public void Start()
    {
        bool isDark = themeSource.ActualTheme == ElementTheme.Dark;
        _ = editorHost.InitializeAsync(DitheredGradientBrushFactory.MidColor(isDark));
    }

    /// <summary>Asks the editor page to run the startup reveal entrance.</summary>
    public void BeginEntrance()
    {
        if (editorHost != null)
        {
            editorHost.BeginEntrance();
        }
    }

    /// <summary>
    /// Focuses the active buffer's editor, or, before the page is ready, as soon as it reports
    /// <c>ready</c>.
    /// </summary>
    public void Focus()
    {
        wantFocusOnReady = true;
        if (editorHost == null)
        {
            return;
        }

        webView.Focus(FocusState.Programmatic);
        editorHost.FocusEditor();
    }

    /// <summary>Points the editor at buffer <paramref name="bufferIndex"/>, unless it already shows it.</summary>
    public void ActivateBuffer(int bufferIndex)
    {
        // The tab-content entrance animation is replayed inside the editor page when it activates
        // the buffer (a WebView2's own opacity cannot be animated from XAML without flashing black).
        if (editorHost != null && bufferIndex != lastActivatedBufferIndex)
        {
            lastActivatedBufferIndex = bufferIndex;
            editorHost.Activate(bufferIndex);

            // The notice is about the tab that was full, not the one now on screen.
            ResetLimitNotice();
        }
    }

    /// <summary>Drops the character-limit notice and re-arms it.</summary>
    public void ResetLimitNotice()
    {
        if (limitNotice != null)
        {
            limitNotice.Reset();
        }
    }

    /// <summary>Asks the editor page to push any pending content immediately.</summary>
    public void RequestFlush()
    {
        if (editorHost != null)
        {
            editorHost.RequestFlush();
        }
    }

    /// <summary>Empties buffer <paramref name="bufferIndex"/>, in the page and in the buffer service.</summary>
    public void ClearBuffer(int bufferIndex)
    {
        if (editorHost != null)
        {
            editorHost.SetText(bufferIndex, string.Empty);
        }

        if (bufferService != null)
        {
            bufferService.UpdateContent(bufferIndex, string.Empty);
        }
    }

    /// <summary>Paints every surface with the flat mid-tone (see <see cref="PanelBackgroundPainter"/>).</summary>
    public void ApplyFallbackBackground()
    {
        backgroundPainter.ApplyFallbackBackground();
    }

    /// <summary>Swaps the dithered meshes in, or keeps the flat mid-tone until they can be.</summary>
    public void ApplyDitheredBackground()
    {
        backgroundPainter.ApplyDitheredBackground();
    }

    /// <summary>Rebuilds the meshes once the window has stopped resizing.</summary>
    public void ScheduleBackgroundRebuild()
    {
        backgroundPainter.ScheduleRebuild();
    }

    /// <summary>Starts re-sending the editor's colours on every system colour change.</summary>
    public void TrackSystemColorChanges()
    {
        themeColors.TrackSystemColorChanges();
    }

    /// <summary>Re-applies the mesh and the editor's colours after the panel's theme changed.</summary>
    public void ApplyThemeChange()
    {
        backgroundPainter.ApplyDitheredBackground();

        // A manual light/dark override changes the element theme without an OS colour change, so
        // OnColorValuesChanged does not fire; re-push the editor's per-theme colours here so the
        // CodeMirror text, caret, and link colours track the panel's theme, not just the mesh.
        themeColors.Apply();
    }

    private void OnEditorReady(object sender, EventArgs e)
    {
        if (startupCover.HasRenderSettled)
        {
            // A repeat ready is a page reload after an editor-process failure. The reloaded
            // page starts with the caret-blink hold applied (it cannot know it is already
            // visible), and the startup cover flow that normally releases it will not run
            // again — release it here or the caret stays frozen solid.
            editorHost.ResetCaretBlink();
        }

        var payload = new List<KeyValuePair<int, string>>();
        if (initialBuffers != null)
        {
            foreach (Buffer buffer in initialBuffers)
            {
                payload.Add(new KeyValuePair<int, string>(buffer.Index, buffer.Content ?? string.Empty));
            }
        }

        editorHost.InitBuffers(payload);
        lastActivatedBufferIndex = activeBufferIndex();
        editorHost.Activate(lastActivatedBufferIndex);
        backgroundPainter.ApplyDitheredBackground();
        startupCover.StartFallback();

        if (wantFocusOnReady)
        {
            webView.Focus(FocusState.Programmatic);
            editorHost.FocusEditor();
        }
    }

    private async void OnEditorPasteRequested()
    {
        // Text-only, host-driven paste: reuses the STA WinRT clipboard path; the page clamps the
        // insertion to the cap and normalises line endings, matching the retired paste path.
        if (editorHost == null)
        {
            return;
        }

        DataPackageView dataView = Clipboard.GetContent();
        if (!dataView.Contains(StandardDataFormats.Text))
        {
            return;
        }

        string text = await dataView.GetTextAsync();
        editorHost.InsertText(text);
    }

    /// <summary>
    /// Hands a clamped edit to the notice. Every route into the document reports here — typing, IME,
    /// dictation, the Windows emoji panel, paste, drag-drop, and the host's own insert path
    /// (context-menu paste) — so a buffer sitting at the cap reports once per keystroke; the notice
    /// is what throttles that into one calm message.
    /// </summary>
    private void OnEditorLimitReached(object sender, EditorLimitEventArgs e)
    {
        LimitNoticeCause cause = e.Cause == EditorHost.CausePaste
            ? LimitNoticeCause.Paste
            : LimitNoticeCause.Typing;

        limitNotice.Report(e.Index, cause, e.DroppedCharacters);
    }

    private void OnEditorContentSynced(object sender, EditorContentEventArgs e)
    {
        if (bufferService == null)
        {
            return;
        }

        // The page sends "\n"-joined text; the buffer file uses CRLF. Convert and trim trailing
        // breaks exactly as the retired ExtractDirtyBuffers did.
        string text = e.Text ?? string.Empty;
        text = text.Replace("\n", "\r\n").TrimEnd('\r', '\n');

        bufferService.UpdateContent(e.Index, text);
        bufferEmptinessChanged(e.Index, string.IsNullOrEmpty(text));
    }

    private void OnEditorContextMenuRequested(object sender, EditorContextMenuEventArgs e)
    {
        if (editorContextMenu == null)
        {
            return;
        }

        editorContextMenu.ShowAt(webView, new Point(e.X, e.Y), e.CanUndo, e.CanRedo, e.HasSelection);
    }
}
