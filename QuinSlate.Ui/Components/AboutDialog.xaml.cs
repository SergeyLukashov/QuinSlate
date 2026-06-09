using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.IO;
using System.Reflection;

namespace QuinSlate.Ui.Components;

/// <summary>
/// A redesigned, feature-rich modal that displays application details,
/// data storage paths, hotkey mappings, and helper actions.
/// </summary>
public sealed partial class AboutDialog : ContentDialog
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
    /// The absolute directory the running services read and write data in. Set by
    /// the caller before <see cref="ContentDialog.ShowAsync"/> so the dialog shows
    /// the real, live storage location instead of recomputing it.
    /// </summary>
    public string StorageDirectory { get; set; }

    private string dataDirectory;

    /// <summary>
    /// Constructs the dialog and wires basic layout and theme listeners.
    /// </summary>
    public AboutDialog()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        ActualThemeChanged += OnActualThemeChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyDitheredBackground();
        PopulateAppInfo();
        UpdateThemedLogo();
        PopulateAppDataPath();
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
    /// dialog is shown without an explicit <see cref="StorageDirectory"/>.
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
        Hide();
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
