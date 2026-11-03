namespace FlightRadar.Core.Geo;

/// <summary>A position on the Earth's surface, in decimal degrees (WGS84).</summary>
/// <param name="Latitude">-90 (south pole) to 90 (north pole).</param>
/// <param name="Longitude">-180 to 180, negative is west of Greenwich.</param>
public readonly record struct GeoPoint(double Latitude, double Longitude);
