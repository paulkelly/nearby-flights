using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FlightRadar.Api.Caching;

/// <summary>
/// Can the API reach its Redis cache? Registered only when Redis is configured, and reported as Degraded (not
/// Unhealthy) when it can't: the API still works without Redis, just with less caching, so it shouldn't be taken
/// out of service or restarted because of it.
/// </summary>
public sealed class RedisHealthCheck(IDistributedCache cache) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            // A read of a key that never exists: a full round trip to Redis without writing anything. Bounded by
            // the client's own 1 s timeouts (RedisCacheServiceCollectionExtensions).
            await cache.GetAsync("health-probe", cancellationToken);
            return HealthCheckResult.Healthy("Redis is reachable.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // context.Registration.FailureStatus is Degraded, as registered. The health system logs a Warning for each
            // degraded check, i.e. every probe (10 s) while Redis is down; the reason goes in the message, not as an
            // exception, so that isn't a full stack trace each time (HybridCache already logs one per failed request).
            return new HealthCheckResult(
                context.Registration.FailureStatus,
                $"Redis is unreachable; serving from the in-memory cache only. ({ex.GetType().Name}: {ex.Message})");
        }
    }
}
