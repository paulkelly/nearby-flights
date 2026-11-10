using FlightRadar.Api.Caching;
using FlightRadar.Api.Health;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace FlightRadar.Api.Watching;

public static class WatchingServiceCollectionExtensions
{
    /// <summary>
    /// The live-viewer registry: in Redis (shared by all instances) when ConnectionStrings:Redis is set, otherwise in
    /// memory. Registered both as <see cref="IWatchedAreaRegistry"/> (requests mark areas) and as one of the
    /// <see cref="IWatchedAreaSource"/>s the poller reads.
    /// </summary>
    public static IServiceCollection AddWatchedAreas(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<WatchingOptions>()
            .BindConfiguration(WatchingOptions.SectionName)
            .ValidateDataAnnotations()
            .Validate(
                options => options.ClaimSeconds < options.IntervalSeconds,
                "Poller:ClaimSeconds must be less than Poller:IntervalSeconds, or areas couldn't be claimed every tick.")
            .ValidateOnStart();
        services.TryAddSingleton(TimeProvider.System);

        bool redisConfigured = !string.IsNullOrWhiteSpace(
            configuration.GetConnectionString(RedisCacheServiceCollectionExtensions.ConnectionStringName));
        if (redisConfigured)
        {
            services.AddSingleton<IPollClaims, RedisPollClaims>();
            services.AddSingleton<IWatchedAreaRegistry>(serviceProvider => new RedisWatchedAreaRegistry(
                serviceProvider.GetRequiredService<IConnectionMultiplexer>(),
                serviceProvider.GetRequiredService<IOptions<WatchingOptions>>(),
                serviceProvider.GetRequiredService<TimeProvider>(),
                serviceProvider.GetRequiredService<ILogger<RedisWatchedAreaRegistry>>()));
        }
        else
        {
            services.AddSingleton<IPollClaims, SingleInstancePollClaims>();
            services.AddSingleton<IWatchedAreaRegistry, InMemoryWatchedAreaRegistry>();
        }

        // The same instance as a source. More sources (step 9: widget subscriptions) are added the same way.
        services.AddSingleton<IWatchedAreaSource>(serviceProvider =>
            serviceProvider.GetRequiredService<IWatchedAreaRegistry>());

        // The poller itself. Off (Poller:Enabled false): requests fetch on demand through the cache, as before 7a.
        if (configuration.GetValue(WatchingOptions.SectionName + ":" + nameof(WatchingOptions.Enabled), defaultValue: true))
        {
            services.AddTransient<PollerTick>();
            services.AddSingleton<PollerStatus>();
            services.AddHostedService<FlightPoller>();
            services.AddHealthChecks()
                .AddCheck<PollerHealthCheck>("poller", failureStatus: HealthStatus.Degraded, tags: [HealthTags.Ready]);
        }

        return services;
    }
}
