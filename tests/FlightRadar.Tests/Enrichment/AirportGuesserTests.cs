using FlightRadar.Core.Enrichment;
using FlightRadar.Core.Flights;
using FlightRadar.Core.Geo;

namespace FlightRadar.Tests.Enrichment;

public sealed class AirportGuesserTests
{
    private static readonly Airport Dublin =
        new("EIDW", "DUB", "Dublin Airport", "Dublin", "IE", new GeoPoint(53.421299, -6.27007), 242);

    private static readonly Airport LasVegas =
        new("KLAS", "LAS", "McCarran International Airport", "Las Vegas", "US", new GeoPoint(36.080101, -115.152), 2181);

    private static readonly AirportDirectory Airports = new([Dublin, LasVegas]);

    /// <summary>The point <paramref name="km"/> from <paramref name="start"/> in direction <paramref name="bearingDeg"/>.</summary>
    private static GeoPoint Offset(GeoPoint start, double bearingDeg, double km)
    {
        double angular = km / Haversine.EarthRadiusKm;
        double bearing = double.DegreesToRadians(bearingDeg);
        double lat1 = double.DegreesToRadians(start.Latitude);
        double lon1 = double.DegreesToRadians(start.Longitude);
        double lat2 = Math.Asin(Math.Sin(lat1) * Math.Cos(angular) + Math.Cos(lat1) * Math.Sin(angular) * Math.Cos(bearing));
        double lon2 = lon1 + Math.Atan2(
            Math.Sin(bearing) * Math.Sin(angular) * Math.Cos(lat1), Math.Cos(angular) - Math.Sin(lat1) * Math.Sin(lat2));
        return new GeoPoint(double.RadiansToDegrees(lat2), double.RadiansToDegrees(lon2));
    }

    private static Flight Aircraft(
        GeoPoint position, int? altitudeFt = null, int? verticalFpm = null, double? trackDeg = null, bool onGround = false) => new()
    {
        Hex = "abc123",
        Position = position,
        PositionTimestamp = DateTimeOffset.UnixEpoch,
        AltitudeFt = altitudeFt,
        VerticalRateFpm = verticalFpm,
        TrackDeg = trackDeg,
        OnGround = onGround,
    };

    // ---- In the air ----

    [Fact]
    public void Guess_ClimbingAwayNearAnAirport_IsFromIt()
    {
        // 15 km east of Dublin at 5,000 ft, climbing, heading east (away).
        var aircraft = Aircraft(Offset(Dublin.Position, 90, 15), altitudeFt: 5000, verticalFpm: 2000, trackDeg: 90);

        Assert.Equal(new AirportGuess(AirportGuessKind.From, Dublin), AirportGuesser.Guess(aircraft, Airports));
    }

    [Fact]
    public void Guess_DescendingTowardsAnAirport_IsToIt()
    {
        // 20 km west of Dublin at 4,000 ft, descending, heading east (towards).
        var aircraft = Aircraft(Offset(Dublin.Position, 270, 20), altitudeFt: 4000, verticalFpm: -800, trackDeg: 90);

        Assert.Equal(new AirportGuess(AirportGuessKind.To, Dublin), AirportGuesser.Guess(aircraft, Airports));
    }

    [Fact]
    public void Guess_DescendingButFlyingAway_IsStillToTheOnlyCandidate()
    {
        // The downwind leg of an approach flies away from the airport: direction is a preference, not a rule.
        var aircraft = Aircraft(Offset(Dublin.Position, 90, 10), altitudeFt: 3000, verticalFpm: -500, trackDeg: 90);

        Assert.Equal(AirportGuessKind.To, AirportGuesser.Guess(aircraft, Airports)!.Kind);
    }

    [Theory]
    [InlineData(30, 35000, -1000)] // cruising past, starting a descent towards somewhere else
    [InlineData(10, 12000, 2000)]  // too high for 10 km out (limit 5,500 ft above the airport)
    [InlineData(45, 6000, -800)]   // too far out
    public void Guess_TooHighOrTooFar_IsNull(double km, int altitudeFt, int verticalFpm)
    {
        var aircraft = Aircraft(Offset(Dublin.Position, 90, km), altitudeFt, verticalFpm, trackDeg: 270);

        Assert.Null(AirportGuesser.Guess(aircraft, Airports));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(250)]  // under the climb/descent threshold: level flight
    [InlineData(-250)]
    public void Guess_Level_IsNull(int verticalFpm)
    {
        // Level near an airport could be holding, the circuit, or passing by: nothing says which.
        var aircraft = Aircraft(Offset(Dublin.Position, 90, 10), altitudeFt: 3000, verticalFpm, trackDeg: 90);

        Assert.Null(AirportGuesser.Guess(aircraft, Airports));
    }

    [Theory]
    [InlineData(null, -800)]
    [InlineData(4000, null)]
    public void Guess_UnknownAltitudeOrVerticalSpeed_IsNull(int? altitudeFt, int? verticalFpm)
    {
        var aircraft = Aircraft(Offset(Dublin.Position, 90, 10), altitudeFt, verticalFpm, trackDeg: 270);

        Assert.Null(AirportGuesser.Guess(aircraft, Airports));
    }

    [Fact]
    public void Guess_Height_IsMeasuredAboveTheAirport()
    {
        // 10 km from Las Vegas (2,181 ft above sea level) at 7,000 ft: 4,819 ft above the airport, inside the
        // 5,500 ft limit. Measured from sea level it would be 7,000 ft and wrongly rejected.
        var aircraft = Aircraft(Offset(LasVegas.Position, 0, 10), altitudeFt: 7000, verticalFpm: -900, trackDeg: 180);

        Assert.Equal(new AirportGuess(AirportGuessKind.To, LasVegas), AirportGuesser.Guess(aircraft, Airports));
    }

    [Theory]
    [InlineData(90, "EAST")]  // heading east: towards the farther airport, which is preferred
    [InlineData(270, "WEST")] // heading west: towards the nearer one
    public void Guess_SeveralCandidates_PrefersTheOneItIsHeadingFor(double trackDeg, string expectedIcao)
    {
        var west = new Airport("WEST", "WWW", "West", null, null, new GeoPoint(53.4, -6.3), 0);
        var east = new Airport("EAST", "EEE", "East", null, null, Offset(west.Position, 90, 20), 0);
        var between = Offset(west.Position, 90, 8); // nearer West
        var aircraft = Aircraft(between, altitudeFt: 3000, verticalFpm: -700, trackDeg);

        Assert.Equal(expectedIcao, AirportGuesser.Guess(aircraft, new AirportDirectory([west, east]))!.Airport.Icao);
    }

    [Fact]
    public void Guess_SeveralCandidatesAndUnknownTrack_PicksTheNearest()
    {
        var west = new Airport("WEST", "WWW", "West", null, null, new GeoPoint(53.4, -6.3), 0);
        var east = new Airport("EAST", "EEE", "East", null, null, Offset(west.Position, 90, 20), 0);
        var aircraft = Aircraft(Offset(west.Position, 90, 8), altitudeFt: 3000, verticalFpm: -700, trackDeg: null);

        Assert.Equal("WEST", AirportGuesser.Guess(aircraft, new AirportDirectory([west, east]))!.Airport.Icao);
    }

    // ---- On the ground ----

    [Fact]
    public void Guess_OnTheGroundAtAnAirport_IsAtIt()
    {
        var atTheGate = Aircraft(new GeoPoint(53.4265, -6.2499), onGround: true);

        Assert.Equal(new AirportGuess(AirportGuessKind.At, Dublin), AirportGuesser.Guess(atTheGate, Airports));
    }

    [Fact]
    public void Guess_OnTheGroundAwayFromAirports_IsNull()
    {
        // 20 km away: a field, a small airfield not in the list, or a ground vehicle.
        var aircraft = Aircraft(Offset(Dublin.Position, 180, 20), onGround: true);

        Assert.Null(AirportGuesser.Guess(aircraft, Airports));
    }
}
