using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;

namespace Jott.Ui.Services;

/// <summary>
/// Manages the two-step clear-confirm flow for buffer tabs.
/// When the user clicks the clear button, the service enters a confirming
/// state that swaps the normal header panel for a dismiss-or-confirm prompt.
/// The prompt auto-cancels after a fixed timeout.
/// </summary>
/// <remarks>
/// The service updates the visibility of UI elements directly via the
/// dictionary references passed at construction time. It does not own
/// those elements and never removes them.
/// Raises <see cref="Cleared"/> so the caller can clear the editor content
/// and update any dependent UI (e.g. the clear-button enabled state).
/// </remarks>
public sealed class ClearConfirmService
{
    private const int ConfirmAutoCancelMilliseconds = 4000;

    private readonly Dictionary<int, FrameworkElement> normalPanels;
    private readonly Dictionary<int, Border> confirmPanels;

    private int confirmingBufferIndex = -1;
    private DispatcherTimer confirmCancelTimer;

    /// <summary>
    /// Raised when the user confirms the clear operation.
    /// The argument is the 1-based buffer index that was cleared.
    /// </summary>
    public event EventHandler<int> Cleared;

    /// <summary>
    /// Initialises the service with the live panel-visibility dictionaries
    /// maintained by the owning view.
    /// </summary>
    /// <param name="normalPanels">Maps buffer index → the panel shown in normal state.</param>
    /// <param name="confirmPanels">Maps buffer index → the confirm-overlay panel.</param>
    public ClearConfirmService(
        Dictionary<int, FrameworkElement> normalPanels,
        Dictionary<int, Border> confirmPanels)
    {
        if (normalPanels == null)
        {
            throw new ArgumentNullException(nameof(normalPanels));
        }

        if (confirmPanels == null)
        {
            throw new ArgumentNullException(nameof(confirmPanels));
        }

        this.normalPanels = normalPanels;
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
        SetPanelVisibility(bufferIndex, normalVisible: false, confirmVisible: true);

        StopTimer();

        confirmCancelTimer = new DispatcherTimer();
        confirmCancelTimer.Interval = TimeSpan.FromMilliseconds(ConfirmAutoCancelMilliseconds);
        confirmCancelTimer.Tick += OnTimerTick;
        confirmCancelTimer.Start();
    }

    /// <summary>
    /// Cancels the confirm state for whichever buffer is currently confirming,
    /// restoring the normal panel. No-op if nothing is confirming.
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

        SetPanelVisibility(index, normalVisible: true, confirmVisible: false);
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

    private void SetPanelVisibility(int bufferIndex, bool normalVisible, bool confirmVisible)
    {
        if (normalPanels.TryGetValue(bufferIndex, out FrameworkElement normalPanel))
        {
            normalPanel.Visibility = normalVisible ? Visibility.Visible : Visibility.Collapsed;
        }

        if (confirmPanels.TryGetValue(bufferIndex, out Border confirmPanel))
        {
            confirmPanel.Visibility = confirmVisible ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
