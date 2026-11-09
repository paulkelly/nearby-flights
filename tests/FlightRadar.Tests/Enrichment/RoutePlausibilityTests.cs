using FlightRadar.Core.Enrichment;
using FlightRadar.Core.Flights;
using FlightRadar.Core.Geo;

namespace FlightRadar.Tests.Enrichment;

/// <summary>Real airports; aircraft positions computed along (or off) the real great-circle paths.</summary>
public sealed class RoutePlausibilityTests
{
    private static readonly RouteAirport Dublin = Airport("EIDW", 53.421299, -6.27007);
    private static readonly RouteAirport Chicago = Airport("KORD", 41.9786, -87.9048);
    private static readonly RouteAirport Heathrow = Airport("EGLL", 51.4706, -0.461941);
    private static readonly RouteAirport Kalamata = Airport("LGKL", 37.0683, 22.0255);
    private static readonly RouteAirport Bergamo = Airport("LIME", 45.6739, 9.70417);

    /// <summary>30% of the way from Dublin to Chicago, over the Atlantic; the direction to Chicago is due west.</summary>
    private static readonly GeoPoint MidAtlantic = new(56.72, -33.65);

    /// <summary>Halfway from Dublin to Heathrow, over Wales; the direction to Heathrow is ~119°.</summary>
    private static readonly GeoPoint OverWales = new(52.48, -3.3);

    private static RouteAirport Airport(string icao, double lat, double lon) =>
        new(icao, null, icao, null, null, new GeoPoint(lat, lon));

    private static FlightRoute Route(params RouteAirport[] airports) => new(airports);

    private static Flight Aircraft(GeoPoint position, double? trackDeg, bool onGround = false) => new()
    {
        Hex = "abc123",
        Position = position,
        PositionTimestamp = DateTimeOffset.UnixEpoch,
        TrackDeg = trackDeg,
        OnGround = onGround,
    };

    // ---- On the way ----

    [Fact]
    public void IsPlausible_OnTheGreatCircleHeadingForTheDestination_IsTrue()
    {
        Assert.True(RoutePlausibility.IsPlausible(Route(Dublin, Chicago), Aircraft(MidAtlantic, trackDeg: 270)));
    }

    [Fact]
    public void IsPlausible_FarFromTheRoute_IsFalse()
    {
        // The case seen live: a "Kalamata → Bergamo" route for an aircraft near Dublin (a 2,865 km detour on a
        // 1,402 km leg).
        var nearDublin = new GeoPoint(53.5, -6.4);

        Assert.False(RoutePlausibility.IsPlausible(Route(Kalamata, Bergamo), Aircraft(nearDublin, trackDeg: 120)));
    }

    [Fact]
    public void IsPlausible_SomewhatOffTheDirectLine_IsTrue()
    {
        // North of the direct Dublin–Heathrow line (a 37 km detour): routings, weather and vectoring do that.
        var northOfLine = new GeoPoint(53.3, -2.9);

        Assert.True(RoutePlausibility.IsPlausible(Route(Dublin, Heathrow), Aircraft(northOfLine, trackDeg: 140)));
    }

    [Fact]
    public void IsPlausible_WellOffAShortLeg_IsFalse()
    {
        // ~300 km south of the Dublin–Heathrow midpoint (over the Channel): a 279 km detour, 140 km allowed.
        var overTheChannel = new GeoPoint(49.78, -3.3);

        Assert.False(RoutePlausibility.IsPlausible(Route(Dublin, Heathrow), Aircraft(overTheChannel, trackDeg: 60)));
    }

    // ---- Direction ----

    [Fact]
    public void IsPlausible_OnTheRouteButFlyingTheOtherWay_IsFalse()
    {
        // Over the Atlantic heading east: that's the Chicago → Dublin flight, not this one (a reused flight number).
        Assert.False(RoutePlausibility.IsPlausible(Route(Dublin, Chicago), Aircraft(MidAtlantic, trackDeg: 90)));
    }

    [Theory]
    [InlineData(119 + 85)]  // just inside the tolerance either side of the direct direction (~119°)
    [InlineData(119 - 85)]
    public void IsPlausible_TrackWithinTheTolerance_IsTrue(double trackDeg)
    {
        Assert.True(RoutePlausibility.IsPlausible(Route(Dublin, Heathrow), Aircraft(OverWales, trackDeg)));
    }

    [Fact]
    public void IsPlausible_UnknownTrack_ChecksPositionOnly()
    {
        Assert.True(RoutePlausibility.IsPlausible(Route(Dublin, Chicago), Aircraft(MidAtlantic, trackDeg: null)));
    }

    [Fact]
    public void IsPlausible_NearAnAirport_IgnoresDirection()
    {
        // 20 km from Dublin, pointing back west: a departure turn, holding, or vectors onto final.
        var justOffDublin = new GeoPoint(53.42, -5.97);

        Assert.True(RoutePlausibility.IsPlausible(Route(Dublin, Heathrow), Aircraft(justOffDublin, trackDeg: 270)));
    }

    // ---- On the ground ----

    [Theory]
    [InlineData(53.4265, -6.2499)] // at the gate in Dublin, waiting to leave for Heathrow
    [InlineData(51.4700, -0.4543)] // at Heathrow, just landed
    public void IsPlausible_OnTheGroundAtEitherEnd_IsTrue(double lat, double lon)
    {
        var aircraft = Aircraft(new GeoPoint(lat, lon), trackDeg: 300, onGround: true);

        Assert.True(RoutePlausibility.IsPlausible(Route(Dublin, Heathrow), aircraft));
    }

    [Fact]
    public void IsPlausible_OnTheGroundAtAnotherAirport_IsFalse()
    {
        // Parked at Chicago with a Dublin → Heathrow route: stale.
        var aircraft = Aircraft(Chicago.Position, trackDeg: null, onGround: true);

        Assert.False(RoutePlausibility.IsPlausible(Route(Dublin, Heathrow), aircraft));
    }

    // ---- Routes with stops ----

    [Fact]
    public void IsPlausible_OnAnyLeg_IsTrue()
    {
        // Heathrow → Dublin → Chicago, currently on the second leg.
        var route = Route(Heathrow, Dublin, Chicago);

        Assert.True(RoutePlausibility.IsPlausible(route, Aircraft(MidAtlantic, trackDeg: 270)));
    }

    [Fact]
    public void IsPlausible_OnNoLeg_IsFalse()
    {
        // Kalamata → Bergamo → Heathrow, but over the Atlantic.
        var route = Route(Kalamata, Bergamo, Heathrow);

        Assert.False(RoutePlausibility.IsPlausible(route, Aircraft(MidAtlantic, trackDeg: 270)));
    }
}
