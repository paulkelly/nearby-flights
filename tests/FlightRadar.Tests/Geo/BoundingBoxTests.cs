using FlightRadar.Core.Geo;

namespace FlightRadar.Tests.Geo;

/// <summary>
/// Properties every answer must have (coverage, tightness, validity) over many inputs, plus the special cases: the
/// equator (a known value), the poles and the antimeridian.
/// </summary>
public sealed class BoundingBoxTests
{
    /// <summary>Allowance for floating-point rounding in degrees (~0.1 mm), nothing more.</summary>
    private const double EpsilonDeg = 1e-9;

    public static TheoryData<double, double, double> Requests()
    {
        var data = new TheoryData<double, double, double>
        {
            { 53.42, -6.27, 50 },     // Dublin
            { 0, 0, 1 },              // tiny, on the equator
            { 70, 20, 400 },          // far north, big: where cos(lat) approximations go wrong
            { -65, 140, 400 },        // far south
            { 0.5, 179.9, 50 },       // crosses the antimeridian (eastern side)
            { -10, -179.95, 120 },    // crosses it from the western side
            { 89.5, 10, 100 },        // contains the north pole
            { -89.9, -45, 30 },       // contains the south pole
            { 87, 0, 300 },           // reaches the pole from further away
        };

        var random = new Random(42);
        for (var i = 0; i < 200; i++)
        {
            data.Add(random.NextDouble() * 178 - 89, random.NextDouble() * 360 - 180, 1 + random.NextDouble() * 399);
        }
        return data;
    }

    /// <summary>Points on the circle's edge, every 0.25° of bearing (great-circle "destination point" formula).</summary>
    private static IEnumerable<GeoPoint> CircleEdge(GeoPoint centre, double radiusKm)
    {
        var δ = radiusKm / Haversine.EarthRadiusKm;
        var φ1 = double.DegreesToRadians(centre.Latitude);
        var λ1 = double.DegreesToRadians(centre.Longitude);
        for (var bearingDeg = 0.0; bearingDeg < 360; bearingDeg += 0.25)
        {
            var θ = double.DegreesToRadians(bearingDeg);
            var φ2 = Math.Asin(Math.Sin(φ1) * Math.Cos(δ) + Math.Cos(φ1) * Math.Sin(δ) * Math.Cos(θ));
            var λ2 = λ1 + Math.Atan2(Math.Sin(θ) * Math.Sin(δ) * Math.Cos(φ1), Math.Cos(δ) - Math.Sin(φ1) * Math.Sin(φ2));
            var lon = (double.RadiansToDegrees(λ2) + 540) % 360 - 180; // normalise to [-180, 180)
            yield return new GeoPoint(double.RadiansToDegrees(φ2), lon);
        }
    }

    private static bool InsideAny(IReadOnlyList<BoundingBox> boxes, GeoPoint p) =>
        boxes.Any(b => p.Latitude >= b.LatMin - EpsilonDeg && p.Latitude <= b.LatMax + EpsilonDeg &&
                       p.Longitude >= b.LonMin - EpsilonDeg && p.Longitude <= b.LonMax + EpsilonDeg);

    [Theory]
    [MemberData(nameof(Requests))]
    public void Around_AnyCircle_EveryEdgePointIsInsideABox(double lat, double lon, double radiusKm)
    {
        var centre = new GeoPoint(lat, lon);

        var boxes = BoundingBox.Around(centre, radiusKm);

        // The edge is the furthest the circle reaches; boxes are convex in lat/lon, so the inside follows.
        var missed = CircleEdge(centre, radiusKm).Where(p => !InsideAny(boxes, p)).ToList();
        Assert.True(missed.Count == 0, $"{missed.Count} edge points outside, e.g. {missed.FirstOrDefault()}");
        Assert.True(InsideAny(boxes, centre));
    }

    [Theory]
    [MemberData(nameof(Requests))]
    public void Around_AnyCircle_ReturnsValidNonWrappingBoxes(double lat, double lon, double radiusKm)
    {
        var boxes = BoundingBox.Around(new GeoPoint(lat, lon), radiusKm);

        Assert.InRange(boxes.Count, 1, 2);
        Assert.All(boxes, b =>
        {
            Assert.InRange(b.LatMin, -90, 90);
            Assert.InRange(b.LatMax, -90, 90);
            Assert.InRange(b.LonMin, -180, 180);
            Assert.InRange(b.LonMax, -180, 180);
            Assert.True(b.LatMin < b.LatMax && b.LonMin < b.LonMax, $"Empty or wrapped box {b}");
        });
    }

    [Theory]
    [MemberData(nameof(Requests))]
    public void Around_CircleAwayFromPolesAndAntimeridian_IsTight(double lat, double lon, double radiusKm)
    {
        var centre = new GeoPoint(lat, lon);
        var boxes = BoundingBox.Around(centre, radiusKm);
        if (boxes.Count != 1 || boxes[0].LonMax - boxes[0].LonMin >= 360)
        {
            return; // split or full-width boxes have edges set by ±180° rather than the circle
        }

        // Every edge of the box is touched by the circle (to within 0.01°): no wasted margin to pay for.
        var box = boxes[0];
        var edge = CircleEdge(centre, radiusKm).ToList();
        Assert.Equal(box.LatMin, edge.Min(p => p.Latitude), 0.01);
        Assert.Equal(box.LatMax, edge.Max(p => p.Latitude), 0.01);
        Assert.Equal(box.LonMin, edge.Min(p => p.Longitude), 0.01);
        Assert.Equal(box.LonMax, edge.Max(p => p.Longitude), 0.01);
    }

    [Fact]
    public void Around_OnTheEquator_SpansOneDegreeEachWayFor111Km()
    {
        // 1° of latitude, and of longitude on the equator, is 111.195 km on a 6371 km sphere.
        var box = Assert.Single(BoundingBox.Around(new GeoPoint(0, 0), 111.19492664));

        Assert.Equal(new BoundingBox(-1, -1, 1, 1), box with
        {
            LatMin = Math.Round(box.LatMin, 6), LonMin = Math.Round(box.LonMin, 6),
            LatMax = Math.Round(box.LatMax, 6), LonMax = Math.Round(box.LonMax, 6),
        });
    }

    [Fact]
    public void Around_CircleContainingThePole_IsFullWidthUpToThePole()
    {
        var box = Assert.Single(BoundingBox.Around(new GeoPoint(89.5, 10), 100));

        Assert.Equal((-180.0, 180.0, 90.0), (box.LonMin, box.LonMax, box.LatMax));
    }

    [Fact]
    public void Around_CircleCrossingTheAntimeridian_SplitsIntoTwoNarrowBoxes()
    {
        var boxes = BoundingBox.Around(new GeoPoint(0.5, 179.9), 50);

        Assert.Equal(2, boxes.Count);
        Assert.Contains(boxes, b => b.LonMax == 180);  // the eastern edge of the world...
        Assert.Contains(boxes, b => b.LonMin == -180); // ...continues at its western edge
        Assert.True(boxes.Sum(b => b.LonMax - b.LonMin) < 1, "Together they should be ~0.9° wide, not the whole world");
    }

    [Fact]
    public void Around_ZeroRadius_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BoundingBox.Around(new GeoPoint(0, 0), 0));
    }
}
