using FlightRadar.Core.Flights;
using FlightRadar.Core.Geo;

namespace FlightRadar.Core.Enrichment;

/// <summary>
/// Does a route make sense for where the aircraft actually is? Route databases go stale and airlines reuse flight
/// numbers, so a lookup can say "Kalamata → Bergamo" for a plane over Dublin. Better to show no route than a wrong one.
/// </summary>
/// <remarks>
/// <para>
/// A route is plausible if the aircraft fits any one of its legs (origin → stop → destination). Fitting a leg
/// takes two checks:
/// </para>
/// <para>
/// 1. <b>On the way.</b> Going via the aircraft's position must not be much longer than flying the leg directly:
/// <c>d(from, aircraft) + d(aircraft, to) ≤ d(from, to) × <see cref="MaxDetourFactor"/> + <see cref="DetourSlackKm"/></c>.
/// The points that pass form an ellipse with the two airports as its foci, fat enough for real flight paths
/// (airways, oceanic tracks, weather), with slack around the airports for departures, approaches and holding.
/// An aircraft at either airport always passes, so planes at the gate, taxiing or just landed keep their route.
/// </para>
/// <para>
/// 2. <b>Heading the right way.</b> An airborne aircraft more than <see cref="NearAirportKm"/> from both airports
/// must be tracking within <see cref="MaxTrackOffDeg"/> of the direction to the destination. Otherwise a route
/// in the opposite direction (a reused flight number) would pass the first check. Near the airports aircraft turn
/// all over the place (departure turns, holding, vectors onto final), so there's no direction check there.
/// </para>
/// <para>
/// Both thresholds are deliberately generous: the aim is to hide routes that are clearly wrong, not to second-guess
/// air traffic control.
/// </para>
/// </remarks>
public static class RoutePlausibility
{
    /// <summary>How much longer than the direct leg the trip via the aircraft may be (1.2 = 20% longer).</summary>
    public const double MaxDetourFactor = 1.2;

    /// <summary>Extra allowance on top, which matters most for short legs (approaches, holding, vectoring).</summary>
    public const double DetourSlackKm = 50;

    /// <summary>Within this distance of either airport, the direction of travel isn't checked.</summary>
    public const double NearAirportKm = 50;

    /// <summary>How far the aircraft's track may point away from the destination.</summary>
    public const double MaxTrackOffDeg = 90;

    public static bool IsPlausible(FlightRoute route, Flight flight)
    {
        for (int i = 0; i + 1 < route.Airports.Count; i++)
        {
            if (FitsLeg(route.Airports[i].Position, route.Airports[i + 1].Position, flight))
            {
                return true;
            }
        }

        return false;
    }

    private static bool FitsLeg(GeoPoint from, GeoPoint to, Flight flight)
    {
        var position = flight.Position;
        double fromKm = Haversine.DistanceKm(from, position);
        double toKm = Haversine.DistanceKm(position, to);
        double legKm = Haversine.DistanceKm(from, to);

        if (fromKm + toKm > legKm * MaxDetourFactor + DetourSlackKm)
        {
            return false; // not on the way between these two airports
        }

        // Direction only means something for an aircraft in the air, with a known track, away from both airports.
        if (flight.OnGround || flight.TrackDeg is not { } trackDeg || fromKm <= NearAirportKm || toKm <= NearAirportKm)
        {
            return true;
        }

        double toDestinationDeg = Bearing.InitialDeg(position, to);
        return Bearing.DifferenceDeg(trackDeg, toDestinationDeg) <= MaxTrackOffDeg;
    }
}
