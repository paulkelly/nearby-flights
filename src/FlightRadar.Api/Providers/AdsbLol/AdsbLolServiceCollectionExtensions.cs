using FlightRadar.Core.Flights;
using Microsoft.Extensions.Options;

namespace FlightRadar.Api.Providers.AdsbLol;

public static class AdsbLolServiceCollectionExtensions
{
    /// <summary>Registers adsb.lol as the <see cref="IFlightDataProvider"/>, configured from "Providers:AdsbLol".</summary>
    public static IServiceCollection AddAdsbLolFlightDataProvider(this IServiceCollection services)
    {
        services.AddOptions<AdsbLolOptions>()
            .BindConfiguration(AdsbLolOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart(); // bad config fails at startup, not on the first request

        // Typed client: IHttpClientFactory creates the HttpClient and pools the underlying connections.
        services.AddHttpClient<IFlightDataProvider, AdsbLolFlightDataProvider>((serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<AdsbLolOptions>>().Value;
            client.BaseAddress = options.BaseUrl;
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("FlightRadar/0.1");
        });

        return services;
    }
}
