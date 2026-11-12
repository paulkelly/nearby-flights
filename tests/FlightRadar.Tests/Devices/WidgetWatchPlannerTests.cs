using FlightRadar.Api.Devices;
using FlightRadar.Core.Geo;

namespace FlightRadar.Tests.Devices;

public sealed class WidgetWatchPlannerTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;
    private static readonly TimeSpan Minute = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(10);

    private static readonly Guid Alice = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();

    private static SearchArea Area(int n) => new(new GeoPoint(50 + n, 0), 10);

    /// <summary>A watch on a fresh location, <paramref name="ageOrder"/> seconds after T0 (smaller = older).</summary>
    private static WidgetWatch Watch(Guid owner, SearchArea area, int ageOrder, Guid? location = null) =>
        new(owner, location ?? Guid.NewGuid(), area, T0.AddSeconds(ageOrder));

    // ---- Choose: the caps ----

    [Fact]
    public void Choose_MoreAreasThanTheLimit_KeepsTheOldest()
    {
        var watches = Enumerable.Range(1, 5).Select(n => Watch(Guid.NewGuid(), Area(n), ageOrder: 10 - n)).ToList();

        var plan = WidgetWatchPlanner.Choose(watches, perOwner: 3, maxAreas: 3);

        Assert.Equal([Area(5), Area(4), Area(3)], plan.Areas); // asked first, served first
        Assert.Equal(2, plan.AreasOverLimit);
    }

    [Fact]
    public void Choose_OwnerOverTheirLimit_GetsTheirOldestOnly()
    {
        var watches = new[]
        {
            Watch(Alice, Area(4), 4),
            Watch(Alice, Area(1), 1),
            Watch(Alice, Area(3), 3),
            Watch(Alice, Area(2), 2),
        };

        var plan = WidgetWatchPlanner.Choose(watches, perOwner: 3, maxAreas: 10);

        Assert.Equal([Area(1), Area(2), Area(3)], plan.Areas);
        Assert.Equal(1, plan.LocationsOverOwnerLimit);
    }

    [Fact]
    public void Choose_OwnerOverTheirLimit_DoesNotUseUpOtherPeoplesSlots()
    {
        var watches = new[]
        {
            Watch(Alice, Area(1), 1),
            Watch(Alice, Area(2), 2),
            Watch(Alice, Area(3), 3), // over Alice's limit of 2
            Watch(Bob, Area(4), 4),
        };

        var plan = WidgetWatchPlanner.Choose(watches, perOwner: 2, maxAreas: 3);

        Assert.Equal([Area(1), Area(2), Area(4)], plan.Areas);
        Assert.Equal(0, plan.AreasOverLimit);
    }

    [Fact]
    public void Choose_TwoPeopleSameArea_ShareOneSlot()
    {
        var watches = new[] { Watch(Alice, Area(1), 1), Watch(Bob, Area(1), 2), Watch(Bob, Area(2), 3) };

        var plan = WidgetWatchPlanner.Choose(watches, perOwner: 3, maxAreas: 2);

        Assert.Equal([Area(1), Area(2)], plan.Areas);
        Assert.Equal(0, plan.AreasOverLimit);
    }

    [Fact]
    public void Choose_OneLocationOnTwoDevices_CountsOnceAsOldAsItsOldest()
    {
        var home = Guid.NewGuid();
        var watches = new[]
        {
            Watch(Alice, Area(2), 2),
            Watch(Alice, Area(1), 9, location: home), // the tablet asked later…
            Watch(Alice, Area(1), 1, location: home), // …than the phone
            Watch(Alice, Area(3), 3),
        };

        var plan = WidgetWatchPlanner.Choose(watches, perOwner: 3, maxAreas: 10);

        Assert.Equal([Area(1), Area(2), Area(3)], plan.Areas);
        Assert.Equal(0, plan.LocationsOverOwnerLimit);
    }

    [Fact]
    public void Choose_Locations_AreThoseInsideTheCaps()
    {
        Guid aliceHome = Guid.NewGuid(), aliceWork = Guid.NewGuid(), bobHome = Guid.NewGuid(), bobFar = Guid.NewGuid();
        var watches = new[]
        {
            Watch(Alice, Area(1), 1, aliceHome),
            Watch(Alice, Area(2), 2, aliceWork), // over Alice's limit of 1
            Watch(Bob, Area(1), 3, bobHome),     // shares Alice's area: in
            Watch(Bob, Area(3), 4, bobFar),      // over the overall limit of 1 area
        };

        var plan = WidgetWatchPlanner.Choose(watches, perOwner: 1, maxAreas: 1);

        Assert.Equal(new HashSet<Guid> { aliceHome, bobHome }, plan.Locations);
    }

    [Fact]
    public void Choose_Nothing_WatchesNothing()
    {
        var plan = WidgetWatchPlanner.Choose([], perOwner: 3, maxAreas: 10);

        Assert.Empty(plan.Areas);
    }

    // ---- Due: the 60 s rhythm ----

    [Fact]
    public void Due_NeverHandedOut_IsDue()
    {
        Assert.Equal([Area(1)], WidgetWatchPlanner.Due([Area(1)], new Dictionary<SearchArea, DateTimeOffset>(), T0, Minute, Tick));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(50, false)]
    [InlineData(54.9, false)]
    [InlineData(55, true)]    // half a tick early: ticks don't land exactly on the second
    [InlineData(59.98, true)]
    [InlineData(60, true)]
    [InlineData(600, true)]
    public void Due_HandedOutEarlier_DueOnceTheIntervalHasAlmostPassed(double secondsAgo, bool due)
    {
        var last = new Dictionary<SearchArea, DateTimeOffset> { [Area(1)] = T0 };

        var result = WidgetWatchPlanner.Due([Area(1)], last, T0.AddSeconds(secondsAgo), Minute, Tick);

        Assert.Equal(due, result.Count == 1);
    }

    [Fact]
    public void Due_SomeDueSomeNot_ReturnsOnlyTheDue()
    {
        var last = new Dictionary<SearchArea, DateTimeOffset> { [Area(1)] = T0, [Area(2)] = T0.AddSeconds(30) };

        var result = WidgetWatchPlanner.Due([Area(1), Area(2), Area(3)], last, T0.AddSeconds(60), Minute, Tick);

        Assert.Equal([Area(1), Area(3)], result);
    }
}
