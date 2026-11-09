using FlightRadar.Core.Enrichment;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Options;

namespace FlightRadar.Api.Routes;

/// <summary>
/// Decorator: caches each callsign's route (HybridCache: memory, plus Redis when configured), so a route is
/// fetched once per hour per callsign, not on every poll by every user.
/// </summary>
/// <remarks>
/// "No route known" is cached too, as an empty entry, otherwise every poll would ask again for the many callsigns the
/// database doesn't have. Failures aren't cached (HybridCache never caches exceptions), so they're retried. Like the
/// flights cache, concurrent misses for one callsign share a single lookup.
/// </remarks>
public sealed partial class CachingRouteProvider(
    IRouteProvider inner,
    HybridCache cache,
    IOptions<RouteOptions> options,
    ILogger<CachingRouteProvider> logger) : IRouteProvider
{
    /// <summary>
    /// What's stored: a wrapper, so "looked it up, no route" (Route null) is a cached value in its own right,
    /// distinct from "not in the cache".
    /// </summary>
    public sealed record CachedRoute(FlightRoute? Route);

    private readonly HybridCacheEntryOptions _entryOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(options.Value.CacheMinutes),
        LocalCacheExpiration = TimeSpan.FromMinutes(options.Value.CacheMinutes),
    };

    public async Task<FlightRoute?> GetRouteAsync(string callsign, CancellationToken cancellationToken)
    {
        var cached = await cache.GetOrCreateAsync(
            CacheKey(callsign),
            (self: this, inner, callsign),
            static async (state, ct) =>
            {
                state.self.LogRouteLookup(state.callsign);
                return new CachedRoute(await state.inner.GetRouteAsync(state.callsign, ct));
            },
            _entryOptions,
            cancellationToken: cancellationToken);

        return cached.Route;
    }

    /// <summary>e.g. "route:EIN123". Visible with redis-cli: <c>KEYS route:*</c>, <c>TTL route:EIN123</c>.</summary>
    public static string CacheKey(string callsign) => $"route:{callsign}";

    // Debug: a busy area causes dozens of these when the cache is cold.
    [LoggerMessage(Level = LogLevel.Debug, Message = "Route cache miss for {Callsign}: looking it up")]
    private partial void LogRouteLookup(string callsign);
}
