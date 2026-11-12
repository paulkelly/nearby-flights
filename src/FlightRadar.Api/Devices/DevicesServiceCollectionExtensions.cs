using FlightRadar.Api.Data;
using FlightRadar.Api.Pushes;
using FlightRadar.Api.Watching;

namespace FlightRadar.Api.Devices;

public static class DevicesServiceCollectionExtensions
{
    /// <summary>
    /// Adds widget locations as a watched-area source for the poller (step 9c), when there's a database to read them
    /// from. Without one there are no devices, and the poller watches live viewers only, as before.
    /// </summary>
    public static IServiceCollection AddWidgetWatching(this IServiceCollection services, IConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration.GetConnectionString(DatabaseServiceCollectionExtensions.ConnectionStringName)))
        {
            return services;
        }

        services.AddSingleton<WidgetWatchSource>(); // singleton: it remembers when it last handed each area out
        services.AddSingleton<IWatchedAreaSource>(serviceProvider => serviceProvider.GetRequiredService<WidgetWatchSource>());

        // Step 9d: after each refresh, push to the widgets' devices when their aircraft changed. Scoped like the
        // SignalR publisher (it uses the scoped response builder and DbContext); its memory lives in the singleton.
        services.AddSingleton<WidgetNearestState>();
        services.AddScoped<IFlightUpdatePublisher, WidgetPushPublisher>();
        return services;
    }
}
