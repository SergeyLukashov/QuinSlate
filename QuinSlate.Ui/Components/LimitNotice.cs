using QuinSlate.Ui.Constants;
using QuinSlate.Ui.Services;
using Serilog;
using System;

namespace QuinSlate.Ui.Components;

/// <summary>
/// Decides when the "this tab is full" notice is shown, and drives the <see cref="LimitNoticeView"/>
/// that shows it.
/// </summary>
/// <remarks>
/// A buffer sitting at the character cap clamps <em>every</em> keystroke, so the editor page reports
/// at typing speed. <see cref="LimitNoticeThrottle"/> turns that stream into one calm notice: the
/// first clamp is admitted immediately, the rest are swallowed — but they still hold a visible
/// notice open, so it never expires mid-burst while a key is held down.
/// </remarks>
public sealed class LimitNotice
{
    private readonly LimitNoticeView view;
    private readonly LimitNoticeThrottle throttle = new LimitNoticeThrottle();

    /// <summary>Creates the notice over <paramref name="view"/>.</summary>
    public LimitNotice(LimitNoticeView view)
    {
        if (view == null)
        {
            throw new ArgumentNullException(nameof(view));
        }

        this.view = view;
    }

    /// <summary>
    /// Reports that an edit in buffer <paramref name="bufferIndex"/> was clamped at the cap,
    /// dropping <paramref name="droppedCharacters"/> characters. Shows the notice unless the
    /// throttle is still holding one down.
    /// </summary>
    public void Report(int bufferIndex, LimitNoticeCause cause, int droppedCharacters)
    {
        if (throttle.TryAdmit(DateTime.UtcNow) == false)
        {
            view.HoldOpen();
            return;
        }

        view.Show();

        // Admitted notices only: at typing speed the suppressed ones would flood the log. Indices
        // and counts, never buffer text.
        Log.ForContext<LimitNotice>().Information(
            "Buffer {Index} hit the {Limit}-character cap; {Dropped} characters dropped ({Cause}).",
            bufferIndex,
            AppConstants.MaxBufferLength,
            droppedCharacters,
            cause);
    }

    /// <summary>
    /// Drops the notice and re-arms the throttle, so the next clamp is answered immediately. Called
    /// when the notice stops being about what the user is looking at: a tab switch, or the panel
    /// hiding.
    /// </summary>
    public void Reset()
    {
        view.DismissImmediately();
        throttle.Reset();
    }
}
