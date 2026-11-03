using FlightRadar.Core.Geo;

namespace FlightRadar.Core.Flights;

/// <summary>A flight plus its great-circle distance from the search centre.</summary>
public sealed record NearbyFlight(Flight Flight, double DistanceKm);

/// <summary>
/// Finds aircraft inside a circle. Providers only promise "roughly this area", so this applies the exact
/// radius in one place, whichever provider is configured.
/// </summary>
public sealed class NearbyFlightsService(IFlightDataProvider provider)
{
    /// <summary>Aircraft within <paramref name="radiusKm"/> of <paramref name="centre"/>, nearest first.</summary>
    public async Task<IReadOnlyList<NearbyFlight>> GetNearbyFlightsAsync(
        GeoPoint centre, double radiusKm, CancellationToken cancellationToken)
    {
        var flights = await provider.GetFlightsAsync(centre, radiusKm, cancellationToken);

        return flights
            .Select(flight => new NearbyFlight(flight, Haversine.DistanceKm(centre, flight.Position)))
            .Where(nearby => nearby.DistanceKm <= radiusKm)
            .OrderBy(nearby => nearby.DistanceKm)
            .ToList();
    }
}
