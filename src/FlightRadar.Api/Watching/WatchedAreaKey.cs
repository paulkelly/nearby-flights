using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using FlightRadar.Core.Geo;

namespace FlightRadar.Api.Watching;

/// <summary>
/// An area as text ("53.4:-6.3:25") and back, for storing in Redis. Round-trip ("R") formatting with the invariant
/// culture, like the cache keys: the same area always gives the same text, and parsing gives back the exact doubles.
/// </summary>
public static class WatchedAreaKey
{
    public static string Format(SearchArea area) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{area.Centre.Latitude:R}:{area.Centre.Longitude:R}:{area.RadiusKm:R}");

    public static bool TryParse(string? text, [NotNullWhen(true)] out SearchArea? area)
    {
        area = null;
        var parts = text?.Split(':');
        if (parts is not { Length: 3 }
            || !TryParseDouble(parts[0], out double lat) || lat is < -90 or > 90
            || !TryParseDouble(parts[1], out double lon) || lon is < -180 or > 180
            || !TryParseDouble(parts[2], out double radiusKm) || radiusKm <= 0)
        {
            return false;
        }

        area = new SearchArea(new GeoPoint(lat, lon), radiusKm);
        return true;
    }

    private static bool TryParseDouble(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
}
