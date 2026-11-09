using FlightRadar.Core.Enrichment;
using FlightRadar.Core.Flights;

namespace FlightRadar.Api.Flights;

// Public JSON contract of GET /api/flights. These are what the frontend sees, so changes here are API changes.
// Serialised as camelCase (ASP.NET Core's default), e.g. "radiusKm", "altitudeFt".

/// <summary>Aircraft within <see cref="RadiusKm"/> of (<see cref="Lat"/>, <see cref="Lon"/>), nearest first.</summary>
public sealed record FlightsResponse(
    double Lat,
    double Lon,
    double RadiusKm,
    int Count,
    DateTimeOffset FetchedAt,
    IReadOnlyList<FlightDto> Flights);

/// <param name="Airline">
/// The airline, worked out from an airline-style callsign ("EIN123" → "Aer Lingus"); null for other callsigns
/// (private aircraft often use their registration) and unknown codes.
/// </param>
/// <param name="Route">
/// The route usually flown under this callsign, if one is known and fits the aircraft's position and direction
/// (see RoutePlausibility). Still only probable.
/// </param>
public sealed record FlightDto(
    string Hex,
    string? Callsign,
    string? Airline,
    RouteDto? Route,
    string? Registration,
    string? AircraftType,
    double Lat,
    double Lon,
    DateTimeOffset PositionTimestamp,
    double DistanceKm,
    int? AltitudeFt,
    bool OnGround,
    double? GroundSpeedKt,
    double? TrackDeg,
    int? VerticalRateFpm)
{
    /// <param name="routes">Routes found for this response's callsigns (see RouteLookup), keyed by callsign.</param>
    public static FlightDto From(
        NearbyFlight nearby, AirlineDirectory airlines, IReadOnlyDictionary<string, FlightRoute> routes)
    {
        var flight = nearby.Flight;
        return new FlightDto(
            Hex: flight.Hex,
            Callsign: flight.Callsign,
            Airline: airlines.NameForCallsign(flight.Callsign),
            // Only a route that fits where the aircraft is: route data can be stale, and none beats a wrong one.
            Route: flight.Callsign is { } callsign
                   && routes.TryGetValue(callsign, out var route)
                   && RoutePlausibility.IsPlausible(route, flight)
                ? RouteDto.From(route)
                : null,
            Registration: flight.Registration,
            AircraftType: flight.AircraftType,
            Lat: flight.Position.Latitude,
            Lon: flight.Position.Longitude,
            PositionTimestamp: flight.PositionTimestamp,
            DistanceKm: Math.Round(nearby.DistanceKm, 2),
            AltitudeFt: flight.AltitudeFt,
            OnGround: flight.OnGround,
            GroundSpeedKt: flight.GroundSpeedKt,
            TrackDeg: flight.TrackDeg,
            VerticalRateFpm: flight.VerticalRateFpm);
    }
}

/// <summary>The airports in order: origin first, destination last, any stops between.</summary>
public sealed record RouteDto(IReadOnlyList<AirportDto> Airports)
{
    public static RouteDto From(FlightRoute route) =>
        new(route.Airports.Select(airport => new AirportDto(
            airport.Icao, airport.Iata, airport.Name, airport.City, airport.CountryCode)).ToList());
}

/// <param name="Icao">e.g. "EIDW".</param>
/// <param name="Iata">e.g. "DUB", the code on tickets and departure boards; not every airport has one.</param>
/// <param name="Name">e.g. "Dublin Airport".</param>
/// <param name="City">e.g. "Dublin".</param>
/// <param name="CountryCode">ISO two-letter code, e.g. "IE".</param>
public sealed record AirportDto(string Icao, string? Iata, string Name, string? City, string? CountryCode);
