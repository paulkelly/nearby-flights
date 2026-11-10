using FlightRadar.Api.Watching;
using FlightRadar.Core.Flights;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Options;

namespace FlightRadar.Api.Caching;

public static class FlightCacheServiceCollectionExtensions
{
    /// <summary>
    /// Registers <typeparamref name="TProvider"/>, wrapped in <see cref="CachingFlightDataProvider"/> and then
    /// <see cref="WatchingFlightDataProvider"/>, as the app's <see cref="IFlightDataProvider"/>. <typeparamref name="TProvider"/> must already be registered
    /// under its own type (e.g. by AddAdsbLolFlightDataProvider).
    /// </summary>
    public static IServiceCollection AddCachedFlightDataProvider<TProvider>(this IServiceCollection services)
        where TProvider : class, IFlightDataProvider =>
        services.AddCachedFlightDataProvider(serviceProvider => serviceProvider.GetRequiredService<TProvider>());

    /// <summary>
    /// The same, for a provider built at resolve time, e.g. a fallback combining two others.
    /// </summary>
    public static IServiceCollection AddCachedFlightDataProvider(
        this IServiceCollection services, Func<IServiceProvider, IFlightDataProvider> createInner)
    {
        services.AddOptions<FlightCacheOptions>()
            .BindConfiguration(FlightCacheOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // In-memory only for now. Registering an IDistributedCache (e.g. Redis) later makes it a two-level
        // cache with no change to the code that uses it.
        services.AddHybridCache();

        // Decorators, wired by hand: callers ask for IFlightDataProvider and get watching → cache → real provider.
        // (Libraries such as Scrutor add a .Decorate<>() helper; two registrations don't need it.)
        // The cache is also registered under its own type, for the background poller, which must refresh the cache
        // without counting as a viewer (see WatchingFlightDataProvider).
        services.AddTransient(serviceProvider => new CachingFlightDataProvider(
            createInner(serviceProvider),
            serviceProvider.GetRequiredService<HybridCache>(),
            serviceProvider.GetRequiredService<IOptions<FlightCacheOptions>>(),
            serviceProvider.GetRequiredService<ILogger<CachingFlightDataProvider>>()));
        services.AddTransient<IFlightDataProvider>(serviceProvider => new WatchingFlightDataProvider(
            serviceProvider.GetRequiredService<CachingFlightDataProvider>(),
            serviceProvider.GetRequiredService<IWatchedAreaRegistry>()));

        return services;
    }
}
