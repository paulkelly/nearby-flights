using FlightRadar.Api.Flights;
using FlightRadar.Api.Streaming;
using FlightRadar.Core.Enrichment;
using FlightRadar.Core.Flights;
using FlightRadar.Core.Geo;
using FlightRadar.Tests.Fakes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using static FlightRadar.Tests.Fakes.FakeFlightDataProvider;

namespace FlightRadar.Tests.Streaming;

/// <summary>
/// Real SignalR clients over real (in-memory) WebSockets to the whole app, with a fake flight provider and clock.
/// </summary>
public sealed class FlightsHubTests : IAsyncLifetime
{
    private static readonly GeoPoint Dublin = new(53.42, -6.27);
    private static readonly GeoPoint London = new(51.47, -0.45);

    private readonly FakeTimeProvider clock = new(new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero));
    private readonly WebApplicationFactory<Program> app;
    private readonly List<HubConnection> connections = [];

    public FlightsHubTests()
    {
        var provider = new FakeFlightDataProvider(FlightAt("near", 53.52, -6.27), FlightAt("far", 54.5, -6.27));
        app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Redis", "");
            builder.UseSetting("ConnectionStrings:Postgres", "");
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IFlightDataProvider>(provider);
                services.AddSingleton<IRouteProvider>(new FakeRouteProvider());
                services.AddSingleton<TimeProvider>(clock);
            });
        });
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var connection in connections)
        {
            await connection.DisposeAsync();
        }
        await app.DisposeAsync();
    }

    /// <summary>Connects like the browser will: WebSockets only, no negotiate request.</summary>
    private async Task<HubConnection> ConnectAsync()
    {
        var server = app.Server;
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
        connections.Add(connection);
        return connection;
    }

    private static Task<FlightsResponse> WatchAsync(HubConnection connection, GeoPoint centre, double radiusKm) =>
        connection.InvokeAsync<FlightsResponse>("Watch", centre.Latitude, centre.Longitude, radiusKm);

    private FlightSubscriptions Subscriptions => app.Services.GetRequiredService<FlightSubscriptions>();

    private async Task<IReadOnlyCollection<SearchArea>> WatchedAsync() =>
        await Subscriptions.GetWatchedAreasAsync(default);

    [Fact]
    public async Task Watch_ValidArea_ReturnsTheFlightsInItNow()
    {
        var connection = await ConnectAsync();

        var response = await WatchAsync(connection, Dublin, 50);

        // The same shape and trimming as GET /api/flights: "far" (~120 km) is outside the circle.
        Assert.Equal((53.42, -6.27, 50.0), (response.Lat, response.Lon, response.RadiusKm));
        Assert.Equal(["near"], response.Flights.Select(flight => flight.Hex));
    }

    [Fact]
    public async Task Watch_ValidArea_IsAWatchedAreaForThePoller()
    {
        var connection = await ConnectAsync();

        await WatchAsync(connection, Dublin, 50);

        Assert.Equal([AreaSnapping.Snap(Dublin, 50, double.PositiveInfinity)], await WatchedAsync());
    }

    [Fact]
    public async Task Watch_InvalidArea_FailsWithTheReason()
    {
        var connection = await ConnectAsync();

        var error = await Assert.ThrowsAsync<HubException>(() => WatchAsync(connection, new GeoPoint(91, 0), 50));

        Assert.Contains("lat must be between -90 and 90.", error.Message);
        Assert.Empty(await WatchedAsync());
    }

    [Fact]
    public async Task Watch_ThenPushToItsGroup_IsReceived()
    {
        var connection = await ConnectAsync();
        var received = new TaskCompletionSource<FlightsResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<FlightsResponse>(FlightsHub.FlightsUpdatedMethod, response => received.TrySetResult(response));
        await WatchAsync(connection, Dublin, 50);

        // What the poller will do (piece 2): push to the area's group through the hub context.
        var area = AreaSnapping.Snap(Dublin, 50, double.PositiveInfinity);
        var pushed = new FlightsResponse(1, 2, 3, 0, clock.GetUtcNow(), []);
        await app.Services.GetRequiredService<IHubContext<FlightsHub>>()
            .Clients.Group(FlightsHub.GroupFor(area)).SendAsync(FlightsHub.FlightsUpdatedMethod, pushed);

        Assert.Equal(pushed.RadiusKm, (await received.Task.WaitAsync(TimeSpan.FromSeconds(5))).RadiusKm);
    }

    [Fact]
    public async Task Watch_AnotherArea_SwitchesGroup()
    {
        var connection = await ConnectAsync();
        var received = new List<double>();
        var londonReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<FlightsResponse>(FlightsHub.FlightsUpdatedMethod, response =>
        {
            lock (received) received.Add(response.Lat);
            if (response.Lat == 2)
            {
                londonReceived.TrySetResult();
            }
        });
        await WatchAsync(connection, Dublin, 50);
        await WatchAsync(connection, London, 50);

        var hub = app.Services.GetRequiredService<IHubContext<FlightsHub>>();
        await hub.Clients.Group(FlightsHub.GroupFor(AreaSnapping.Snap(Dublin, 50, double.PositiveInfinity)))
            .SendAsync(FlightsHub.FlightsUpdatedMethod, new FlightsResponse(1, 0, 0, 0, clock.GetUtcNow(), []));
        await hub.Clients.Group(FlightsHub.GroupFor(AreaSnapping.Snap(London, 50, double.PositiveInfinity)))
            .SendAsync(FlightsHub.FlightsUpdatedMethod, new FlightsResponse(2, 0, 0, 0, clock.GetUtcNow(), []));

        // Wait for London's push itself. (Not for the reply to some later call: the client hands pushes to a separate
        // handler loop, so a reply can be seen before an earlier push's handler has run. That raced on CI.)
        // Handlers run one at a time in arrival order, so if Dublin's push, sent first, had come, it would be in the
        // list already.
        await londonReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));

        lock (received) Assert.Equal([2.0], received); // only London's: Dublin's group was left
    }

    [Fact]
    public async Task Unwatch_StopsBeingAWatchedArea()
    {
        var connection = await ConnectAsync();
        await WatchAsync(connection, Dublin, 50);

        await connection.InvokeAsync("Unwatch");

        Assert.Empty(await WatchedAsync());
    }

    [Fact]
    public async Task Disconnect_StopsBeingAWatchedArea()
    {
        var connection = await ConnectAsync();
        await WatchAsync(connection, Dublin, 50);

        await connection.StopAsync();

        // The server notices the close asynchronously; give it up to 5 s (CI runners are slower than a dev machine).
        for (int i = 0; i < 250 && (await WatchedAsync()).Count > 0; i++)
        {
            await Task.Delay(20);
        }
        Assert.Empty(await WatchedAsync());
    }

    [Fact]
    public async Task Watch_TooManyChangesAtOnce_IsRefusedUntilTheThrottleRefills()
    {
        var connection = await ConnectAsync();
        for (int i = 0; i < WatchThrottle.Burst; i++)
        {
            await WatchAsync(connection, new GeoPoint(50 + i, 0), 10);
        }

        var error = await Assert.ThrowsAsync<HubException>(() => WatchAsync(connection, Dublin, 10));
        Assert.Contains("Too many area changes", error.Message);

        clock.Advance(WatchThrottle.RefillEvery);
        await WatchAsync(connection, Dublin, 10); // allowed again
    }

    [Fact]
    public async Task Watch_ThrottleIsPerConnection()
    {
        var busy = await ConnectAsync();
        for (int i = 0; i < WatchThrottle.Burst; i++)
        {
            await WatchAsync(busy, new GeoPoint(50 + i, 0), 10);
        }

        var other = await ConnectAsync();
        await WatchAsync(other, Dublin, 10); // unaffected by the other connection's changes
    }
}
