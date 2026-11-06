using FlightRadar.Api.Caching;
using FlightRadar.Api.Providers.AdsbLol;
using FlightRadar.Api.Providers.OpenSky;
using FlightRadar.Core.Flights;

namespace FlightRadar.Api.Providers;

/// <summary>The flight data sources the app can use. Chosen with "Providers:Active" and "Providers:Fallback".</summary>
public enum FlightDataSource
{
    AdsbLol,
    OpenSky,
}

public static class FlightDataProvidersServiceCollectionExtensions
{
    public const string ActiveSetting = "Providers:Active";
    public const string FallbackSetting = "Providers:Fallback";

    /// <summary>Value of "Providers:Fallback" that turns the fallback off.</summary>
    public const string NoFallback = "None";

    /// <summary>
    /// Registers every provider under its own type, then makes the app's IFlightDataProvider:
    /// cache → (primary, falling back to the fallback provider if it fails) → the providers.
    /// </summary>
    /// <remarks>
    /// "Providers:Active" picks the primary (default AdsbLol). "Providers:Fallback" picks the fallback; left unset it
    /// is OpenSky when the primary is adsb.lol and none otherwise, and "None" turns it off. Both are read at startup,
    /// because they decide which services exist, so a typo fails then rather than on the first request.
    /// </remarks>
    public static IServiceCollection AddFlightDataProviders(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddAdsbLolFlightDataProvider();
        services.AddOpenSkyFlightDataProvider();

        var active = ActiveSource(configuration);
        var fallback = FallbackSource(configuration, active);

        return services.AddCachedFlightDataProvider(serviceProvider =>
        {
            var primary = Resolve(serviceProvider, active);
            return fallback is not { } fallbackSource
                ? primary
                : new FallbackFlightDataProvider(
                    primary, active.ToString(),
                    Resolve(serviceProvider, fallbackSource), fallbackSource.ToString(),
                    serviceProvider.GetRequiredService<ILogger<FallbackFlightDataProvider>>());
        });
    }

    private static IFlightDataProvider Resolve(IServiceProvider serviceProvider, FlightDataSource source) => source switch
    {
        FlightDataSource.AdsbLol => serviceProvider.GetRequiredService<AdsbLolFlightDataProvider>(),
        FlightDataSource.OpenSky => serviceProvider.GetRequiredService<OpenSkyFlightDataProvider>(),
        _ => throw new InvalidOperationException($"No provider registered for {source}."),
    };

    internal static FlightDataSource ActiveSource(IConfiguration configuration) =>
        Parse(ActiveSetting, configuration[ActiveSetting]) ?? FlightDataSource.AdsbLol;

    /// <summary>The fallback provider, or null for none.</summary>
    internal static FlightDataSource? FallbackSource(IConfiguration configuration, FlightDataSource active)
    {
        var value = configuration[FallbackSetting];
        if (string.Equals(value?.Trim(), NoFallback, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var configured = Parse(FallbackSetting, value);
        if (configured is null)
        {
            // Unset: OpenSky backs up adsb.lol (anonymous OpenSky is free, and only used during adsb.lol outages).
            return active == FlightDataSource.AdsbLol ? FlightDataSource.OpenSky : null;
        }

        return configured != active
            ? configured
            : throw new InvalidOperationException(
                $"{FallbackSetting} is {configured}, the same as {ActiveSetting}. Choose another provider or '{NoFallback}'.");
    }

    /// <summary>A provider name (any case), or null if the setting is blank.</summary>
    private static FlightDataSource? Parse(string setting, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        // IsDefined as well as TryParse: TryParse also accepts numbers ("7"), which aren't valid sources.
        return Enum.TryParse<FlightDataSource>(value, ignoreCase: true, out var source) && Enum.IsDefined(source)
            ? source
            : throw new InvalidOperationException(
                $"{setting} is '{value}'; expected one of: {string.Join(", ", Enum.GetNames<FlightDataSource>())}.");
    }
}
