using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Animation;
using QuinSlate.Ui.Models;
using QuinSlate.Ui.Services;
using System;

namespace QuinSlate.Ui.Components;

/// <summary>
/// Builds, shows, and manages the lifecycle of the flyout that lets the user
/// edit a tab's emoji and title. The flyout and its <see cref="TabEditFlyoutView"/>
/// are built once and reused across opens. Raises <see cref="Saved"/> when the
/// user commits a change and <see cref="Cancelled"/> when the flyout is dismissed
/// without saving.
/// </summary>
public sealed class TabEditFlyout
{
    private const string DefaultEmoji = "📋";
    private const double PresenterPadding = 8;
    private const double PresenterCornerRadius = 8;
    private const double PresenterTopMargin = 4;
    private const double EntranceVerticalOffset = 8;

    private readonly EmojiPicker emojiPicker;
    private readonly SettingsService settingsService;

    private Flyout cachedFlyout;
    private TabEditFlyoutView cachedView;
    private ElementTheme builtTheme = ElementTheme.Default;
    private bool isOpen;
    private int editingTabIndex = -1;
    private string pendingEmoji;

    /// <summary>
    /// Raised when the user confirms the edit. Arguments carry the affected
    /// buffer index, the chosen emoji, and the new title.
    /// </summary>
    public event EventHandler<TabEditSavedEventArgs> Saved;

    /// <summary>
    /// Raised when the flyout is closed without saving (ESC key or Cancel button).
    /// </summary>
    public event EventHandler Cancelled;

    /// <summary>Raised whenever the flyout is dismissed (save, cancel, or light-dismiss).</summary>
    public event EventHandler FlyoutClosed;

    /// <summary>Gets whether the edit flyout is currently open.</summary>
    public bool IsOpen => isOpen;

    /// <summary>Gets the index of the tab currently being edited, or -1 if none.</summary>
    public int EditingTabIndex => editingTabIndex;

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

        // The shared picker outlives any single built view, so subscribe once here rather than in
        // EnsureBuilt (which rebuilds the view on a theme change and would otherwise re-subscribe).
        this.emojiPicker.EmojiSelected += OnEmojiSelected;
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

        EnsureBuilt(anchor.ActualTheme);

        editingTabIndex = bufferIndex;
        pendingEmoji = tab.Emoji;
        isOpen = true;

        cachedView.SetValues(tab.Emoji, tab.Title);

        FlyoutBase.SetAttachedFlyout(anchor, cachedFlyout);
        FlyoutBase.ShowAttachedFlyout(anchor);
    }

    private void EnsureBuilt(ElementTheme theme)
    {
        if (cachedFlyout != null && builtTheme == theme)
        {
            return;
        }

        // A cached view realized in the previous theme keeps that theme's brushes for the first
        // frame when reshown, which reads as a flash after a theme switch. Rebuild the view (and
        // its flyout) in the new theme instead, so its very first realization paints correctly.
        if (cachedFlyout != null)
        {
            cachedView.SaveRequested -= OnSaveRequested;
            cachedView.CancelRequested -= OnCancelRequested;
            cachedView.EmojiButtonClicked -= OnEmojiButtonClicked;
            cachedFlyout.Closed -= OnFlyoutClosed;
        }

        cachedView = new TabEditFlyoutView();
        cachedView.RequestedTheme = theme;
        cachedView.SaveRequested += OnSaveRequested;
        cachedView.CancelRequested += OnCancelRequested;
        cachedView.EmojiButtonClicked += OnEmojiButtonClicked;

        var presenterStyle = new Style(typeof(FlyoutPresenter));
        presenterStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(PresenterPadding)));
        presenterStyle.Setters.Add(new Setter(Control.CornerRadiusProperty, new CornerRadius(PresenterCornerRadius)));
        presenterStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, PresenterTopMargin, 0, 0)));

        var transitions = new TransitionCollection
        {
            new EntranceThemeTransition
            {
                FromVerticalOffset = EntranceVerticalOffset,
                FromHorizontalOffset = 0
            }
        };
        presenterStyle.Setters.Add(new Setter(UIElement.TransitionsProperty, transitions));

        cachedFlyout = new Flyout
        {
            Content = cachedView,
            Placement = FlyoutPlacementMode.Bottom,
            FlyoutPresenterStyle = presenterStyle,
            AreOpenCloseAnimationsEnabled = false,
            ShowMode = FlyoutShowMode.Standard,
        };
        cachedFlyout.Closed += OnFlyoutClosed;

        builtTheme = theme;
    }

    private void OnEmojiButtonClicked(object sender, EventArgs e)
    {
        emojiPicker.Open(cachedView.EmojiButton, settingsService.GetRecentEmoji());
    }

    private void OnEmojiSelected(object sender, string emoji)
    {
        pendingEmoji = emoji;

        if (cachedView != null)
        {
            cachedView.SetEmoji(emoji);
        }
    }

    private void OnSaveRequested(object sender, EventArgs e)
    {
        CommitEdit();
    }

    private void OnCancelRequested(object sender, EventArgs e)
    {
        CancelEdit();
    }

    private void OnFlyoutClosed(object sender, object e)
    {
        isOpen = false;
        editingTabIndex = -1;
        pendingEmoji = null;
        FlyoutClosed?.Invoke(this, EventArgs.Empty);
    }

    private void CommitEdit()
    {
        int index = editingTabIndex;
        if (index == -1 || cachedView == null)
        {
            return;
        }

        string newTitle = cachedView.GetTitle().Trim();

        string newEmoji = pendingEmoji;
        if (string.IsNullOrEmpty(newEmoji))
        {
            newEmoji = DefaultEmoji;
        }

        settingsService.AddRecentEmoji(newEmoji);
        Hide();
        Saved?.Invoke(this, new TabEditSavedEventArgs(index, newEmoji, newTitle));
    }

    private void CancelEdit()
    {
        Hide();
        Cancelled?.Invoke(this, EventArgs.Empty);
    }

    private void Hide()
    {
        if (cachedFlyout != null)
        {
            cachedFlyout.Hide();
        }
    }
}
