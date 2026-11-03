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

public sealed record FlightDto(
    string Hex,
    string? Callsign,
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
    public static FlightDto From(NearbyFlight nearby)
    {
        var flight = nearby.Flight;
        return new FlightDto(
            Hex: flight.Hex,
            Callsign: flight.Callsign,
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
