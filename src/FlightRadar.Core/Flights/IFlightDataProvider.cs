using FlightRadar.Core.Geo;

namespace FlightRadar.Core.Flights;

/// <summary>A source of live aircraft positions, e.g. adsb.lol or OpenSky.</summary>
public interface IFlightDataProvider
{
    /// <summary>
    /// The largest radius this provider can serve in one request, in km. Each provider states its own (from its
    /// own units), so callers such as the cache never hard-code one. <see cref="double.PositiveInfinity"/> if none.
    /// </summary>
    double MaxRadiusKm { get; }

    /// <summary>
    /// Gets aircraft around <paramref name="centre"/>. Only aircraft with a known position are returned.
    /// </summary>
    /// <remarks>
    /// Providers query in their own units and shapes (nautical miles, bounding boxes), so the result
    /// may include aircraft slightly outside <paramref name="radiusKm"/>. Callers filter by exact distance.
    /// </remarks>
    /// <exception cref="FlightDataUnavailableException">The upstream source failed or timed out.</exception>
    Task<IReadOnlyList<Flight>> GetFlightsAsync(GeoPoint centre, double radiusKm, CancellationToken cancellationToken);
}
