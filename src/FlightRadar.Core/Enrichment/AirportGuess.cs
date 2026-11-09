using System.Text.Json.Serialization;
using FlightRadar.Core.Flights;
using FlightRadar.Core.Geo;

namespace FlightRadar.Core.Enrichment;

/// <summary>What an aircraft appears to be doing at an airport. Serialised as text ("From", "To", "At").</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AirportGuessKind>))]
public enum AirportGuessKind
{
    /// <summary>Climbing away from it: just departed.</summary>
    From,

    /// <summary>Descending towards it: arriving.</summary>
    To,

    /// <summary>On the ground there: about to leave or just arrived (the data can't tell which).</summary>
    At,
}

public sealed record AirportGuess(AirportGuessKind Kind, Airport Airport);

/// <summary>
/// Works out, from an aircraft's own position, height and climb or descent, which airport it's leaving, approaching
/// or at. For flights with no route on record (or only a stale one): many airlines' callsigns are reassigned so often
/// that route databases can't keep up, but an aircraft climbing out of Dublin is clearly leaving Dublin.
/// </summary>
/// <remarks>
/// <para>Rules, all deliberately conservative (no guess is better than a wrong one):</para>
/// <para>
/// <b>On the ground</b> within <see cref="OnGroundRadiusKm"/> of an airport → At it.
/// </para>
/// <para>
/// <b>In the air</b>, an airport is a candidate if the aircraft is within <see cref="MaxDistanceKm"/> of it and low
/// enough for that distance: height above the airport ≤ <see cref="BaseHeightFt"/> + <see cref="HeightPerKmFt"/> ×
/// distance. That's a cone over each airport, wider than real climb-outs and approaches (a 3° approach descends about
/// 170 ft per km) so it catches them, but low enough that aircraft cruising past don't qualify.
/// Then the vertical speed decides: climbing faster than <see cref="MinVerticalSpeedFpm"/> → From, descending → To,
/// anything in between (level) → no guess.
/// </para>
/// <para>
/// <b>Which airport</b> if several qualify (London has five): prefer those the aircraft is moving the right way for
/// (heading towards an airport it's descending to, away from one it's climbing from, within 90°), then the nearest.
/// Aircraft on approach do fly away from the airport for a while (the downwind leg), so direction is a preference,
/// not a requirement.
/// </para>
/// <para>Like dead reckoning, this is a pure function of one snapshot; it's recomputed on every poll.</para>
/// </remarks>
public static class AirportGuesser
{
    public const double OnGroundRadiusKm = 5;
    public const double MaxDistanceKm = 40;
    public const double BaseHeightFt = 2_000;
    public const double HeightPerKmFt = 350;
    public const int MinVerticalSpeedFpm = 300;

    public static AirportGuess? Guess(Flight flight, AirportDirectory airports)
    {
        if (flight.OnGround)
        {
            var nearest = airports.Within(flight.Position, OnGroundRadiusKm);
            return nearest.Count > 0 ? new AirportGuess(AirportGuessKind.At, nearest[0].Airport) : null;
        }

        if (flight.AltitudeFt is not { } altitudeFt || flight.VerticalRateFpm is not { } verticalFpm)
        {
            return null;
        }

        AirportGuessKind kind;
        if (verticalFpm >= MinVerticalSpeedFpm)
        {
            kind = AirportGuessKind.From;
        }
        else if (verticalFpm <= -MinVerticalSpeedFpm)
        {
            kind = AirportGuessKind.To;
        }
        else
        {
            return null; // level: passing by, holding, or in the circuit; nothing says which airport
        }

        var candidates = airports.Within(flight.Position, MaxDistanceKm)
            .Where(match => altitudeFt - match.Airport.ElevationFt <= BaseHeightFt + HeightPerKmFt * match.DistanceKm)
            .ToList(); // still nearest first
        if (candidates.Count == 0)
        {
            return null;
        }

        var best = flight.TrackDeg is { } trackDeg
            ? candidates.FirstOrDefault(match => MovingTheRightWay(flight.Position, match.Airport, trackDeg, kind))
            : default;
        return new AirportGuess(kind, (best.Airport ?? candidates[0].Airport));
    }

    /// <summary>
    /// Arriving: is it heading towards the airport? Departing: is it heading away from it? Within 90° either way.
    /// </summary>
    private static bool MovingTheRightWay(GeoPoint position, Airport airport, double trackDeg, AirportGuessKind kind)
    {
        double towardsAirportDeg = Bearing.InitialDeg(position, airport.Position);
        double wantedDeg = kind == AirportGuessKind.To ? towardsAirportDeg : (towardsAirportDeg + 180) % 360;
        return Bearing.DifferenceDeg(trackDeg, wantedDeg) <= 90;
    }
}
