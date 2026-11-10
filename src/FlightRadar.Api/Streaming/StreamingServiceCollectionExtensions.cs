using FlightRadar.Api.Caching;
using FlightRadar.Api.Flights;
using FlightRadar.Api.RateLimiting;
using FlightRadar.Api.Watching;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.StackExchangeRedis;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;

namespace FlightRadar.Api.Streaming;

public static class StreamingServiceCollectionExtensions
{
    /// <summary>
    /// SignalR, the flights hub, and its subscriptions (also a watched-area source for the poller). With Redis
    /// configured, SignalR also gets the Redis <b>backplane</b>, so a push sent on one instance reaches connections on
    /// every instance.
    /// </summary>
    /// <remarks>
    /// The backplane is Redis pub/sub: each instance SUBSCRIBEs to a channel per group its connections are in
    /// (<c>FlightRadar.Api.Streaming.FlightsHub:group:area:&lt;area&gt;</c>), and a send to a group is one PUBLISH that
    /// Redis copies to every subscriber. Like the message bus between the servers of a sharded game world. It
    /// carries every send, local ones included: if Redis is down, no pushes arrive anywhere (connections stay up),
    /// which is why browsers fall back to polling when pushes stop coming.
    /// <para>
    /// Channel names start with the hub's type name, not our <c>flightradar:</c> key prefix: StackExchange.Redis
    /// applies a channel prefix per connection, and this is the shared connection, which has none. Channels aren't
    /// keys, so they can't collide with ours anyway. <c>PUBSUB CHANNELS *FlightsHub*</c> lists them.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddFlightStreaming(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);
        var signalR = services.AddSignalR();

        if (!string.IsNullOrWhiteSpace(configuration.GetConnectionString(RedisCacheServiceCollectionExtensions.ConnectionStringName)))
        {
            signalR.AddStackExchangeRedis();
            services.AddOptions<RedisOptions>().Configure<IServiceProvider>((options, serviceProvider) =>
            {
                // Our one shared, fail-safe connection, like the cache and the watched-areas registry.
                options.ConnectionFactory = _ => Task.FromResult(serviceProvider.GetRequiredService<IConnectionMultiplexer>());
            });
        }

        services.AddScoped<FlightsResponseBuilder>();
        services.AddSingleton<FlightSubscriptions>();
        services.AddSingleton<IWatchedAreaSource>(serviceProvider => serviceProvider.GetRequiredService<FlightSubscriptions>());
        services.AddScoped<IFlightUpdatePublisher, HubFlightUpdatePublisher>();
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
