using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using QuinSlate.Ui.Constants;
using Serilog;
using System;
using System.IO;
using System.Reflection;

namespace QuinSlate.Ui.Components;

/// <summary>
/// The About card: a fixed-size panel that displays application details, the live
/// data storage path, hotkey mappings, and helper actions. Hosted by
/// <see cref="AboutWindow"/> in its own top-level window so it always renders at its
/// natural size regardless of how small the main window has been resized.
/// </summary>
public sealed partial class AboutView : UserControl
{
    private const string ReportIssueMailtoUri = "mailto:contact@quinslate.com?subject=QuinSlate%20issue%20report";
    private const string AboutTitlePrefix = "About ";
    private const string VersionPrefix = "v";
    private const char SemVerMetadataSeparator = '+';
    private const int VersionFieldCount = 3;
    private const string DarkLogoAsset = "ms-appx:///Assets/Logo-dark.png";
    private const string LightLogoAsset = "ms-appx:///Assets/Logo-light.png";

    private const string MiddleEllipsis = "…";

    /// <summary>Point size of the monospaced path text (matches <c>PathText</c> in XAML).</summary>
    private const double PathFontSize = 13.0;

    /// <summary>
    /// Consolas advances every glyph by ~0.55 em, so the on-screen width of the path is simply
    /// <c>characters × PathFontSize × this ratio</c>. Being monospaced is what lets the
    /// middle-ellipsis fit be computed exactly instead of measured.
    /// </summary>
    private const double ConsolasAdvanceEmRatio = 0.55;

    /// <summary>
    /// Raised when the user activates the close affordance. The hosting
    /// <see cref="AboutWindow"/> closes the window in response.
    /// </summary>
    public event EventHandler CloseRequested;

    /// <summary>
    /// The absolute directory the running services read and write data in. Set by
    /// the caller before the view is shown so it displays the real, live storage
    /// location instead of recomputing it.
    /// </summary>
    public string StorageDirectory { get; set; }

    private string dataDirectory;
    private string dataPath;

    /// <summary>
    /// Constructs the view and wires basic content and theme listeners.
    /// </summary>
    public AboutView()
    {
        InitializeComponent();

        PopulateAppInfo();
        PopulateAppDataPath();

        Loaded += OnLoaded;
        ActualThemeChanged += OnActualThemeChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyDitheredBackground();
        UpdateThemedLogo();
    }

    private void PopulateAppInfo()
    {
        TitleBarText.Text = AboutTitlePrefix + AppConstants.AppName;
        VersionText.Text = ResolveDisplayVersion();
    }

    /// <summary>
    /// Selects the logo lockup that matches the current theme: the dark-theme
    /// asset carries a light wordmark for dark backgrounds and vice versa.
    /// </summary>
    private void UpdateThemedLogo()
    {
        var asset = ActualTheme == ElementTheme.Dark ? DarkLogoAsset : LightLogoAsset;
        LogoImage.Source = new BitmapImage(new Uri(asset));
    }

    /// <summary>
    /// Returns the running assembly's version for display. Prefers the
    /// informational (semantic) version when present, stripping any build
    /// metadata the SDK appends (e.g. <c>+&lt;commit&gt;</c>), and falls back to
    /// the three-part assembly version.
    /// </summary>
    private static string ResolveDisplayVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();

        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        if (informational != null && string.IsNullOrEmpty(informational.InformationalVersion) == false)
        {
            var value = informational.InformationalVersion;
            var separatorIndex = value.IndexOf(SemVerMetadataSeparator);
            if (separatorIndex >= 0)
            {
                value = value.Substring(0, separatorIndex);
            }

            return VersionPrefix + value;
        }

        var version = assembly.GetName().Version;
        return VersionPrefix + version.ToString(VersionFieldCount);
    }

    private void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        ApplyDitheredBackground();
        UpdateThemedLogo();
    }

    private void ApplyDitheredBackground()
    {
        ImageBrush brush = DitheredGradientBrushFactory.CreateForElement(RootGrid);
        if (brush != null)
        {
            RootGrid.Background = brush;
        }
    }

    private void PopulateAppDataPath()
    {
        dataDirectory = ResolveDataDirectory();

        dataPath = dataDirectory.EndsWith("\\") ? dataDirectory : dataDirectory + "\\";
        PathText.Text = dataPath;
        ToolTipService.SetToolTip(PathText, dataPath);
        RootGrid.SizeChanged += OnRootGridSizeChanged;
    }

    /// <summary>
    /// Re-fits the path whenever the card's width changes. <c>RootGrid</c> is the measuring stick
    /// because it is the window's content, arranged to exactly the client rect. The card's nominal
    /// width cannot stand in for it: <see cref="AboutWindow"/> sizes the window to 480 DIPs, but
    /// that is the *outer* rect, and the dialog frame is thick at high DPI — measured at 250%, the
    /// client is 467.2 DIPs, not 480. Those missing 12.8 DIPs are nearly two characters of path.
    /// The path's own text block cannot be measured instead: with <c>TextWrapping="NoWrap"</c> its
    /// <c>ActualWidth</c> is the width of its text (measured: 629 DIPs for the full path), not of
    /// the slot it was given, so asking it would always answer "it fits".
    /// </summary>
    private void OnRootGridSizeChanged(object sender, SizeChangedEventArgs e)
    {
        string fitted = MiddleEllipsize(dataPath, MeasurePathAvailableWidth(e.NewSize.Width));
        if (PathText.Text != fitted)
        {
            PathText.Text = fitted;
        }
    }

    /// <summary>
    /// Returns the width in DIPs left for the path once the body and data-card insets are taken
    /// out of <paramref name="cardWidth"/>. The insets are read from the live elements rather than
    /// restated as constants here, so re-styling the card in XAML cannot silently invalidate the
    /// fit.
    /// </summary>
    private double MeasurePathAvailableWidth(double cardWidth)
    {
        Thickness bodyPadding = BodyBorder.Padding;
        Thickness cardPadding = DataCardBorder.Padding;
        Thickness cardBorder = DataCardBorder.BorderThickness;

        return cardWidth
            - bodyPadding.Left - bodyPadding.Right
            - cardPadding.Left - cardPadding.Right
            - cardBorder.Left - cardBorder.Right;
    }

    /// <summary>
    /// Returns <paramref name="path"/> with its middle replaced by an ellipsis if it would not fit
    /// <paramref name="availableWidth"/> DIPs on one line, keeping the leading drive/root and the
    /// trailing folder visible. Exact (not measured) because the path font is monospaced.
    /// </summary>
    private static string MiddleEllipsize(string path, double availableWidth)
    {
        double advance = PathFontSize * ConsolasAdvanceEmRatio;
        int maxChars = (int)(availableWidth / advance);
        if (path.Length <= maxChars)
        {
            return path;
        }

        if (maxChars <= MiddleEllipsis.Length)
        {
            return MiddleEllipsis;
        }

        int keep = maxChars - MiddleEllipsis.Length;
        int head = (keep + 1) / 2;
        int tail = keep - head;
        return path.Substring(0, head) + MiddleEllipsis + path.Substring(path.Length - tail);
    }

    /// <summary>
    /// Returns the live storage directory supplied by the caller, falling back to
    /// the same resolution the app uses (<c>%AppData%\QuinSlate\</c>) when the
    /// view is shown without an explicit <see cref="StorageDirectory"/>.
    /// </summary>
    private string ResolveDataDirectory()
    {
        if (string.IsNullOrEmpty(StorageDirectory) == false)
        {
            return StorageDirectory;
        }

        return Services.AppDataPathResolver.Resolve();
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        EventHandler handler = CloseRequested;
        if (handler != null)
        {
            handler(this, EventArgs.Empty);
        }
    }

    private void OnOpenFolderClicked(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(dataDirectory))
        {
            return;
        }

        try
        {
            if (Directory.Exists(dataDirectory) == false)
            {
                Directory.CreateDirectory(dataDirectory);
            }

            System.Diagnostics.Process.Start("explorer.exe", dataDirectory);
        }
        catch (Exception ex)
        {
            Log.ForContext<AboutView>().Warning(ex, "Failed to open the data storage folder in Explorer.");
        }
    }

    private void OnCopyPathClicked(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(dataDirectory))
        {
            return;
        }

        try
        {
            var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
            dataPackage.SetText(dataDirectory);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
        }
        catch (Exception ex)
        {
            Log.ForContext<AboutView>().Warning(ex, "Failed to copy the data storage path to the clipboard.");
        }
    }

    private void OnReportIssueClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            _ = Windows.System.Launcher.LaunchUriAsync(new Uri(ReportIssueMailtoUri));
        }
        catch (Exception ex)
        {
            Log.ForContext<AboutView>().Warning(ex, "Failed to launch the report-issue mail link.");
        }
    }
}
