using FlightRadar.Core.Flights;
using FlightRadar.Core.Geo;

namespace FlightRadar.Api.Watching;

/// <summary>
/// The outermost flight data decorator: notes that someone is looking at this area, then passes the request on
/// (to the cache, then the providers) unchanged. The area is snapped exactly as the cache snaps it, so the poller
/// later refreshes precisely the cache entry these requests read.
/// </summary>
/// <remarks>
/// Only user requests come through here. The poller calls the cache directly, so polling an area never counts as
/// watching it: otherwise every area would keep itself alive forever.
/// </remarks>
public sealed class WatchingFlightDataProvider(IFlightDataProvider inner, IWatchedAreaRegistry watchedAreas)
    : IFlightDataProvider
{
    public double MaxRadiusKm => inner.MaxRadiusKm;

    public async Task<IReadOnlyList<Flight>> GetFlightsAsync(
        GeoPoint centre, double radiusKm, CancellationToken cancellationToken)
    {
        await watchedAreas.MarkWatchedAsync(AreaSnapping.Snap(centre, radiusKm, inner.MaxRadiusKm), cancellationToken);
        return await inner.GetFlightsAsync(centre, radiusKm, cancellationToken);
    }
}
