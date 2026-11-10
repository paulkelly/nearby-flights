using FlightRadar.Api.Health;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using StackExchange.Redis;

namespace FlightRadar.Api.Caching;

public static class RedisCacheServiceCollectionExtensions
{
    public const string ConnectionStringName = "Redis";

    /// <summary>Every key this app writes starts with this, so its keys are easy to find and can't collide.</summary>
    public const string KeyPrefix = "flightradar:";

    /// <summary>
    /// If "ConnectionStrings:Redis" is set, registers Redis as the app's <c>IDistributedCache</c>, which
    /// HybridCache then uses automatically as its second level (L2) behind the in-memory one (L1).
    /// If it isn't set, nothing is registered and the app runs on the in-memory cache alone, so Redis is never
    /// required (local runs and free deployments work without it).
    /// </summary>
    public static IServiceCollection AddOptionalRedisCache(
        this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return services;
        }

        var redisOptions = CreateConfigurationOptions(connectionString);

        // One connection for everything that talks to Redis: the cache, and the watched-areas registry (step 7a),
        // which needs commands IDistributedCache doesn't offer (sorted sets). A ConnectionMultiplexer is designed to
        // be shared and long-lived: one per app, like an HttpClient handler, not one per use.
        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisOptions));

        services.AddStackExchangeRedisCache(options => options.InstanceName = KeyPrefix);
        services.AddOptions<RedisCacheOptions>()
            .Configure<IServiceProvider>((options, serviceProvider) =>
                options.ConnectionMultiplexerFactory =
                    () => Task.FromResult(serviceProvider.GetRequiredService<IConnectionMultiplexer>()));

        // Part of /health/ready ("ready" tag). Degraded on failure: see RedisHealthCheck.
        services.AddHealthChecks()
            .AddCheck<RedisHealthCheck>("redis", failureStatus: HealthStatus.Degraded, tags: [HealthTags.Ready]);

        return services;
    }

    /// <summary>Client settings for our Redis: parsed from the connection string, then made fail-safe.</summary>
    public static ConfigurationOptions CreateConfigurationOptions(string connectionString)
    {
        var redisOptions = ConfigurationOptions.Parse(connectionString);

        // A cache must never take the app down with it. If Redis is missing or dies:
        // - AbortOnConnectFail = false: start anyway and keep reconnecting in the background, instead of throwing.
        // - FailFast: while disconnected, commands fail immediately. The default queues them until reconnect or
        //   timeout (5 s), which would make every request wait 5 s for a cache that isn't there.
        // - Short timeouts, one connect attempt: a slow cache is worse than no cache; the provider is only
        //   ~100-200 ms away. The first request waits for the first connect, and with the defaults (3 attempts
        //   × up to 5 s) that request took 6.4 s when Redis was down at startup.
        redisOptions.AbortOnConnectFail = false;
        redisOptions.BacklogPolicy = BacklogPolicy.FailFast;
        redisOptions.ConnectRetry = 1;
        redisOptions.ConnectTimeout = 1_000;
        redisOptions.SyncTimeout = 1_000;
        redisOptions.AsyncTimeout = 1_000;
        return redisOptions;
    }
}
