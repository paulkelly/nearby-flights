using System.Text.Json;
using System.Text.Json.Serialization;
using FlightRadar.Core.Geo;

namespace FlightRadar.Core.Enrichment;

/// <summary>An airport with scheduled passenger service.</summary>
/// <param name="ElevationFt">Height of the airfield above sea level, to turn an aircraft's altitude into its height above the airport.</param>
public sealed record Airport(
    string Icao,
    string Iata,
    string Name,
    string? City,
    string? CountryCode,
    GeoPoint Position,
    double ElevationFt);

/// <summary>
/// The airports with an IATA code (about 6,400), to find which one an aircraft is at, leaving or approaching.
/// </summary>
/// <remarks>
/// Bundled as <c>airports.json</c> (embedded in this assembly), a snapshot of Virtual Radar Server's community data
/// (CC0), refreshed with <c>scripts/update-airports.ps1</c>. Immutable after construction; one instance is shared.
/// </remarks>
public sealed class AirportDirectory
{
    private const string BundledResourceName = "FlightRadar.Core.Enrichment.airports.json";

    private readonly Airport[] _airports;

    public AirportDirectory(IEnumerable<Airport> airports)
    {
        _airports = airports.ToArray();
    }

    public int Count => _airports.Length;

    /// <summary>The list bundled with the app.</summary>
    public static AirportDirectory LoadBundled()
    {
        using var stream = typeof(AirportDirectory).Assembly.GetManifestResourceStream(BundledResourceName)
            ?? throw new InvalidOperationException($"Embedded resource {BundledResourceName} is missing.");
        var entries = JsonSerializer.Deserialize<List<BundledAirport>>(stream)
            ?? throw new InvalidOperationException($"Embedded resource {BundledResourceName} is empty.");
        return new AirportDirectory(entries.Select(entry => new Airport(
            entry.Icao, entry.Iata, entry.Name, entry.City, entry.Country, new GeoPoint(entry.Lat, entry.Lon), entry.ElevationFt)));
    }

    /// <summary>The airports within <paramref name="radiusKm"/> of <paramref name="point"/>, nearest first, with their distances.</summary>
    /// <remarks>
    /// A plain scan of every airport, but a cheap latitude/longitude box check rejects almost all of them before the
    /// (more expensive) great-circle distance. About 100 aircraft per response × 6,400 airports is well under a
    /// millisecond this way. A spatial index would be the next step if that ever mattered.
    /// </remarks>
    public IReadOnlyList<(Airport Airport, double DistanceKm)> Within(GeoPoint point, double radiusKm)
    {
        // 1° of latitude ≈ 111 km everywhere; a degree of longitude shrinks with cos(latitude). Clamp near the poles
        // so the box doesn't become infinitely wide (there are no airports up there that this would miss).
        double latDelta = radiusKm / 111.0;
        double lonDelta = radiusKm / (111.0 * Math.Max(Math.Cos(double.DegreesToRadians(point.Latitude)), 0.01));

        var matches = new List<(Airport, double)>();
        foreach (var airport in _airports)
        {
            if (Math.Abs(airport.Position.Latitude - point.Latitude) > latDelta)
            {
                continue;
            }

            // Longitude difference the short way round, so an airport just across the antimeridian is "near".
            double lonDifference = Math.Abs(airport.Position.Longitude - point.Longitude) % 360;
            if (Math.Min(lonDifference, 360 - lonDifference) > lonDelta)
            {
                continue;
            }

            double distanceKm = Haversine.DistanceKm(point, airport.Position);
            if (distanceKm <= radiusKm)
            {
                matches.Add((airport, distanceKm));
            }
        }

        return matches.OrderBy(match => match.Item2).ToList();
    }

    private sealed record BundledAirport(
        [property: JsonPropertyName("icao")] string Icao,
        [property: JsonPropertyName("iata")] string Iata,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("city")] string? City,
        [property: JsonPropertyName("country")] string? Country,
        [property: JsonPropertyName("lat")] double Lat,
        [property: JsonPropertyName("lon")] double Lon,
        [property: JsonPropertyName("elevationFt")] double ElevationFt);
}
