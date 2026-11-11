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

        // Our own connection, for our code that talks to Redis directly: the watched-areas registry and the poll
        // claims (step 7a), which need commands IDistributedCache doesn't offer (sorted sets, SET NX). A
        // ConnectionMultiplexer is designed to be shared and long-lived: one per app, like an HttpClient handler,
        // not one per use. The DI container owns it and disposes it at shutdown.
        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(CreateConfigurationOptions(connectionString)));

        // The cache gets a connection of its own, with the same fail-safe settings. RedisCache closes and disposes
        // whatever connection it uses when it's disposed, even one lent to it through ConnectionMultiplexerFactory,
        // so sharing ours meant the cache closed it under everyone else at shutdown. (SignalR's backplane does the
        // same and has its own too: see AddFlightStreaming.) Whoever creates a connection disposes it.
        services.AddStackExchangeRedisCache(options =>
        {
            options.InstanceName = KeyPrefix;
            options.ConfigurationOptions = CreateConfigurationOptions(connectionString);
        });

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
