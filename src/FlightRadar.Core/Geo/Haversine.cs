namespace FlightRadar.Core.Geo;

/// <summary>
/// Great-circle distance between two points, treating the Earth as a sphere.
/// Error versus the real (slightly squashed) Earth is up to ~0.5%, which is fine for "is this aircraft in my circle".
/// </summary>
public static class Haversine
{
    /// <summary>Mean Earth radius (IUGG). The tests' expected values assume this radius.</summary>
    public const double EarthRadiusKm = 6371.0;

    private const double DegToRad = Math.PI / 180;

    public static double DistanceKm(GeoPoint a, GeoPoint b)
    {
        double lat1 = a.Latitude * DegToRad;
        double lat2 = b.Latitude * DegToRad;
        double sinHalfDLat = Math.Sin((b.Latitude - a.Latitude) * DegToRad / 2);
        double sinHalfDLon = Math.Sin((b.Longitude - a.Longitude) * DegToRad / 2);

        double h = sinHalfDLat * sinHalfDLat
                 + Math.Cos(lat1) * Math.Cos(lat2) * sinHalfDLon * sinHalfDLon;
        h = Math.Clamp(h, 0, 1); // guard against rounding pushing h past 1 (→ NaN)

        return 2 * EarthRadiusKm * Math.Atan2(Math.Sqrt(h), Math.Sqrt(1 - h));
    }
}
