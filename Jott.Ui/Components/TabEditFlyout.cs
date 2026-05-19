using Jott.Ui.Models;
using Jott.Ui.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using Windows.System;

namespace Jott.Ui.Components;

/// <summary>
/// Builds, shows, and manages the lifecycle of the flyout that lets the user
/// edit a tab's emoji and title. Raises <see cref="Saved"/> when the user
/// commits a change and <see cref="Cancelled"/> when the flyout is dismissed
/// without saving.
/// </summary>
public sealed class TabEditFlyout
{
    private const double EmojiButtonWidth = 32;
    private const double TitleFieldWidth = 160;
    private const int MaxTitleLength = 16;
    private const string DefaultEmoji = "📋";

    private readonly EmojiPicker emojiPicker;
    private readonly SettingsService settingsService;

    private Flyout activeFlyout;
    private int editingTabIndex = -1;
    private string pendingEmoji;
    private Button emojiButton;
    private TextBox titleBox;
    private TextBlock charCounter;
    private StackPanel flyoutContent;

    /// <summary>
    /// Raised when the user confirms the edit. Arguments carry the affected
    /// buffer index, the chosen emoji, and the new title.
    /// </summary>
    public event EventHandler<TabEditSavedEventArgs> Saved;

    /// <summary>
    /// Raised when the flyout is closed without saving (ESC key or light-dismiss).
    /// </summary>
    public event EventHandler Cancelled;

    /// <summary>Gets whether the edit flyout is currently open.</summary>
    public bool IsOpen => activeFlyout != null;

    /// <summary>Raised whenever the flyout is dismissed (save, cancel, or light-dismiss).</summary>
    public event EventHandler FlyoutClosed;

    /// <summary>
    /// Initialises the component with the shared emoji picker and settings services.
    /// </summary>
    public TabEditFlyout(EmojiPicker emojiPicker, SettingsService settingsService)
    {
        if (emojiPicker == null)
        {
            throw new ArgumentNullException(nameof(emojiPicker));
        }

        if (settingsService == null)
        {
            throw new ArgumentNullException(nameof(settingsService));
        }

        this.emojiPicker = emojiPicker;
        this.settingsService = settingsService;
    }

    /// <summary>
    /// Opens the edit flyout anchored to <paramref name="anchor"/>, pre-filled
    /// with the values from <paramref name="tab"/>.
    /// </summary>
    /// <param name="bufferIndex">The 1-based index of the tab being edited.</param>
    /// <param name="tab">Current label state to pre-fill the flyout controls.</param>
    /// <param name="anchor">The UI element the flyout is attached to.</param>
    public void Open(int bufferIndex, TabDefinition tab, FrameworkElement anchor)
    {
        if (tab == null || anchor == null)
        {
            return;
        }

        CloseActive();

        editingTabIndex = bufferIndex;
        pendingEmoji = tab.Emoji;

        var emojiBtn = new Button
        {
            Content = tab.Emoji,
            Width = EmojiButtonWidth,
            VerticalAlignment = VerticalAlignment.Stretch,
            Padding = new Thickness(0, 0, 0, 2),
            FontSize = 20,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        emojiButton = emojiBtn;
        emojiPicker.EmojiSelected -= OnEmojiSelected;
        emojiPicker.EmojiSelected += OnEmojiSelected;
        emojiBtn.Click += OnEmojiButtonClick;

        var titleTextBox = new TextBox
        {
            Text = tab.Title,
            MaxLength = MaxTitleLength,
            Width = TitleFieldWidth,
            PlaceholderText = "Tab title",
            VerticalAlignment = VerticalAlignment.Center,
        };
        titleBox = titleTextBox;
        titleTextBox.KeyDown += OnTitleBoxKeyDown;
        titleTextBox.TextChanged += OnTitleBoxTextChanged;

        var counter = new TextBlock
        {
            Text = BuildCounterText(tab.Title.Length),
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Right,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        };
        charCounter = counter;

        var saveBtn = new Button
        {
            Content = new FontIcon
            {
                Glyph = "",
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                FontSize = 16,
            },
            Style = (Style)Application.Current.Resources["AccentButtonStyle"],
            VerticalAlignment = VerticalAlignment.Stretch,
            Padding = new Thickness(8, 0, 8, 0),
        };
        saveBtn.Click += OnSaveClick;

        var titleRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Stretch,
            Spacing = 8,
        };
        titleRow.Children.Add(emojiBtn);
        titleRow.Children.Add(titleTextBox);
        titleRow.Children.Add(saveBtn);

        var content = new StackPanel
        {
            Spacing = 8,
        };
        content.Children.Add(titleRow);
        content.Children.Add(counter);
        flyoutContent = content;
        content.GotFocus += OnFlyoutContentGotFocus;

        var presenterStyle = new Style(typeof(FlyoutPresenter));
        presenterStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8)));

        var flyout = new Flyout
        {
            Content = content,
            Placement = FlyoutPlacementMode.Bottom,
            FlyoutPresenterStyle = presenterStyle,
        };
        flyout.Closed += OnFlyoutClosed;
        activeFlyout = flyout;

        flyout.ShowAt(anchor, new FlyoutShowOptions { ShowMode = FlyoutShowMode.Standard });
    }

    private void OnEmojiButtonClick(object sender, RoutedEventArgs e)
    {
        var recentEmoji = settingsService.GetRecentEmoji();
        emojiPicker.Open(emojiButton, recentEmoji);
    }

    private void OnEmojiSelected(object sender, string emoji)
    {
        pendingEmoji = emoji;

        if (emojiButton != null)
        {
            emojiButton.Content = emoji;
        }
    }

    private void OnTitleBoxKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            CommitEdit();
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Escape)
        {
            CancelEdit();
            e.Handled = true;
        }
    }

    private void OnTitleBoxTextChanged(object sender, TextChangedEventArgs e)
    {
        if (charCounter != null && titleBox != null)
        {
            charCounter.Text = BuildCounterText(titleBox.Text.Length);
        }
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        CommitEdit();
    }

    private void OnFlyoutContentGotFocus(object sender, RoutedEventArgs e)
    {
        if (flyoutContent != null)
        {
            flyoutContent.GotFocus -= OnFlyoutContentGotFocus;
        }

        if (titleBox != null && !ReferenceEquals(e.OriginalSource, titleBox))
        {
            titleBox.Focus(FocusState.Programmatic);
        }
    }

    private void OnFlyoutClosed(object sender, object e)
    {
        emojiPicker.EmojiSelected -= OnEmojiSelected;

        if (flyoutContent != null)
        {
            flyoutContent.GotFocus -= OnFlyoutContentGotFocus;
        }

        activeFlyout = null;
        editingTabIndex = -1;
        pendingEmoji = null;
        emojiButton = null;
        titleBox = null;
        charCounter = null;
        flyoutContent = null;
        FlyoutClosed?.Invoke(this, EventArgs.Empty);
    }

    private void CommitEdit()
    {
        int index = editingTabIndex;
        if (index == -1 || titleBox == null)
        {
            return;
        }

        string newTitle = titleBox.Text.Trim();

        string newEmoji = pendingEmoji;
        if (string.IsNullOrEmpty(newEmoji))
        {
            newEmoji = DefaultEmoji;
        }

        settingsService.AddRecentEmoji(newEmoji);
        CloseActive();
        Saved?.Invoke(this, new TabEditSavedEventArgs(index, newEmoji, newTitle));
    }

    private void CancelEdit()
    {
        CloseActive();
        Cancelled?.Invoke(this, EventArgs.Empty);
    }

    private void CloseActive()
    {
        if (activeFlyout != null)
        {
            activeFlyout.Hide();
        }
    }

    private static string BuildCounterText(int length)
    {
        return $"{length} / {MaxTitleLength}";
    }
}
