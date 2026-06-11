using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using QuinSlate.Ui.Components;
using QuinSlate.Ui.Constants;
using Windows.UI;
using Windows.UI.Text;

namespace QuinSlate.Ui.Tray;

/// <summary>
/// A two-column panel that displays a compact preview of every buffer. Used
/// inside the WinUI 3 tray peek window so that color emoji render correctly.
/// </summary>
public sealed partial class TrayPeekPanel : UserControl
{
    private readonly TextBlock[] numbers;
    private readonly TextBlock[] emojis;
    private readonly TextBlock[] titles;
    private readonly TextBlock[] previews;

    /// <summary>
    /// Initialises the panel and caches the row TextBlock references.
    /// </summary>
    public TrayPeekPanel()
    {
        InitializeComponent();
        AppNameTextBlock.Text = AppConstants.AppName;

        // The dithered background is applied on load (so ActualTheme is resolved) and rebuilt
        // on theme change; until then the XAML gradient on RootBorder shows.
        Loaded += OnPanelLoaded;
        ActualThemeChanged += OnPanelActualThemeChanged;

        numbers = new TextBlock[] { Number0, Number1, Number2, Number3, Number4 };
        emojis = new TextBlock[] { Emoji0, Emoji1, Emoji2, Emoji3, Emoji4 };
        titles = new TextBlock[] { Title0, Title1, Title2, Title3, Title4 };
        previews = new TextBlock[] { Preview0, Preview1, Preview2, Preview3, Preview4 };
    }

    /// <summary>
    /// Updates every row from the supplied <paramref name="rows"/> array.
    /// Rows beyond the panel's row count are silently ignored.
    /// </summary>
    internal void SetRows(TrayPeekRow[] rows)
    {
        if (rows == null)
        {
            return;
        }

        int count = rows.Length < titles.Length ? rows.Length : titles.Length;
        for (int i = 0; i < count; i++)
        {
            TrayPeekRow row = rows[i];
            if (row == null)
            {
                numbers[i].Text = string.Empty;
                emojis[i].Text = string.Empty;
                titles[i].Text = string.Empty;
                previews[i].Text = string.Empty;
                continue;
            }

            numbers[i].Text = row.Number.ToString();
            emojis[i].Text = row.Emoji;
            titles[i].Text = row.Title;
            titles[i].FontStyle = FontStyle.Normal;
            titles[i].Foreground = GetThemeBrush("TextFillColorPrimaryBrush", Color.FromArgb(255, 0, 0, 0));

            if (row.IsEmpty)
            {
                previews[i].Text = "(empty)";
                previews[i].Foreground = GetThemeBrush("TextFillColorTertiaryBrush", Color.FromArgb(255, 136, 136, 136));
            }
            else
            {
                previews[i].Text = row.Preview;
                previews[i].Foreground = GetThemeBrush("TextFillColorPrimaryBrush", Color.FromArgb(255, 224, 224, 224));
            }
        }
    }

    /// <summary>
    /// Starts the GPU-accelerated entrance animation (slide up or down)
    /// for the panel content.
    /// </summary>
    /// <param name="slideUp">Whether to slide up (from bottom) or down (from top).</param>
    internal void PlayShowAnimation(bool slideUp)
    {
        double fromOffset = slideUp ? 12.0 : -12.0;
        GridAnimation.From = fromOffset;
        ShowStoryboard.Begin();
    }


    private void OnPanelLoaded(object sender, RoutedEventArgs e)
    {
        ApplyDitheredBackground();
    }

    private void OnPanelActualThemeChanged(FrameworkElement sender, object args)
    {
        ApplyDitheredBackground();
    }

    /// <summary>
    /// Replaces the peek window's XAML gradient with the dithered gradient for the current theme
    /// so it matches the main panel and does not band. Rendered at the panel's native pixel size
    /// (a stretched dithered bitmap re-bands). When a brush cannot be built the XAML gradient on
    /// RootBorder remains.
    /// </summary>
    private void ApplyDitheredBackground()
    {
        ImageBrush brush = DitheredGradientBrushFactory.CreateForElement(RootBorder);
        if (brush != null)
        {
            RootBorder.Background = brush;
        }
    }

    private static Brush GetThemeBrush(string resourceKey, Color fallbackColor)
    {
        if (Microsoft.UI.Xaml.Application.Current != null &&
            Microsoft.UI.Xaml.Application.Current.Resources.TryGetValue(resourceKey, out object obj) &&
            obj is Brush brush)
        {
            return brush;
        }
        return new SolidColorBrush(fallbackColor);
    }
}
