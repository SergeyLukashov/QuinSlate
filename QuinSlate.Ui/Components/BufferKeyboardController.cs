using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.Generic;
using Windows.System;

namespace QuinSlate.Ui.Components;

/// <summary>
/// Centralises the keyboard shortcut handling for the buffer panel.
/// </summary>
/// <remarks>
/// The controller observes the panel-level <c>PreviewKeyDown</c> and the
/// per-editor <c>KeyDown</c> events. It handles:
/// <list type="bullet">
/// <item>Escape to dismiss an in-flight clear-confirm prompt.</item>
/// <item>F2 to request the tab-edit flyout for the selected tab (raised via <see cref="EditFlyoutRequested"/>).</item>
/// <item>Ctrl+Tab / Ctrl+Shift+Tab and Tab / Shift+Tab inside the editor to cycle buffers.</item>
/// <item>Ctrl+1..5 (top row and numpad) to jump to a specific buffer.</item>
/// <item>Suppresses Ctrl+B/I/U so the rich-edit box does not toggle formatting.</item>
/// <item>Forwards equals key state to the <see cref="CalcResultAnimator"/>.</item>
/// </list>
/// </remarks>
public sealed class BufferKeyboardController
{
    private const int Digit1To5BufferCount = 5;

    private readonly TabView tabView;
    private readonly IReadOnlyDictionary<int, RichEditBox> editorsByBufferIndex;
    private readonly CalcResultAnimator calcResultAnimator;

    /// <summary>
    /// Raised when the user requests the tab-edit flyout (F2). The argument is
    /// the 1-based buffer index of the currently selected tab.
    /// </summary>
    public event EventHandler<int> EditFlyoutRequested;

    /// <summary>
    /// Constructs the controller with live references to the panel's collaborators.
    /// The dictionary is read through on every shortcut, so the owner can keep
    /// adding entries without re-creating the controller.
    /// </summary>
    /// <param name="tabView">The hosting <see cref="TabView"/>; used to read and set the selected index.</param>
    /// <param name="editorsByBufferIndex">Map from 1-based buffer index to its editor; used to focus on Ctrl+N selection.</param>
    /// <param name="clearConfirmOverlay">Owns the clear-confirm flow that Escape dismisses.</param>
    /// <param name="calcResultAnimator">Receives the equals-key signal for the inline calculator.</param>
    public BufferKeyboardController(
        TabView tabView,
        IReadOnlyDictionary<int, RichEditBox> editorsByBufferIndex,
        CalcResultAnimator calcResultAnimator)
    {
        if (tabView == null)
        {
            throw new ArgumentNullException(nameof(tabView));
        }

        if (editorsByBufferIndex == null)
        {
            throw new ArgumentNullException(nameof(editorsByBufferIndex));
        }

        if (calcResultAnimator == null)
        {
            throw new ArgumentNullException(nameof(calcResultAnimator));
        }

        this.tabView = tabView;
        this.editorsByBufferIndex = editorsByBufferIndex;
        this.calcResultAnimator = calcResultAnimator;
    }

    /// <summary>
    /// Handles a key press observed by the panel before any inner control sees it.
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
            int selectedIndex = tabView.SelectedIndex;
            if (selectedIndex >= 0)
            {
                EditFlyoutRequested?.Invoke(this, selectedIndex + 1);
                e.Handled = true;
                return;
            }
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

    /// <summary>
    /// Handles a key press inside a <see cref="RichEditBox"/>. Cycles buffers on
    /// Tab / Shift+Tab and forwards the equals key state to the calc animator.
    /// </summary>
    /// <param name="sender">The editor raising the event (unused).</param>
    /// <param name="e">The key event from the editor.</param>
    public void HandleEditorKey(object sender, KeyRoutedEventArgs e)
    {
        if (e == null)
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

        calcResultAnimator.TrackKeyDown(
            (int)e.Key,
            IsKeyDown(InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)));
    }

    private void CycleBuffer(int direction)
    {
        int count = tabView.TabItems.Count;
        if (count == 0)
        {
            return;
        }

        int next = ((tabView.SelectedIndex + direction) % count + count) % count;
        SelectBuffer(next);
    }

    private void SelectBuffer(int zeroBasedIndex)
    {
        if (zeroBasedIndex < 0 || zeroBasedIndex >= tabView.TabItems.Count)
        {
            return;
        }

        tabView.SelectedIndex = zeroBasedIndex;
        int bufferIndex = zeroBasedIndex + 1;
        if (editorsByBufferIndex.TryGetValue(bufferIndex, out RichEditBox editor))
        {
            editor.DispatcherQueue.TryEnqueue(() => editor.Focus(FocusState.Programmatic));
        }
    }

    private static bool IsKeyDown(Windows.UI.Core.CoreVirtualKeyStates state)
    {
        return (state & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
    }
}
