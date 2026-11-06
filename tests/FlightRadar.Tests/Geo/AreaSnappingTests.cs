using FlightRadar.Core.Geo;

namespace FlightRadar.Tests.Geo;

/// <summary>
/// The contract for AreaSnapping.Snap. These tests check *properties* that must hold for any good design (coverage,
/// sharing, cost) over many inputs, rather than exact outputs, so they don't dictate your grid or bucket sizes.
/// </summary>
public sealed class AreaSnappingTests
{
    /// <summary>
    /// The provider's limit (adsb.lol's 250 nm), not the public one (FlightsEndpoints.MaxRadiusKm, 400): the snapped
    /// circle is what gets sent upstream, so it's the provider that mustn't be asked for more.
    /// </summary>
    private const double MaxRadiusKm = 250 * Units.KmPerNauticalMile; // 463 km

    /// <summary>
    /// How much bigger the snapped radius may be. A design decision too: tighten it once you've picked your cell
    /// and bucket sizes, so the test guards your choice.
    /// </summary>
    private const double MaxExtraKm = 20;

    /// <summary>Allowance for floating-point rounding, nothing more (1 mm).</summary>
    private const double EpsilonKm = 1e-6;

    private static readonly GeoPoint Dublin = new(53.42, -6.27);

    /// <summary>
    /// Many pseudo-random requests, from a fixed seed so every run (and every machine) tests the same inputs.
    /// Includes the awkward places: high latitudes, near the poles and next to the antimeridian (±180°).
    /// </summary>
    public static TheoryData<double, double, double> Requests()
    {
        var data = new TheoryData<double, double, double>
        {
            { 53.42, -6.27, 50 },     // Dublin
            { 0.0, 0.0, 1 },          // tiny radius
            { 0.03, 179.99, 25 },     // next to the antimeridian
            { -0.03, -179.99, 25 },   // the other side of it
            { 89.95, 12.3, 30 },      // near the north pole
            { -89.95, -100.0, 30 },   // near the south pole
            { 78.22, 15.65, 80 },     // Svalbard: longitude degrees are tiny here
            { 51.47, -0.45, 400 },    // a big radius
        };

        var random = new Random(42);
        for (var i = 0; i < 200; i++)
        {
            data.Add(
                random.NextDouble() * 180 - 90,
                random.NextDouble() * 360 - 180,
                Math.Round(1 + random.NextDouble() * (MaxRadiusKm - 1), 3));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Requests))]
    public void Snap_AnyRequest_SnappedCircleContainsTheOriginal(double lat, double lon, double radiusKm)
    {
        var centre = new GeoPoint(lat, lon);

        var snapped = AreaSnapping.Snap(centre, radiusKm, MaxRadiusKm);

        // Circle B contains circle A exactly when B's radius ≥ A's radius + the distance between their centres
        // (triangle inequality: any point within r of A's centre is within r + d of B's centre).
        var centreShiftKm = Haversine.DistanceKm(centre, snapped.Centre);
        Assert.True(
            snapped.RadiusKm + EpsilonKm >= radiusKm + centreShiftKm,
            $"Snapped radius {snapped.RadiusKm:F3} km < original {radiusKm:F3} km + centre shift {centreShiftKm:F3} km");
    }

    [Theory]
    [MemberData(nameof(Requests))]
    public void Snap_AnyRequest_ReturnsAValidAreaWithinTheLimits(double lat, double lon, double radiusKm)
    {
        var snapped = AreaSnapping.Snap(new GeoPoint(lat, lon), radiusKm, MaxRadiusKm);

        Assert.InRange(snapped.Centre.Latitude, -90, 90);
        Assert.InRange(snapped.Centre.Longitude, -180, 180);
        Assert.InRange(snapped.RadiusKm, radiusKm, MaxRadiusKm);       // never smaller, never past the limit
        Assert.True(snapped.RadiusKm <= radiusKm + MaxExtraKm,         // cost: not much more to download
            $"Snapped radius {snapped.RadiusKm:F3} km is more than {MaxExtraKm} km over {radiusKm:F3} km");
    }

    [Theory]
    [MemberData(nameof(Requests))]
    public void Snap_SameRequestTwice_ReturnsTheSameArea(double lat, double lon, double radiusKm)
    {
        var centre = new GeoPoint(lat, lon);

        Assert.Equal(AreaSnapping.Snap(centre, radiusKm, MaxRadiusKm), AreaSnapping.Snap(centre, radiusKm, MaxRadiusKm));
    }

    [Theory]
    [InlineData(53.42, -6.27)]  // Dublin
    [InlineData(0.0, 0.0)]      // where the grid's lines cross
    [InlineData(-33.95, 151.18)] // Sydney
    [InlineData(69.68, 18.92)]  // Tromsø: far north
    public void Snap_ManyRequestsWithinAFewHundredMetres_ShareAtMostFourAreas(double lat, double lon)
    {
        // 100 centres scattered within ±0.003° (~300 m) of one point, same radius. If cells are bigger than that
        // scatter, it can straddle at most 4 cells (a corner), so at most 4 distinct snapped areas.
        var random = new Random(7);
        var areas = Enumerable.Range(0, 100)
            .Select(_ => new GeoPoint(
                lat + (random.NextDouble() - 0.5) * 0.006,
                lon + (random.NextDouble() - 0.5) * 0.006))
            .Select(centre => AreaSnapping.Snap(centre, 50, MaxRadiusKm))
            .Distinct()
            .ToList();

        Assert.InRange(areas.Count, 1, 4);
    }

    [Fact]
    public void Snap_SlightlyDifferentRadii_ShareAnArea()
    {
        // The radius slider sends values like 50 and 50.4; they should share a cache entry.
        var radii = new[] { 50.1, 50.4, 50.9 };

        var areas = radii.Select(radius => AreaSnapping.Snap(Dublin, radius, MaxRadiusKm)).Distinct();

        Assert.Single(areas);
    }

    [Fact]
    public void Snap_RadiusOverTheLimit_Throws()
    {
        // No answer could both cover this circle and stay within the limit, so refuse rather than return a wrong one.
        Assert.Throws<ArgumentOutOfRangeException>(() => AreaSnapping.Snap(Dublin, MaxRadiusKm + 1, MaxRadiusKm));
    }

    [Theory]
    [InlineData(450)]
    [InlineData(MaxRadiusKm)]
    public void Snap_RadiusNearTheLimit_StaysWithinTheLimitAndStillCovers(double radiusKm)
    {
        var snapped = AreaSnapping.Snap(Dublin, radiusKm, MaxRadiusKm);

        Assert.True(snapped.RadiusKm <= MaxRadiusKm, $"{snapped.RadiusKm:F3} km is past the {MaxRadiusKm} km limit");
        Assert.True(snapped.RadiusKm + EpsilonKm >= radiusKm + Haversine.DistanceKm(Dublin, snapped.Centre));
    }
}
