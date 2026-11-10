using FlightRadar.Api.Flights;
using FlightRadar.Api.RateLimiting;
using FlightRadar.Api.Watching;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FlightRadar.Api.Streaming;

public static class StreamingServiceCollectionExtensions
{
    /// <summary>SignalR, the flights hub, and its subscriptions (also a watched-area source for the poller).</summary>
    public static IServiceCollection AddFlightStreaming(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSignalR();
        services.AddScoped<FlightsResponseBuilder>();
        services.AddSingleton<FlightSubscriptions>();
        services.AddSingleton<IWatchedAreaSource>(serviceProvider => serviceProvider.GetRequiredService<FlightSubscriptions>());
        return services;
    }

    public static IEndpointRouteBuilder MapFlightStreaming(this IEndpointRouteBuilder app)
    {
        app.MapHub<FlightsHub>(FlightsHub.Path, options =>
            {
                // WebSockets only, so clients skip SignalR's "negotiate" request and connect in one go. With two API
                // instances behind nginx's round-robin, negotiate and connect could reach different instances and
                // fail; one request can't. (The other transports, Server-Sent Events and long polling, need several
                // requests to land on the same instance: "sticky sessions".) Browsers without a working WebSocket
                // fall back to polling GET /api/flights, which the frontend does anyway.
                options.Transports = HttpTransportType.WebSockets;
            })
            // Opening a connection counts against the client's HTTP rate limit, like a request; messages over it
            // don't (the limiter never sees them), which is why Watch has its own throttle.
            .RequireRateLimiting(RateLimitingServiceCollectionExtensions.PerClientPolicy);

        return app;
    }
}
