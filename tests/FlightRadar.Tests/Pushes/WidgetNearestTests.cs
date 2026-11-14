using FlightRadar.Api.Pushes;
using FlightRadar.Api.Watching;
using FlightRadar.Core.Flights;
using static FlightRadar.Tests.Fakes.FakeFlightDataProvider;

namespace FlightRadar.Tests.Pushes;

public sealed class WidgetNearestTests
{
    private static NearbyFlight Air(string hex, double km) => new(FlightAt(hex, 53, -6), km);

    private static NearbyFlight Ground(string hex, double km) =>
        new(FlightAt(hex, 53, -6) with { OnGround = true }, km);

    // ---- Choose ----

    [Fact]
    public void Choose_Nothing_IsNull() => Assert.Null(WidgetNearest.Choose([], currentHex: null));

    [Fact]
    public void Choose_PrefersTheNearestInTheAir_OverCloserOnesOnTheGround()
    {
        var chosen = WidgetNearest.Choose([Ground("park", 0.5), Air("far", 9), Air("near", 4)], currentHex: null);

        Assert.Equal("near", chosen?.Flight.Hex);
    }

    [Fact]
    public void Choose_OnlyAircraftOnTheGround_IsNull() =>
        Assert.Null(WidgetNearest.Choose([Ground("a", 3), Ground("b", 1)], currentHex: null));

    [Fact]
    public void Choose_CurrentLandedAndNothingElseFlies_IsNull() =>
        Assert.Null(WidgetNearest.Choose([Ground("current", 1)], currentHex: "current"));

    [Fact]
    public void Choose_AnotherSlightlyCloser_KeepsTheCurrent()
    {
        var chosen = WidgetNearest.Choose([Air("current", 5), Air("other", 4.6)], currentHex: "current");

        Assert.Equal("current", chosen?.Flight.Hex); // 8% closer: not enough to switch
    }

    [Fact]
    public void Choose_AnotherClearlyCloser_Switches()
    {
        var chosen = WidgetNearest.Choose([Air("current", 5), Air("other", 4.4)], currentHex: "current");

        Assert.Equal("other", chosen?.Flight.Hex); // 12% closer
    }

    [Fact]
    public void Choose_CurrentHasGone_TakesTheNearest()
    {
        var chosen = WidgetNearest.Choose([Air("a", 7), Air("b", 6)], currentHex: "gone");

        Assert.Equal("b", chosen?.Flight.Hex);
    }

    [Fact]
    public void Choose_CurrentLandedWhileOthersFly_SwitchesToTheAir()
    {
        var chosen = WidgetNearest.Choose([Ground("current", 1), Air("other", 8)], currentHex: "current");

        Assert.Equal("other", chosen?.Flight.Hex);
    }

    // ---- Push priority ----

    [Fact]
    public void PriorityFor_Changed_IsHigh_EvenToNothing()
    {
        Assert.Equal(PushPriority.High, WidgetPushPublisher.PriorityFor(changed: true, Air("a", 3), refreshDue: true));
        Assert.Equal(PushPriority.High, WidgetPushPublisher.PriorityFor(changed: true, chosen: null, refreshDue: true));
    }

    [Fact]
    public void PriorityFor_Changed_GoesOutEvenWhenNoRefreshIsDue() =>
        Assert.Equal(PushPriority.High, WidgetPushPublisher.PriorityFor(changed: true, Air("a", 3), refreshDue: false));

    [Fact]
    public void PriorityFor_SameAircraftInTheAir_IsANormalRefresh_OnlyWhenDue()
    {
        Assert.Equal(PushPriority.Normal, WidgetPushPublisher.PriorityFor(changed: false, Air("a", 3), refreshDue: true));
        Assert.Null(WidgetPushPublisher.PriorityFor(changed: false, Air("a", 3), refreshDue: false));
    }

    [Fact]
    public void PriorityFor_SameAndOnTheGroundOrNothing_NoPush()
    {
        Assert.Null(WidgetPushPublisher.PriorityFor(changed: false, Ground("a", 3), refreshDue: true));
        Assert.Null(WidgetPushPublisher.PriorityFor(changed: false, chosen: null, refreshDue: true));
    }

    [Fact]
    public void RefreshInterval_IsTheWidgetIntervalLessHalfATick() =>
        Assert.Equal(
            TimeSpan.FromSeconds(55),
            WidgetPushPublisher.RefreshInterval(new WatchingOptions { IntervalSeconds = 10, WidgetIntervalSeconds = 60 }));

    // ---- State ----

    private readonly WidgetNearestState state = new();
    private readonly Guid home = Guid.NewGuid();

    [Fact]
    public void Update_FirstLook_IsNeverAChange()
    {
        var (changed, chosen) = state.Update(home, [Air("a", 3)]);

        Assert.False(changed); // after a restart: a starting point, not news
        Assert.Equal("a", chosen?.Flight.Hex);
    }

    [Fact]
    public void Update_SameAircraft_NoChange_AnotherOrNone_IsAChange()
    {
        state.Update(home, [Air("a", 3)]);

        Assert.False(state.Update(home, [Air("a", 2.5)]).Changed); // moved, same aircraft
        Assert.True(state.Update(home, [Air("b", 1)]).Changed);
        Assert.True(state.Update(home, []).Changed); // clear skies
        Assert.False(state.Update(home, []).Changed);
        Assert.True(state.Update(home, [Air("c", 4)]).Changed);
    }

    [Fact]
    public void RefreshDue_NeverPushed_ThenOnlyOnceTheIntervalHasPassed()
    {
        var t0 = new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);
        var every = TimeSpan.FromSeconds(55);
        Assert.True(state.RefreshDue(home, t0, every));

        state.MarkPushed(home, t0);

        Assert.False(state.RefreshDue(home, t0.AddSeconds(10), every));
        Assert.False(state.RefreshDue(home, t0.AddSeconds(50), every));
        Assert.True(state.RefreshDue(home, t0.AddSeconds(55), every));
    }

    [Fact]
    public void KeepOnly_ForgottenLocation_ForgetsWhenItWasPushed()
    {
        var t0 = new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);
        state.MarkPushed(home, t0);

        state.KeepOnly([]);

        Assert.True(state.RefreshDue(home, t0.AddSeconds(1), TimeSpan.FromSeconds(55)));
    }

    [Fact]
    public void KeepOnly_ForgottenLocation_StartsAfresh()
    {
        state.Update(home, [Air("a", 3)]);

        state.KeepOnly([]);

        Assert.False(state.Update(home, [Air("b", 3)]).Changed); // a first look again
    }
}
