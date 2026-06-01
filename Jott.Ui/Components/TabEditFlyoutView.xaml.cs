using Jott.Ui.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using Windows.System;

namespace Jott.Ui.Components;

/// <summary>
/// The tab-edit surface: an emoji button and a title text box with a character
/// counter, plus Cancel and Save buttons. Raises <see cref="SaveRequested"/>,
/// <see cref="CancelRequested"/>, and <see cref="EmojiButtonClicked"/> so the
/// hosting <see cref="TabEditFlyout"/> can drive persistence and the emoji picker.
/// </summary>
public sealed partial class TabEditFlyoutView : UserControl
{
    private const int MaxTitleLength = 12;
    private const string CharCounterName = "CharCounter";

    private TextBlock charCounter;
    private bool focusForwarded;

    /// <summary>Raised when the user confirms the edit (Save button or Enter key).</summary>
    public event EventHandler SaveRequested;

    /// <summary>Raised when the user dismisses the edit (Cancel button or Escape key).</summary>
    public event EventHandler CancelRequested;

    /// <summary>Raised when the user clicks the emoji button to open the picker.</summary>
    public event EventHandler EmojiButtonClicked;

    /// <summary>Builds the tab-edit surface and wires the title box behaviours.</summary>
    public TabEditFlyoutView()
    {
        InitializeComponent();

        TitleBox.Loaded += OnTitleBoxLoaded;
        TitleBox.TextChanged += OnTitleBoxTextChanged;
        TitleBox.KeyDown += OnTitleBoxKeyDown;
        ContentRoot.GotFocus += OnContentRootGotFocus;
    }

    /// <summary>Gets the emoji button so the host can anchor the emoji picker to it.</summary>
    public Button EmojiButton => EmojiPickerButton;

    /// <summary>
    /// Pre-fills the emoji button and title box and refreshes the character
    /// counter. Resets the one-shot focus forwarding so the title box is
    /// selected when the surface next receives focus.
    /// </summary>
    public void SetValues(string emoji, string title)
    {
        focusForwarded = false;
        EmojiPickerButton.Content = emoji;
        TitleBox.Text = title == null ? string.Empty : title;
        UpdateCounter();
    }

    /// <summary>Updates the emoji button content after a pick.</summary>
    public void SetEmoji(string emoji)
    {
        EmojiPickerButton.Content = emoji;
    }

    /// <summary>Returns the current (untrimmed) title text.</summary>
    public string GetTitle()
    {
        return TitleBox.Text;
    }

    private void OnTitleBoxLoaded(object sender, RoutedEventArgs e)
    {
        charCounter = VisualTreeHelpers.FindVisualChild<TextBlock>(TitleBox, CharCounterName);
        UpdateCounter();
    }

    private void OnTitleBoxTextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateCounter();
    }

    private void OnTitleBoxKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            SaveRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Escape)
        {
            CancelRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        SaveRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        CancelRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnEmojiButtonClick(object sender, RoutedEventArgs e)
    {
        EmojiButtonClicked?.Invoke(this, EventArgs.Empty);
    }

    private void OnContentRootGotFocus(object sender, RoutedEventArgs e)
    {
        if (focusForwarded)
        {
            return;
        }

        if (!ReferenceEquals(e.OriginalSource, TitleBox))
        {
            focusForwarded = true;
            TitleBox.Focus(FocusState.Programmatic);
            TitleBox.SelectAll();
        }
    }

    private void UpdateCounter()
    {
        if (charCounter != null)
        {
            charCounter.Text = $"{TitleBox.Text.Length}/{MaxTitleLength}";
        }
    }
}
