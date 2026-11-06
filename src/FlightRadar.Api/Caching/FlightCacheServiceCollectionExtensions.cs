using FlightRadar.Core.Flights;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Options;

namespace FlightRadar.Api.Caching;

public static class FlightCacheServiceCollectionExtensions
{
    /// <summary>
    /// Registers <typeparamref name="TProvider"/>, wrapped in <see cref="CachingFlightDataProvider"/>, as the
    /// app's <see cref="IFlightDataProvider"/>. <typeparamref name="TProvider"/> must already be registered
    /// under its own type (e.g. by AddAdsbLolFlightDataProvider).
    /// </summary>
    public static IServiceCollection AddCachedFlightDataProvider<TProvider>(this IServiceCollection services)
        where TProvider : class, IFlightDataProvider
    {
        services.AddOptions<FlightCacheOptions>()
            .BindConfiguration(FlightCacheOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // In-memory only for now. Registering an IDistributedCache (e.g. Redis) later makes it a two-level
        // cache with no change to the code that uses it.
        services.AddHybridCache();

        // The decorator, wired by hand: callers ask for IFlightDataProvider and get the cache, which holds the
        // real provider. (Libraries such as Scrutor add a .Decorate<>() helper; one registration doesn't need it.)
        services.AddTransient<IFlightDataProvider>(serviceProvider => new CachingFlightDataProvider(
            serviceProvider.GetRequiredService<TProvider>(),
            serviceProvider.GetRequiredService<HybridCache>(),
            serviceProvider.GetRequiredService<IOptions<FlightCacheOptions>>(),
            serviceProvider.GetRequiredService<ILogger<CachingFlightDataProvider>>()));

        return services;
    }
}
