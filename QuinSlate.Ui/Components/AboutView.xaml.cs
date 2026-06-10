using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.IO;
using System.Reflection;

namespace QuinSlate.Ui.Components;

/// <summary>
/// The About card: a fixed-width panel that displays application details, the live
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

    /// <summary>
    /// Raised when the user activates the close affordance. The hosting
    /// <see cref="AboutWindow"/> closes the window in response.
    /// </summary>
    public event EventHandler CloseRequested;

    /// <summary>
    /// Raised whenever the card's layout size changes. The hosting <see cref="AboutWindow"/>
    /// reads <see cref="NaturalCardHeight"/> in response to size its window exactly to the
    /// content (the view itself stretches to fill the window, so its own <c>ActualHeight</c>
    /// cannot be used to discover the content height).
    /// </summary>
    public event EventHandler ContentSized;

    /// <summary>
    /// The card's fixed width in DIPs, as declared in XAML — the single source of truth the
    /// hosting <see cref="AboutWindow"/> sizes itself from.
    /// </summary>
    public double NaturalCardWidth
    {
        get { return RootGrid.Width; }
    }

    /// <summary>
    /// The card's natural height in DIPs, independent of the hosting window size. The card is
    /// re-measured with an unconstrained height because a plain <c>DesiredSize</c> read is
    /// clamped by the current window height — if the window starts shorter than the content
    /// (e.g. the estimated initial size), the clamped value would lock the window in too short
    /// and clip the footer.
    /// </summary>
    public double NaturalCardHeight
    {
        get
        {
            RootGrid.Measure(new Windows.Foundation.Size(NaturalCardWidth, double.PositiveInfinity));
            return RootGrid.DesiredSize.Height;
        }
    }

    /// <summary>
    /// The height in DIPs of the draggable header bar, used by <see cref="AboutWindow"/> to map
    /// out the caption (drag) region of the borderless window.
    /// </summary>
    public double HeaderHeight
    {
        get { return HeaderBar.ActualHeight; }
    }

    /// <summary>
    /// The absolute directory the running services read and write data in. Set by
    /// the caller before the view is shown so it displays the real, live storage
    /// location instead of recomputing it.
    /// </summary>
    public string StorageDirectory { get; set; }

    private string dataDirectory;

    /// <summary>
    /// Constructs the view and wires basic layout and theme listeners.
    /// </summary>
    public AboutView()
    {
        InitializeComponent();

        // Populate the text-bearing fields before the first layout pass so the card measures
        // at its final height immediately (the data path wraps to two lines, which the hosting
        // window must account for when sizing itself). Theme-dependent visuals stay in OnLoaded.
        PopulateAppInfo();
        PopulateAppDataPath();

        Loaded += OnLoaded;
        ActualThemeChanged += OnActualThemeChanged;
        RootGrid.SizeChanged += OnRootGridSizeChanged;
    }

    private void OnRootGridSizeChanged(object sender, SizeChangedEventArgs e)
    {
        EventHandler handler = ContentSized;
        if (handler != null)
        {
            handler(this, EventArgs.Empty);
        }
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
        PathText.Text = dataDirectory.EndsWith("\\") ? dataDirectory : dataDirectory + "\\";
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
