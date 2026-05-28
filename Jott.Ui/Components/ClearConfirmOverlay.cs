using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;

namespace Jott.Ui.Components;

/// <summary>
/// Manages the two-step clear-confirm flow for buffer tabs.
/// When the user clicks the clear button, the component enters a confirming
/// state that shows the confirm-or-dismiss prompt. The prompt auto-cancels
/// after a fixed timeout.
/// </summary>
/// <remarks>
/// The component updates the visibility of the confirm panels directly via the
/// dictionary reference passed at construction time. It does not own those
/// elements and never removes them. The owning view is responsible for the
/// clear-button visibility, since hover-driven visibility is the view's concern.
/// Raises <see cref="Cleared"/> so the caller can clear the editor content
/// and update any dependent UI (e.g. the clear-button enabled state).
/// </remarks>
public sealed class ClearConfirmOverlay
{
    private const int ConfirmAutoCancelMilliseconds = 4000;

    private readonly Dictionary<int, Border> confirmPanels;

    private int confirmingBufferIndex = -1;
    private DispatcherTimer confirmCancelTimer;

    /// <summary>
    /// Raised when the user confirms the clear operation.
    /// The argument is the 1-based buffer index that was cleared.
    /// </summary>
    public event EventHandler<int> Cleared;

    /// <summary>
    /// Initialises the component with the live confirm-panel dictionary
    /// maintained by the owning view.
    /// </summary>
    /// <param name="confirmPanels">Maps buffer index → the confirm-overlay panel.</param>
    public ClearConfirmOverlay(Dictionary<int, Border> confirmPanels)
    {
        if (confirmPanels == null)
        {
            throw new ArgumentNullException(nameof(confirmPanels));
        }

        this.confirmPanels = confirmPanels;
    }

    /// <summary>Gets whether any buffer is currently in the confirming state.</summary>
    public bool IsConfirming => confirmingBufferIndex != -1;

    /// <summary>
    /// Transitions the given buffer into the confirm state, showing the
    /// confirm panel and starting the auto-cancel timer.
    /// If another buffer is already confirming, it is first dismissed.
    /// </summary>
    /// <param name="bufferIndex">1-based index of the buffer whose clear was requested.</param>
    public void Enter(int bufferIndex)
    {
        if (confirmingBufferIndex != -1 && confirmingBufferIndex != bufferIndex)
        {
            Exit();
        }

        confirmingBufferIndex = bufferIndex;
        SetConfirmPanelVisibility(bufferIndex, visible: true);

        StopTimer();

        confirmCancelTimer = new DispatcherTimer();
        confirmCancelTimer.Interval = TimeSpan.FromMilliseconds(ConfirmAutoCancelMilliseconds);
        confirmCancelTimer.Tick += OnTimerTick;
        confirmCancelTimer.Start();
    }

    /// <summary>
    /// Cancels the confirm state for whichever buffer is currently confirming,
    /// hiding the confirm panel. No-op if nothing is confirming.
    /// </summary>
    public void Exit()
    {
        StopTimer();

        int index = confirmingBufferIndex;
        confirmingBufferIndex = -1;

        if (index == -1)
        {
            return;
        }

        SetConfirmPanelVisibility(index, visible: false);
    }

    /// <summary>
    /// Confirms the clear for the given buffer: exits the confirm state
    /// and raises <see cref="Cleared"/>.
    /// </summary>
    /// <param name="bufferIndex">1-based index of the buffer to clear.</param>
    public void Confirm(int bufferIndex)
    {
        Exit();
        Cleared?.Invoke(this, bufferIndex);
    }

    private void OnTimerTick(object sender, object e)
    {
        Exit();
    }

    private void StopTimer()
    {
        if (confirmCancelTimer == null)
        {
            return;
        }

        confirmCancelTimer.Stop();
        confirmCancelTimer.Tick -= OnTimerTick;
        confirmCancelTimer = null;
    }

    private void SetConfirmPanelVisibility(int bufferIndex, bool visible)
    {
        if (confirmPanels.TryGetValue(bufferIndex, out Border confirmPanel))
        {
            confirmPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
