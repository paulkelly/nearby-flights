using FlightRadar.Api.Streaming;

namespace FlightRadar.Tests.Streaming;

public sealed class WatchThrottleTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.UnixEpoch;

    [Fact]
    public void TryTake_UpToTheBurst_IsAllowedAtOnce()
    {
        var throttle = new WatchThrottle(Start);

        for (int i = 0; i < WatchThrottle.Burst; i++)
        {
            Assert.True(throttle.TryTake(Start), $"change {i + 1} should be allowed");
        }
        Assert.False(throttle.TryTake(Start));
    }

    [Fact]
    public void TryTake_AfterTheBurst_RefillsOnePerSecond()
    {
        var throttle = new WatchThrottle(Start);
        for (int i = 0; i < WatchThrottle.Burst; i++)
        {
            throttle.TryTake(Start);
        }

        Assert.False(throttle.TryTake(Start.AddSeconds(0.9)));
        Assert.True(throttle.TryTake(Start.AddSeconds(1.0)));
        Assert.False(throttle.TryTake(Start.AddSeconds(1.5)));
        Assert.True(throttle.TryTake(Start.AddSeconds(2.0)));
    }

    [Fact]
    public void TryTake_AfterALongPause_RefillsOnlyUpToTheBurst()
    {
        var throttle = new WatchThrottle(Start);
        var later = Start.AddMinutes(10); // not 600 charges saved up

        int allowed = Enumerable.Range(0, 20).Count(_ => throttle.TryTake(later));

        Assert.Equal(WatchThrottle.Burst, allowed);
    }

    [Fact]
    public void TryTake_ClockGoingBackwards_AddsNothing()
    {
        var throttle = new WatchThrottle(Start);
        for (int i = 0; i < WatchThrottle.Burst; i++)
        {
            throttle.TryTake(Start);
        }

        Assert.False(throttle.TryTake(Start.AddSeconds(-5)));
    }

    [Fact]
    public void WaitFor_Empty_SaysHowLongUntilTheNextToken()
    {
        var throttle = new WatchThrottle(Start);
        for (int i = 0; i < WatchThrottle.Burst; i++)
        {
            throttle.TryTake(Start);
        }

        Assert.Equal(TimeSpan.FromSeconds(0.75), throttle.WaitFor(Start.AddSeconds(0.25)));
        Assert.Equal(TimeSpan.Zero, throttle.WaitFor(Start.AddSeconds(1)));
    }
}
