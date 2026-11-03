namespace FlightRadar.Core.Geo;

public static class Units
{
    /// <summary>One international nautical mile, in kilometres (exact by definition).</summary>
    public const double KmPerNauticalMile = 1.852;

    public static double KmToNauticalMiles(double km) => km / KmPerNauticalMile;
}
