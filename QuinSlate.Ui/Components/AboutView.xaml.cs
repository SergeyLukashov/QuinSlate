using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
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
    private const string DefaultRepoUrl = "https://github.com/lukas/QuinSlate";
    private const string MitLicenseUrl = "https://opensource.org/licenses/MIT";
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
    /// Width in DIPs available to the path text inside the data card, derived from the fixed
    /// card layout: 480 window − 40 body padding − 32 card padding − 2 card border = 406.
    /// </summary>
    private const double PathAvailableWidth = 406.0;

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

        string fullPath = dataDirectory.EndsWith("\\") ? dataDirectory : dataDirectory + "\\";
        PathText.Text = MiddleEllipsize(fullPath);
        ToolTipService.SetToolTip(PathText, fullPath);
    }

    /// <summary>
    /// Returns <paramref name="path"/> with its middle replaced by an ellipsis if it would not
    /// fit the data card on one line, keeping the leading drive/root and the trailing folder
    /// visible. Exact (not measured) because the path font is monospaced.
    /// </summary>
    private static string MiddleEllipsize(string path)
    {
        double advance = PathFontSize * ConsolasAdvanceEmRatio;
        int maxChars = (int)(PathAvailableWidth / advance);
        if (path.Length <= maxChars)
        {
            return path;
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
        catch (Exception)
        {
            // Fail silently to prevent crash
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
        catch (Exception)
        {
            // Fail silently to prevent crash
        }
    }

    private void OnReportIssueClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            _ = Windows.System.Launcher.LaunchUriAsync(new Uri(DefaultRepoUrl));
        }
        catch (Exception)
        {
            // Fail silently to prevent crash
        }
    }

    private void OnMitLicenseClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            _ = Windows.System.Launcher.LaunchUriAsync(new Uri(MitLicenseUrl));
        }
        catch (Exception)
        {
            // Fail silently to prevent crash
        }
    }
}
