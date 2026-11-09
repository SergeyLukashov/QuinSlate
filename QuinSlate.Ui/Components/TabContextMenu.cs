using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using QuinSlate.Ui.Interop;
using System;
using System.Collections.Generic;
using Buffer = QuinSlate.Ui.Models.Buffer;

namespace QuinSlate.Ui.Components;

/// <summary>
/// Each tab's right-click menu: <em>Rename tab</em>, and <em>Clear tab</em> with its two-step
/// confirm (the first click turns the item into <em>Confirm clear</em> and keeps the menu open; the
/// second, after a short cooldown, clears the buffer).
/// </summary>
internal sealed class TabContextMenu
{
    private const string RenameTabMenuText = "Rename tab";
    private const string RenameTabIconGlyph = "";
    private const string FluentIconFontFamily = "Segoe Fluent Icons";
    private const string ClearTabMenuText = "Clear tab";
    private const string ClearTabIconGlyph = "";
    private const string ConfirmClearMenuText = "Confirm clear";
    private const string ConfirmClearCheckmarkGlyph = "";
    private const string TranslateXProperty = "X";
    private const double MenuMinWidth = 150.0;
    private const double ClearItemTransformOrigin = 0.5;
    private const double ConfirmSlideFromX = -20.0;
    private const double ConfirmSlideToX = 0.0;
    private const int ConfirmSlideDurationMs = 250;
    private const int ClearConfirmCooldownMs = 500;

    private readonly TabView tabView;
    private readonly TabLabelEditor labelEditor;
    private readonly Action<int> clearBuffer;
    private readonly Dictionary<int, MenuFlyoutItem> clearMenuItemsByIndex = new Dictionary<int, MenuFlyoutItem>();

    private bool preventMenuClosing;
    private DateTime clearTransitionTime;

    /// <summary>Creates the menu factory.</summary>
    /// <param name="tabView">The tab strip the menus belong to.</param>
    /// <param name="labelEditor">Opens the rename flyout, and reports which tab it is editing.</param>
    /// <param name="clearBuffer">Empties a buffer, by 1-based index, once the clear is confirmed.</param>
    public TabContextMenu(TabView tabView, TabLabelEditor labelEditor, Action<int> clearBuffer)
    {
        if (tabView == null)
        {
            throw new ArgumentNullException(nameof(tabView));
        }

        if (labelEditor == null)
        {
            throw new ArgumentNullException(nameof(labelEditor));
        }

        if (clearBuffer == null)
        {
            throw new ArgumentNullException(nameof(clearBuffer));
        }

        this.tabView = tabView;
        this.labelEditor = labelEditor;
        this.clearBuffer = clearBuffer;
    }

    /// <summary>Forgets every menu built so far.</summary>
    public void Clear()
    {
        clearMenuItemsByIndex.Clear();
    }

    /// <summary>Builds <paramref name="buffer"/>'s menu and sets it as <paramref name="tabItem"/>'s context flyout.</summary>
    public void Attach(TabViewItem tabItem, Buffer buffer)
    {
        var menuFlyout = new MenuFlyout();
        var presenterStyle = new Style(typeof(MenuFlyoutPresenter));
        presenterStyle.Setters.Add(new Setter(Control.MinWidthProperty, MenuMinWidth));
        menuFlyout.MenuFlyoutPresenterStyle = presenterStyle;

        var renameItem = new MenuFlyoutItem
        {
            Text = RenameTabMenuText,
            Icon = new FontIcon
            {
                Glyph = RenameTabIconGlyph,
                FontFamily = new FontFamily(FluentIconFontFamily),
            }
        };
        renameItem.Click += (s, e) => labelEditor.Open(buffer.Index);
        menuFlyout.Items.Add(renameItem);

        var clearIcon = new FontIcon
        {
            Glyph = ClearTabIconGlyph,
            FontFamily = new FontFamily(FluentIconFontFamily),
        };

        var clearItem = new MenuFlyoutItem
        {
            Text = ClearTabMenuText,
            Icon = clearIcon,
            IsEnabled = !string.IsNullOrEmpty(buffer.Content),
            RenderTransformOrigin = new Windows.Foundation.Point(ClearItemTransformOrigin, ClearItemTransformOrigin),
        };

        var itemTranslate = new TranslateTransform();
        clearItem.RenderTransform = itemTranslate;
        clearItem.Click += (s, e) => OnClearItemClick(buffer.Index, clearItem);
        menuFlyout.Items.Add(clearItem);
        clearMenuItemsByIndex[buffer.Index] = clearItem;

        // Forces the arrow cursor the instant the menu's windowed popup appears so the
        // busy/app-starting cursor never shows. See microsoft-ui-xaml#8829.
        menuFlyout.Opened += (s, e) =>
        {
            ForceArrowCursor();
            bool isActive = ReferenceEquals(tabView.SelectedItem, tabItem);
            if (!isActive)
            {
                TabMenuOpenIndicator.SetVisibility(tabItem, true);
            }
        };

        menuFlyout.Closing += (s, e) =>
        {
            if (preventMenuClosing)
            {
                e.Cancel = true;
                preventMenuClosing = false;
            }
        };

        menuFlyout.Closed += (s, e) =>
        {
            ResetClearMenuItem(buffer.Index);
            if (labelEditor.IsEditing(buffer.Index) == false)
            {
                TabMenuOpenIndicator.SetVisibility(tabItem, false);
            }
        };

        tabItem.ContextFlyout = menuFlyout;
    }

    /// <summary>Enables <em>Clear tab</em> for a buffer with text in it, and disables it for an empty one.</summary>
    public void UpdateClearButtonState(int bufferIndex, bool isEmpty)
    {
        if (clearMenuItemsByIndex.TryGetValue(bufferIndex, out MenuFlyoutItem clearItem))
        {
            clearItem.IsEnabled = !isEmpty;
        }
    }

    /// <summary>
    /// Sets the system arrow cursor for the current frame to suppress the busy/app-starting
    /// cursor that WinUI shows over a freshly-opened flyout popup (microsoft-ui-xaml#8829).
    /// The handle returned for a system cursor is shared and OS-cached, so it is never freed.
    /// </summary>
    private static void ForceArrowCursor()
    {
        NativeMethods.SetCursor(NativeMethods.LoadCursor(IntPtr.Zero, NativeMethods.IDC_ARROW));
    }

    private void OnClearItemClick(int bufferIndex, MenuFlyoutItem clearItem)
    {
        if (clearItem.Text == ClearTabMenuText)
        {
            clearItem.Text = ConfirmClearMenuText;
            if (clearItem.Icon is FontIcon fontIcon)
            {
                fontIcon.Glyph = ConfirmClearCheckmarkGlyph;
            }

            if (clearItem.RenderTransform is TranslateTransform translate)
            {
                var duration = new Duration(TimeSpan.FromMilliseconds(ConfirmSlideDurationMs));
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

                var translateXAnim = new DoubleAnimation { From = ConfirmSlideFromX, To = ConfirmSlideToX, Duration = duration, EasingFunction = ease };

                Storyboard.SetTarget(translateXAnim, translate);
                Storyboard.SetTargetProperty(translateXAnim, TranslateXProperty);

                var sb = new Storyboard();
                sb.Children.Add(translateXAnim);
                sb.Begin();
            }

            clearTransitionTime = DateTime.UtcNow;
            preventMenuClosing = true;
        }
        else
        {
            if ((DateTime.UtcNow - clearTransitionTime).TotalMilliseconds < ClearConfirmCooldownMs)
            {
                preventMenuClosing = true;
                return;
            }

            OnClearConfirmed(bufferIndex);
        }
    }

    private void ResetClearMenuItem(int bufferIndex)
    {
        if (clearMenuItemsByIndex.TryGetValue(bufferIndex, out MenuFlyoutItem clearItem))
        {
            clearItem.Text = ClearTabMenuText;
            if (clearItem.Icon is FontIcon fontIcon)
            {
                fontIcon.Glyph = ClearTabIconGlyph;
            }
        }
    }

    private void OnClearConfirmed(int bufferIndex)
    {
        clearBuffer(bufferIndex);
        UpdateClearButtonState(bufferIndex, isEmpty: true);
        ResetClearMenuItem(bufferIndex);
    }
}
