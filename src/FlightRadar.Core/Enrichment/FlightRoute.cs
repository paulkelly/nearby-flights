using FlightRadar.Core.Geo;

namespace FlightRadar.Core.Enrichment;

/// <summary>
/// The airports a flight number is scheduled to visit, in order: origin first, destination last, any stops between
/// ("EIN123": Dublin → Chicago).
/// </summary>
/// <remarks>
/// From a route database keyed by callsign, not from the aircraft itself: ADS-B doesn't broadcast routes. Route
/// databases are community-maintained and airlines reuse flight numbers, so a route is only ever "probably" right.
/// </remarks>
public sealed record FlightRoute(IReadOnlyList<RouteAirport> Airports);

/// <param name="Icao">4-letter ICAO code, e.g. "EIDW". Always present.</param>
/// <param name="Iata">3-letter IATA code, e.g. "DUB": what passengers see on tickets. Not every airport has one.</param>
/// <param name="Name">e.g. "Dublin Airport".</param>
/// <param name="City">The place it serves, e.g. "Dublin".</param>
/// <param name="CountryCode">ISO 3166 two-letter code, e.g. "IE".</param>
public sealed record RouteAirport(
    string Icao,
    string? Iata,
    string Name,
    string? City,
    string? CountryCode,
    GeoPoint Position);
