using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using System;
using System.Collections.Generic;

namespace Jott.Ui.Components;

/// <summary>
/// Hosts the <see cref="EmojiPickerView"/> inside a flyout and raises
/// <see cref="EmojiSelected"/> when the user picks an emoji.
/// </summary>
public sealed class EmojiPicker
{
    private Flyout cachedFlyout;
    private EmojiPickerView cachedView;

    /// <summary>Raised when the user selects an emoji. The argument is the selected emoji string.</summary>
    public event EventHandler<string> EmojiSelected;

    /// <summary>
    /// Opens the emoji picker anchored to <paramref name="anchor"/>.
    /// The recent list is refreshed from <paramref name="recentEmoji"/> on every open.
    /// </summary>
    public void Open(Button anchor, IReadOnlyList<string> recentEmoji)
    {
        if (anchor == null)
        {
            return;
        }

        EnsurePickerBuilt();

        cachedView.Reset(recentEmoji);

        FlyoutBase.SetAttachedFlyout(anchor, cachedFlyout);
        FlyoutBase.ShowAttachedFlyout(anchor);
    }

    private void EnsurePickerBuilt()
    {
        if (cachedFlyout != null)
        {
            return;
        }

        cachedView = new EmojiPickerView();
        cachedView.EmojiClicked += OnEmojiClicked;

        cachedFlyout = new Flyout
        {
            Content = cachedView,
            Placement = FlyoutPlacementMode.Bottom,
            AreOpenCloseAnimationsEnabled = false,
        };
    }

    private void OnEmojiClicked(object sender, string emoji)
    {
        if (cachedFlyout != null)
        {
            cachedFlyout.Hide();
        }

        EmojiSelected?.Invoke(this, emoji);
    }
}
