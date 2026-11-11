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
    /// applies a channel prefix per connection, and we leave the backplane's unset. Channels aren't keys, so they
    /// can't collide with ours anyway. <c>PUBSUB CHANNELS *FlightsHub*</c> lists them.
    /// </para>
    /// <para>
    /// The backplane has its own Redis connection rather than the app's shared one: SignalR disposes the connection
    /// it uses when it shuts down (unsubscribing on it first), even one lent to it through
    /// <c>RedisOptions.ConnectionFactory</c>. Sharing ours made shutdown depend on disposal order, which failed on a
    /// CI runner with ObjectDisposedException. Whoever creates a connection disposes it.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddFlightStreaming(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);
        var signalR = services.AddSignalR();

        var redis = configuration.GetConnectionString(RedisCacheServiceCollectionExtensions.ConnectionStringName);
        if (!string.IsNullOrWhiteSpace(redis))
        {
            // Its own connection (see remarks), with the same fail-safe settings as ours.
            signalR.AddStackExchangeRedis(options =>
                options.Configuration = RedisCacheServiceCollectionExtensions.CreateConfigurationOptions(redis));
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
