using System.Globalization;
using FlightRadar.Core.Flights;
using FlightRadar.Core.Geo;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Options;

namespace FlightRadar.Api.Caching;

/// <summary>
/// Decorator: wraps any other <see cref="IFlightDataProvider"/> and reuses its answer for the same area for a few
/// seconds. Neither the wrapped provider nor its callers know the cache exists.
/// </summary>
/// <remarks>
/// <see cref="HybridCache"/> also gives stampede protection: if 10 requests for the same key arrive while it is
/// empty, the wrapped provider is called once and all 10 await that one call. Failures are never cached; the
/// exception goes to everyone waiting and the next request tries again.
/// </remarks>
public sealed class CachingFlightDataProvider(
    IFlightDataProvider inner, HybridCache cache, IOptions<FlightCacheOptions> options) : IFlightDataProvider
{
    private readonly HybridCacheEntryOptions _entryOptions = new()
    {
        // Expiration covers a distributed (L2) cache such as Redis, once there is one; LocalCacheExpiration is
        // this process's in-memory (L1) copy. Same value for now.
        Expiration = TimeSpan.FromSeconds(options.Value.TtlSeconds),
        LocalCacheExpiration = TimeSpan.FromSeconds(options.Value.TtlSeconds),
    };

    public async Task<IReadOnlyList<Flight>> GetFlightsAsync(
        GeoPoint centre, double radiusKm, CancellationToken cancellationToken)
    {
        // The factory only runs on a miss. It's `static` and receives everything through the state tuple, so it
        // can't accidentally capture variables, and no closure is allocated on hits.
        // Its CancellationToken is HybridCache's own: the shared call is only cancelled if *every* waiting caller
        // gives up, so one closed browser tab can't fail the request for the others.
        return await cache.GetOrCreateAsync(
            CacheKey(centre, radiusKm),
            (inner, centre, radiusKm),
            static async (state, ct) => await state.inner.GetFlightsAsync(state.centre, state.radiusKm, ct),
            _entryOptions,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// The exact request, e.g. "flights:53.42:-6.27:50". Exact matching means nearby-but-different centres miss;
    /// snapping to a grid so neighbours share entries is a later step (5c).
    /// </summary>
    public static string CacheKey(GeoPoint centre, double radiusKm) =>
        string.Create(CultureInfo.InvariantCulture, $"flights:{centre.Latitude:R}:{centre.Longitude:R}:{radiusKm:R}");
}
