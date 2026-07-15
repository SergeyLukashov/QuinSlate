using System;

namespace QuinSlate.Ui.Services;

/// <summary>
/// Leading-edge throttle for the buffer character-limit notice. A buffer at the cap clamps every
/// single keystroke, so the raw signal arrives at typing speed; this admits the first one straight
/// away and then swallows the rest until the window has elapsed.
/// </summary>
/// <remarks>
/// Deliberately clock-injected and timer-free: the caller owns the wall clock and the auto-hide
/// timer, so the admission rule itself stays pure and testable.
/// </remarks>
public sealed class LimitNoticeThrottle
{
    /// <summary>
    /// How long a shown notice suppresses further ones. Long enough that a burst of clamped
    /// keystrokes or a flurry of pastes reads as one event, short enough that a deliberate second
    /// attempt a moment later is still acknowledged.
    /// </summary>
    private const int WindowMilliseconds = 5000;

    private DateTime? lastAdmittedUtc;

    /// <summary>
    /// Whether a notice should be shown for a clamp observed at <paramref name="utcNow"/>. True on
    /// the first call and again once <see cref="WindowMilliseconds"/> has passed since the last
    /// admitted notice; false in between.
    /// </summary>
    public bool TryAdmit(DateTime utcNow)
    {
        if (lastAdmittedUtc != null &&
            (utcNow - lastAdmittedUtc.Value).TotalMilliseconds < WindowMilliseconds)
        {
            return false;
        }

        lastAdmittedUtc = utcNow;
        return true;
    }

    /// <summary>
    /// Re-arms the throttle so the next clamp is admitted immediately. Called when the notice stops
    /// being relevant — a tab switch or the panel hiding — so the user is never left silently
    /// throttled in a context where they have not been told anything yet.
    /// </summary>
    public void Reset()
    {
        lastAdmittedUtc = null;
    }
}
