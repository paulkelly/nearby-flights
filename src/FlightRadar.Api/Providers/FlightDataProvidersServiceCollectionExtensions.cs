using FlightRadar.Api.Caching;
using FlightRadar.Api.Providers.AdsbLol;
using FlightRadar.Api.Providers.OpenSky;

namespace FlightRadar.Api.Providers;

/// <summary>The flight data sources the app can use. Chosen with "Providers:Active".</summary>
public enum FlightDataSource
{
    AdsbLol,
    OpenSky,
}

public static class FlightDataProvidersServiceCollectionExtensions
{
    public const string ActiveSetting = "Providers:Active";

    /// <summary>
    /// Registers every provider under its own type, then makes the one named by "Providers:Active" (default
    /// AdsbLol) the app's IFlightDataProvider, behind the cache. Registering a provider costs nothing until it's
    /// called, and having both available is what the fallback (stage 5f piece 4) builds on.
    /// </summary>
    public static IServiceCollection AddFlightDataProviders(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddAdsbLolFlightDataProvider();
        services.AddOpenSkyFlightDataProvider();

        return ActiveSource(configuration) switch
        {
            FlightDataSource.AdsbLol => services.AddCachedFlightDataProvider<AdsbLolFlightDataProvider>(),
            FlightDataSource.OpenSky => services.AddCachedFlightDataProvider<OpenSkyFlightDataProvider>(),
            var other => throw new InvalidOperationException($"No registration for {other}."),
        };
    }

    /// <summary>Read at startup (it decides which services exist), so a typo fails then, not on the first request.</summary>
    internal static FlightDataSource ActiveSource(IConfiguration configuration)
    {
        var value = configuration[ActiveSetting];
        if (string.IsNullOrWhiteSpace(value))
        {
            return FlightDataSource.AdsbLol;
        }

        // IsDefined as well as TryParse: TryParse also accepts numbers ("7"), which aren't valid sources.
        return Enum.TryParse<FlightDataSource>(value, ignoreCase: true, out var source) && Enum.IsDefined(source)
            ? source
            : throw new InvalidOperationException(
                $"{ActiveSetting} is '{value}'; expected one of: {string.Join(", ", Enum.GetNames<FlightDataSource>())}.");
    }
}
