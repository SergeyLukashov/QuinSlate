using QuinSlate.Ui.Services;

namespace QuinSlate.Tests.Services;

public sealed class LimitNoticeThrottleTests
{
    private static readonly DateTime Start = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    // The window is private to the throttle; these bracket it from the outside.
    private const int InsideWindowMs = 4999;
    private const int WindowMs = 5000;

    [Fact]
    public void TryAdmit_FirstClamp_IsAdmitted()
    {
        var throttle = new LimitNoticeThrottle();

        Assert.True(throttle.TryAdmit(Start));
    }

    [Fact]
    public void TryAdmit_BurstWithinWindow_IsAdmittedOnce()
    {
        var throttle = new LimitNoticeThrottle();
        int admitted = 0;

        // A key held down at the cap: one clamp report every few milliseconds.
        for (int i = 0; i < 200; i++)
        {
            if (throttle.TryAdmit(Start.AddMilliseconds(i * 20)))
            {
                admitted++;
            }
        }

        Assert.Equal(1, admitted);
    }

    [Fact]
    public void TryAdmit_JustInsideWindow_IsSuppressed()
    {
        var throttle = new LimitNoticeThrottle();
        throttle.TryAdmit(Start);

        Assert.False(throttle.TryAdmit(Start.AddMilliseconds(InsideWindowMs)));
    }

    [Fact]
    public void TryAdmit_AfterWindowElapsed_IsAdmittedAgain()
    {
        var throttle = new LimitNoticeThrottle();
        throttle.TryAdmit(Start);

        Assert.True(throttle.TryAdmit(Start.AddMilliseconds(WindowMs)));
    }

    [Fact]
    public void TryAdmit_WindowRunsFromLastAdmitted_NotFromLastAttempt()
    {
        var throttle = new LimitNoticeThrottle();
        throttle.TryAdmit(Start);

        // A suppressed attempt must not push the window out, or continuous typing at the cap would
        // never let a second notice through.
        Assert.False(throttle.TryAdmit(Start.AddMilliseconds(InsideWindowMs)));
        Assert.True(throttle.TryAdmit(Start.AddMilliseconds(WindowMs)));
    }

    [Fact]
    public void Reset_ReArmsImmediately()
    {
        var throttle = new LimitNoticeThrottle();
        throttle.TryAdmit(Start);

        throttle.Reset();

        Assert.True(throttle.TryAdmit(Start.AddMilliseconds(1)));
    }
}
