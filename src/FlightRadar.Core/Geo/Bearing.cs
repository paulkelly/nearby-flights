namespace FlightRadar.Core.Geo;

/// <summary>Directions between points on a sphere.</summary>
public static class Bearing
{
    /// <summary>
    /// The compass direction to set off in to reach <paramref name="to"/> from <paramref name="from"/> along a great
    /// circle: 0–360° clockwise from true north, like an aircraft's track. ("Initial", because on a great circle
    /// the bearing changes along the way, unless you're travelling due north/south or along the equator.)
    /// </summary>
    public static double InitialDeg(GeoPoint from, GeoPoint to)
    {
        double lat1 = double.DegreesToRadians(from.Latitude);
        double lat2 = double.DegreesToRadians(to.Latitude);
        double dLon = double.DegreesToRadians(to.Longitude - from.Longitude);

        double y = Math.Sin(dLon) * Math.Cos(lat2);
        double x = Math.Cos(lat1) * Math.Sin(lat2) - Math.Sin(lat1) * Math.Cos(lat2) * Math.Cos(dLon);

        // Atan2 gives -180..180; shift to 0..360.
        return (double.RadiansToDegrees(Math.Atan2(y, x)) + 360) % 360;
    }

    /// <summary>The smaller angle between two directions, 0–180°: 350° and 10° are 20° apart, not 340°.</summary>
    public static double DifferenceDeg(double a, double b)
    {
        double difference = Math.Abs(a - b) % 360;
        return difference > 180 ? 360 - difference : difference;
    }
}
