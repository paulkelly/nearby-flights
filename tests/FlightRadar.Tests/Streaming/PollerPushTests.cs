using FlightRadar.Api.Watching;
using FlightRadar.Core.Geo;
using FlightRadar.Tests.Fakes;
using FlightRadar.Tests.Watching;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using static FlightRadar.Tests.Fakes.FakeFlightDataProvider;

namespace FlightRadar.Tests.Streaming;

/// <summary>The poller pushing to subscribers: within one instance, and across two through the Redis backplane.</summary>
public sealed class PollerPushTests
{
    private static readonly GeoPoint Dublin = new(53.42, -6.27);

    private static Task WatchAsync(HubConnection connection) =>
        connection.InvokeAsync("Watch", Dublin.Latitude, Dublin.Longitude, 50.0);

    [Fact]
    public async Task Tick_AreaWithASubscriber_PushesTheFreshFlightsToIt()
    {
        var provider = new FakeFlightDataProvider(FlightAt("near", 53.52, -6.27));
        await using var app = new StreamingTestApp(provider);
        var browser = await app.ConnectAsync();
        await WatchAsync(browser);
        var push = StreamingTestApp.NextPushAsync(browser);

        var result = await app.RunPollerTickAsync();

        var pushed = await push;
        Assert.Equal((1, 1), (result.Watched, result.Fetched)); // the subscription alone made it a watched area
        Assert.Equal(["near"], pushed.Flights.Select(flight => flight.Hex));
        // The snapped area's data, a little bigger than the browser's circle; the browser trims it.
        var area = AreaSnapping.Snap(Dublin, 50, double.PositiveInfinity);
        Assert.Equal((area.Centre.Latitude, area.RadiusKm), (pushed.Lat, pushed.RadiusKm));
    }

    [Fact]
    public async Task Tick_NoSubscribers_PushesNothingAnyoneReceives()
    {
        var provider = new FakeFlightDataProvider(FlightAt("near", 53.52, -6.27));
        await using var app = new StreamingTestApp(provider);
        var browser = await app.ConnectAsync();
        var push = StreamingTestApp.NextPushAsync(browser); // connected, but watching nothing

        var result = await app.RunPollerTickAsync();

        Assert.Equal(0, result.Watched);
        await Assert.ThrowsAsync<TimeoutException>(() => push.WaitAsync(TimeSpan.FromMilliseconds(300)));
    }
}

[Trait("Category", "Database")]
[Collection(RedisCollection.Name)]
public sealed class BackplaneTests(RedisFixture redis)
{
    [Fact]
    public async Task Tick_OnInstanceA_ReachesABrowserConnectedToInstanceB()
    {
        // Two copies of the app sharing one Redis, as with `docker compose up --scale api=2`.
        var provider = new FakeFlightDataProvider(FlightAt("near", 53.52, -6.27));
        await using var instanceA = new StreamingTestApp(provider, redis.ConnectionString);
        await using var instanceB = new StreamingTestApp(provider, redis.ConnectionString);

        // The browser is connected to B only.
        var browser = await instanceB.ConnectAsync();
        var centre = new GeoPoint(Random.Shared.Next(-60, 60), Random.Shared.Next(-170, 170)); // an area no other test uses
        await browser.InvokeAsync("Watch", centre.Latitude, centre.Longitude, 50.0);
        var push = StreamingTestApp.NextPushAsync(browser);

        // A learns about the area the way it would for real: from the shared registry in Redis (here marked
        // directly; in the app, B's poller would see it too, and the claim decides who fetches).
        var area = AreaSnapping.Snap(centre, 50, double.PositiveInfinity);
        await instanceA.Services.GetRequiredService<IWatchedAreaRegistry>().MarkWatchedAsync(area, default);
        var result = await instanceA.RunPollerTickAsync();

        Assert.Equal(1, result.Fetched); // A fetched it (A has no browsers of its own)...
        var pushed = await push;         // ...and B's browser got the push, through Redis pub/sub
        Assert.Equal(area.RadiusKm, pushed.RadiusKm);
    }
}
