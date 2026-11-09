using FlightRadar.Core.Enrichment;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

namespace FlightRadar.Api.Routes;

public static class RoutesServiceCollectionExtensions
{
    /// <summary>
    /// Registers route lookups: <see cref="RouteLookup"/> → <see cref="CachingRouteProvider"/> (as
    /// <see cref="IRouteProvider"/>) → <see cref="VrsRouteProvider"/>, configured from "Routes".
    /// </summary>
    public static IServiceCollection AddRouteLookup(this IServiceCollection services)
    {
        services.AddOptions<RouteOptions>()
            .BindConfiguration(RouteOptions.SectionName)
            .ValidateDataAnnotations()
            .Validate(
                options => options.AttemptTimeoutSeconds < options.TotalTimeoutSeconds,
                "AttemptTimeoutSeconds must be less than TotalTimeoutSeconds.")
            .ValidateOnStart();

        services.AddHttpClient<VrsRouteProvider>((serviceProvider, client) =>
            {
                var options = serviceProvider.GetRequiredService<IOptions<RouteOptions>>().Value;
                client.BaseAddress = options.BaseUrl;
                client.DefaultRequestHeaders.UserAgent.ParseAdd("FlightRadar/0.1");
                client.Timeout = Timeout.InfiniteTimeSpan; // the resilience pipeline owns timeouts (see adsb.lol's)
            })
            // The same pipeline as the flight providers. A 404 ("no route") isn't a failure to the pipeline: only
            // 5xx, 408, 429 and network errors are retried or count towards opening the circuit.
            .AddStandardResilienceHandler()
            .Configure((HttpStandardResilienceOptions resilience, IServiceProvider serviceProvider) =>
            {
                var options = serviceProvider.GetRequiredService<IOptions<RouteOptions>>().Value;

                resilience.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(options.TotalTimeoutSeconds);
                resilience.AttemptTimeout.Timeout = TimeSpan.FromSeconds(options.AttemptTimeoutSeconds);
                resilience.Retry.MaxRetryAttempts = options.MaxRetries;
                resilience.Retry.Delay = TimeSpan.FromMilliseconds(200);

                // When the source is down, stop asking for a while: lookups then fail instantly (no route shown)
                // instead of each waiting out its timeout.
                resilience.CircuitBreaker.FailureRatio = 0.5;
                resilience.CircuitBreaker.MinimumThroughput = 5;
                resilience.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
                resilience.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(30);

                resilience.RateLimiter.DefaultRateLimiterOptions.PermitLimit = options.MaxConcurrentRequests;
                resilience.RateLimiter.DefaultRateLimiterOptions.QueueLimit = options.MaxQueuedRequests;
            });

        services.AddHybridCache(); // already added by the flights cache; repeated calls are harmless
        services.TryAddSingleton(TimeProvider.System);

        services.AddTransient<IRouteProvider>(serviceProvider => new CachingRouteProvider(
            serviceProvider.GetRequiredService<VrsRouteProvider>(),
            serviceProvider.GetRequiredService<HybridCache>(),
            serviceProvider.GetRequiredService<IOptions<RouteOptions>>(),
            serviceProvider.GetRequiredService<ILogger<CachingRouteProvider>>()));

        services.AddTransient<RouteLookup>();

        return services;
    }
}
