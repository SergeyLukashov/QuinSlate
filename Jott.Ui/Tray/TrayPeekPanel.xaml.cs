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
    private const string MutedPreviewColor = "#888888";
    private const string NormalPreviewColor = "#E0E0E0";

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
                ? new SolidColorBrush(ParseColor(MutedPreviewColor))
                : new SolidColorBrush(ParseColor(NormalPreviewColor));
        }
    }

    private static Color ParseColor(string hex)
    {
        hex = hex.TrimStart('#');
        byte r = System.Convert.ToByte(hex.Substring(0, 2), 16);
        byte g = System.Convert.ToByte(hex.Substring(2, 2), 16);
        byte b = System.Convert.ToByte(hex.Substring(4, 2), 16);
        return Color.FromArgb(255, r, g, b);
    }
}
