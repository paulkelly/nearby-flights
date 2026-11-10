using FlightRadar.Api.Caching;
using FlightRadar.Api.Flights;
using FlightRadar.Api.Streaming;
using FlightRadar.Api.Watching;
using FlightRadar.Core.Enrichment;
using FlightRadar.Core.Flights;
using FlightRadar.Tests.Fakes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FlightRadar.Tests.Streaming;

/// <summary>
/// The whole app for streaming tests: a fake flight provider everywhere (requests <em>and</em> the poller's cache),
/// fake routes, optionally a fake clock and a real Redis. Plus helpers to connect SignalR clients and run one poller
/// tick on demand.
/// </summary>
public sealed class StreamingTestApp : IAsyncDisposable
{
    private readonly List<HubConnection> _connections = [];

    public StreamingTestApp(FakeFlightDataProvider provider, string redis = "", TimeProvider? clock = null)
    {
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Redis", redis);
            builder.UseSetting("ConnectionStrings:Postgres", "");
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IFlightDataProvider>(provider);
                services.AddSingleton<IRouteProvider>(new FakeRouteProvider());
                // The poller refreshes through the cache decorator, which normally wraps the real adsb.lol client.
                services.AddTransient(serviceProvider => new CachingFlightDataProvider(
                    provider,
                    serviceProvider.GetRequiredService<HybridCache>(),
                    serviceProvider.GetRequiredService<IOptions<FlightCacheOptions>>(),
                    NullLogger<CachingFlightDataProvider>.Instance));
                if (clock is not null)
                {
                    services.AddSingleton(clock);
                }
            });
        });
    }

    public WebApplicationFactory<Program> Factory { get; }

    public IServiceProvider Services => Factory.Services;

    /// <summary>Connects like the browser will: WebSockets only, no negotiate request.</summary>
    public async Task<HubConnection> ConnectAsync()
    {
        var server = Factory.Server;
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress, FlightsHub.Path.TrimStart('/')), options =>
            {
                options.Transports = HttpTransportType.WebSockets;
                options.SkipNegotiation = true;
                options.WebSocketFactory = (context, cancellationToken) =>
                    new ValueTask<System.Net.WebSockets.WebSocket>(
                        server.CreateWebSocketClient().ConnectAsync(context.Uri, cancellationToken));
            })
            .Build();
        await connection.StartAsync();
        _connections.Add(connection);
        return connection;
    }

    /// <summary>
    /// One poller tick, now. Built directly (the test assembly turns the background poller off, so PollerTick isn't
    /// registered) from the app's own services.
    /// </summary>
    public async Task<PollerTickResult> RunPollerTickAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        return await ActivatorUtilities.CreateInstance<PollerTick>(scope.ServiceProvider).RunAsync(default);
    }

    /// <summary>A task that completes with the next push this connection receives.</summary>
    public static Task<FlightsResponse> NextPushAsync(HubConnection connection)
    {
        var received = new TaskCompletionSource<FlightsResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        IDisposable? subscription = null;
        subscription = connection.On<FlightsResponse>(FlightsHub.FlightsUpdatedMethod, response =>
        {
            received.TrySetResult(response);
            subscription?.Dispose();
        });
        return received.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var connection in _connections)
        {
            await connection.DisposeAsync();
        }
        await Factory.DisposeAsync();
    }
}
