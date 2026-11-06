namespace FlightRadar.Core.Geo;

/// <summary>
/// Conversions between the metric units some providers use (OpenSky: metres, m/s) and the aviation units our
/// model uses (feet, knots, feet per minute), which pilots, controllers and adsb.lol use.
/// </summary>
public static class Units
{
    /// <summary>One international nautical mile, in kilometres (exact by definition).</summary>
    public const double KmPerNauticalMile = 1.852;

    /// <summary>One international foot, in metres (exact by definition).</summary>
    public const double MetresPerFoot = 0.3048;

    public static double KmToNauticalMiles(double km) => km / KmPerNauticalMile;

    public static double MetresToFeet(double metres) => metres / MetresPerFoot;

    /// <summary>Knots are nautical miles per hour: m/s × 3600 s/h ÷ 1852 m/nm.</summary>
    public static double MetresPerSecondToKnots(double metresPerSecond) =>
        metresPerSecond * 3600 / (KmPerNauticalMile * 1000);

    public static double MetresPerSecondToFeetPerMinute(double metresPerSecond) =>
        metresPerSecond * 60 / MetresPerFoot;
}