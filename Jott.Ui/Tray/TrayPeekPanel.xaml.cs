using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Jott.Ui.Tray;

/// <summary>
/// A two-column panel that displays a compact preview of every buffer. Used
/// inside the WinUI 3 tray peek window so that color emoji render correctly.
/// </summary>
public sealed partial class TrayPeekPanel : UserControl
{
    private readonly TextBlock[] labels;
    private readonly TextBlock[] previews;

    /// <summary>
    /// Initialises the panel and caches the row TextBlock references.
    /// </summary>
    public TrayPeekPanel()
    {
        InitializeComponent();

        labels = new TextBlock[] { Label0, Label1, Label2, Label3, Label4 };
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

        int count = rows.Length < labels.Length ? rows.Length : labels.Length;
        for (int i = 0; i < count; i++)
        {
            TrayPeekRow row = rows[i];
            if (row == null)
            {
                labels[i].Text = string.Empty;
                previews[i].Text = string.Empty;
                continue;
            }

            labels[i].Text = row.Label;
            previews[i].Text = row.Preview;
            previews[i].Foreground = row.IsEmpty
                ? GetThemeBrush("TextFillColorTertiaryBrush", Color.FromArgb(255, 136, 136, 136))
                : GetThemeBrush("TextFillColorPrimaryBrush", Color.FromArgb(255, 224, 224, 224));
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
