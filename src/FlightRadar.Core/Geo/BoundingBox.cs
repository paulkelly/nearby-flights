namespace FlightRadar.Core.Geo;

/// <summary>
/// A latitude/longitude rectangle, in degrees. Never crosses the antimeridian: <see cref="LonMin"/> ≤
/// <see cref="LonMax"/>, both within [-180, 180]. For providers that search by box (OpenSky) rather than circle.
/// </summary>
public readonly record struct BoundingBox(double LatMin, double LonMin, double LatMax, double LonMax)
{
    /// <summary>Size in "square degrees" (latitude span × longitude span): the unit OpenSky prices requests in.</summary>
    public double AreaSquareDegrees => (LatMax - LatMin) * (LonMax - LonMin);

    public bool Contains(GeoPoint point) =>
        point.Latitude >= LatMin && point.Latitude <= LatMax &&
        point.Longitude >= LonMin && point.Longitude <= LonMax;

    /// <summary>
    /// The smallest box(es) containing the whole circle of <paramref name="radiusKm"/> around <paramref name="centre"/>.
    /// Usually one; two when the circle crosses the ±180° meridian, split there so neither box wraps around the world.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Latitude is simple: north-south distance is the same everywhere, so the box spans ± the circle's angular
    /// radius δ = radiusKm / R. If that reaches a pole, the circle contains the pole, every longitude is inside it,
    /// and the box is full width.
    /// </para>
    /// <para>
    /// Longitude is not ± δ / cos(lat), the common approximation: the circle's widest points aren't due east and west
    /// of the centre but nearer the pole, where meridians are closer together. The exact half-width, where the
    /// circle just touches a meridian, is asin(sin δ / cos lat). The approximation is always smaller (asin x ≥ x),
    /// so it would cut off the circle's edges, by ~2 km for a 400 km search at 70° N.
    /// </para>
    /// <para>
    /// At the antimeridian there are two options: two boxes (one each side), or one box spanning all longitudes.
    /// Two boxes is two requests, but a full-width band costs OpenSky's top credit rate and returns every aircraft in
    /// that latitude band around the world, so splitting is far cheaper.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<BoundingBox> Around(GeoPoint centre, double radiusKm)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(radiusKm);

        var angularRadius = radiusKm / Haversine.EarthRadiusKm; // δ, in radians
        var angularRadiusDeg = double.RadiansToDegrees(angularRadius);

        var latMin = centre.Latitude - angularRadiusDeg;
        var latMax = centre.Latitude + angularRadiusDeg;

        // Contains a pole: all longitudes are within the circle near it.
        if (latMax >= 90 || latMin <= -90)
        {
            return [new BoundingBox(Math.Max(latMin, -90), -180, Math.Min(latMax, 90), 180)];
        }

        // Safe: not containing a pole means δ < 90° - |lat|, so sin δ < cos lat and the asin argument is < 1.
        var halfWidthDeg = double.RadiansToDegrees(
            Math.Asin(Math.Sin(angularRadius) / Math.Cos(double.DegreesToRadians(centre.Latitude))));

        var lonMin = centre.Longitude - halfWidthDeg;
        var lonMax = centre.Longitude + halfWidthDeg;

        // Crosses the antimeridian: split into the part east of -180 and the part west of +180.
        if (lonMin < -180)
        {
            return [new BoundingBox(latMin, lonMin + 360, latMax, 180), new BoundingBox(latMin, -180, latMax, lonMax)];
        }
        if (lonMax > 180)
        {
            return [new BoundingBox(latMin, lonMin, latMax, 180), new BoundingBox(latMin, -180, latMax, lonMax - 360)];
        }

        return [new BoundingBox(latMin, lonMin, latMax, lonMax)];
    }
}
