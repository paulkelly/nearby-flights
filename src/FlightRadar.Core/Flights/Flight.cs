using FlightRadar.Core.Geo;

namespace FlightRadar.Core.Flights;

/// <summary>
/// One aircraft as seen by a flight data provider, in our own units and shape.
/// ADS-B data is patchy, so anything an aircraft might not broadcast is nullable.
/// </summary>
public sealed record Flight
{
    /// <summary>ICAO 24-bit address as 6 hex characters, e.g. "4ca7b5". Unique per airframe, so it is the stable ID.</summary>
    public required string Hex { get; init; }

    /// <summary>Flight number as broadcast, e.g. "RYR4TN". Often missing or padded on the wire; providers trim it.</summary>
    public string? Callsign { get; init; }

    /// <summary>Tail number, e.g. "EI-DCL".</summary>
    public string? Registration { get; init; }

    /// <summary>ICAO type designator, e.g. "B738" for a Boeing 737-800.</summary>
    public string? AircraftType { get; init; }

    public required GeoPoint Position { get; init; }

    /// <summary>When <see cref="Position"/> was last reported. Lets clients extrapolate movement between updates.</summary>
    public required DateTimeOffset PositionTimestamp { get; init; }

    /// <summary>Barometric altitude in feet. Null when unknown or when <see cref="OnGround"/> is true.</summary>
    public int? AltitudeFt { get; init; }

    public bool OnGround { get; init; }

    /// <summary>Speed over the ground in knots.</summary>
    public double? GroundSpeedKt { get; init; }

    /// <summary>Direction of travel over the ground, 0–360 degrees clockwise from true north.</summary>
    public double? TrackDeg { get; init; }

    /// <summary>Climb (positive) or descent (negative) rate in feet per minute.</summary>
    public int? VerticalRateFpm { get; init; }
}
