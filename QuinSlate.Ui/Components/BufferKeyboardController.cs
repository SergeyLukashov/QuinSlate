using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using Windows.System;

namespace QuinSlate.Ui.Components;

/// <summary>
/// Centralises the buffer panel's keyboard shortcuts.
/// </summary>
/// <remarks>
/// Shortcuts arrive from two places and must behave identically:
/// <list type="bullet">
/// <item>When the web editor has focus, the CodeMirror keymap forwards the shortcut over the
/// bridge and the panel calls <see cref="CycleBuffer"/>, <see cref="SelectBuffer"/>, or raises
/// <see cref="EditFlyoutRequested"/> — keys typed inside the WebView2 never reach XAML
/// <c>PreviewKeyDown</c>.</item>
/// <item>When focus is on panel chrome, <see cref="HandlePanelPreviewKey"/> handles the same
/// shortcuts from the panel's <c>PreviewKeyDown</c>.</item>
/// </list>
/// Selecting a buffer sets <see cref="TabView.SelectedIndex"/>; the panel's
/// <c>SelectionChanged</c> handler then activates and focuses that buffer's editor.
/// </remarks>
public sealed class BufferKeyboardController
{
    private const int Digit1To5BufferCount = 5;

    private readonly TabView tabView;

    /// <summary>
    /// Raised when the user requests the tab-edit flyout (F2). The argument is
    /// the 1-based buffer index of the currently selected tab.
    /// </summary>
    public event EventHandler<int> EditFlyoutRequested;

    /// <summary>Constructs the controller over the panel's <see cref="TabView"/>.</summary>
    public BufferKeyboardController(TabView tabView)
    {
        if (tabView == null)
        {
            throw new ArgumentNullException(nameof(tabView));
        }

        this.tabView = tabView;
    }

    /// <summary>
    /// Handles a key press observed by the panel before any inner control sees it. Used when focus
    /// is on panel chrome rather than inside the web editor.
    /// </summary>
    /// <param name="e">The key event from the panel's <c>PreviewKeyDown</c>.</param>
    public void HandlePanelPreviewKey(KeyRoutedEventArgs e)
    {
        if (e == null)
        {
            return;
        }

        if (e.Key == VirtualKey.F2)
        {
            RequestEditFlyout();
            e.Handled = true;
            return;
        }

        bool ctrl = IsKeyDown(InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control));
        if (!ctrl)
        {
            return;
        }

        if (e.Key == VirtualKey.Tab)
        {
            bool back = IsKeyDown(InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift));
            CycleBuffer(back ? -1 : 1);
            e.Handled = true;
            return;
        }

        if (e.Key == VirtualKey.B || e.Key == VirtualKey.I || e.Key == VirtualKey.U)
        {
            e.Handled = true;
            return;
        }

        int bufferIndexFromKey = -1;
        if (e.Key >= VirtualKey.Number1 && e.Key <= VirtualKey.Number5)
        {
            bufferIndexFromKey = (int)e.Key - (int)VirtualKey.Number1;
        }
        else if (e.Key >= VirtualKey.NumberPad1 && e.Key <= VirtualKey.NumberPad5)
        {
            bufferIndexFromKey = (int)e.Key - (int)VirtualKey.NumberPad1;
        }

        if (bufferIndexFromKey >= 0 && bufferIndexFromKey < Digit1To5BufferCount)
        {
            SelectBuffer(bufferIndexFromKey);
            e.Handled = true;
        }
    }

    /// <summary>Raises <see cref="EditFlyoutRequested"/> for the selected tab, if any.</summary>
    public void RequestEditFlyout()
    {
        int selectedIndex = tabView.SelectedIndex;
        if (selectedIndex >= 0)
        {
            EditFlyoutRequested?.Invoke(this, selectedIndex + 1);
        }
    }

    /// <summary>Cycles the selected buffer by <paramref name="direction"/> (+1 forward, -1 back), wrapping.</summary>
    public void CycleBuffer(int direction)
    {
        int count = tabView.TabItems.Count;
        if (count == 0)
        {
            return;
        }

        int next = ((tabView.SelectedIndex + direction) % count + count) % count;
        SelectBuffer(next);
    }

    /// <summary>Selects the buffer at the given zero-based tab index.</summary>
    public void SelectBuffer(int zeroBasedIndex)
    {
        if (zeroBasedIndex < 0 || zeroBasedIndex >= tabView.TabItems.Count)
        {
            return;
        }

        tabView.SelectedIndex = zeroBasedIndex;
    }

    private static bool IsKeyDown(Windows.UI.Core.CoreVirtualKeyStates state)
    {
        return (state & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
    }
}
