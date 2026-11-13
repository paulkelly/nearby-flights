using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FlightRadar.Api.Caching;
using FlightRadar.Api.Devices;
using FlightRadar.Api.Locations;
using FlightRadar.Api.Owners;
using FlightRadar.Api.Pushes;
using FlightRadar.Api.Watching;
using FlightRadar.Core.Enrichment;
using FlightRadar.Core.Flights;
using FlightRadar.Core.Geo;
using FlightRadar.Tests.Data;
using FlightRadar.Tests.Fakes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using static FlightRadar.Tests.Fakes.FakeFlightDataProvider;

namespace FlightRadar.Tests.Pushes;

/// <summary>
/// The whole path, on a real database: a device registers with a push address, the poller ticks (a minute apart, so
/// the widget area is due each time) over flights the test changes, and a fake sender records what would go to FCM.
/// </summary>
[Trait("Category", "Database")]
[Collection(PostgresCollection.Name)]
public sealed class WidgetPushPublisherTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly SavedLocationRequest Home = new("Home", 53.421, -6.270, 10);

    /// <summary>Flights the test sets between ticks.</summary>
    private sealed class ChangingProvider : IFlightDataProvider
    {
        public IReadOnlyList<Flight> Flights { get; set; } = [];
        public double MaxRadiusKm => double.PositiveInfinity;

        public Task<IReadOnlyList<Flight>> GetFlightsAsync(GeoPoint centre, double radiusKm, CancellationToken cancellationToken) =>
            Task.FromResult(Flights);
    }

    /// <summary>Records every message; answers <see cref="Outcome"/> for each.</summary>
    private sealed class RecordingSender : IPushSender
    {
        public List<PushMessage> Sent { get; } = [];
        public PushOutcome Outcome { get; set; } = PushOutcome.Sent;

        public Task<IReadOnlyList<PushOutcome>> SendAsync(IReadOnlyList<PushMessage> messages, CancellationToken cancellationToken)
        {
            Sent.AddRange(messages);
            return Task.FromResult<IReadOnlyList<PushOutcome>>(messages.Select(_ => Outcome).ToList());
        }
    }

    private readonly FakeTimeProvider clock = new(new DateTimeOffset(2026, 11, 12, 12, 0, 0, TimeSpan.Zero));
    private readonly ChangingProvider provider = new();
    private readonly RecordingSender sender = new();
    private WebApplicationFactory<Program> app = null!;
    private string connectionString = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Redis", "");
            builder.UseSetting("ConnectionStrings:Postgres", connectionString);
            builder.UseSetting("RateLimiting:TokenLimit", "1000");
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<TimeProvider>(clock);
                services.AddSingleton<IFlightDataProvider>(provider);
                services.AddSingleton<IRouteProvider>(new FakeRouteProvider());
                services.AddSingleton<IPushSender>(sender);
                services.AddTransient(serviceProvider => new CachingFlightDataProvider(
                    provider,
                    serviceProvider.GetRequiredService<HybridCache>(),
                    serviceProvider.GetRequiredService<IOptions<FlightCacheOptions>>(),
                    NullLogger<CachingFlightDataProvider>.Instance));
            });
        });
    }

    public async Task DisposeAsync() => await app.DisposeAsync();

    /// <summary>A new owner with Home, registered from one device with the given push address.</summary>
    private async Task<(Guid Device, Guid Location)> RegisterHomeAsync(string? pushToken)
    {
        var client = app.CreateClient();
        var owner = await client.PostAsync("/api/owners", content: null);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", (await owner.Content.ReadFromJsonAsync<NewOwnerResponse>())!.Token);
        var saved = await client.PostAsJsonAsync("/api/locations", Home);
        var location = (await saved.Content.ReadFromJsonAsync<SavedLocationDto>())!.Id;
        var device = Guid.NewGuid();
        var registered = await client.PutAsJsonAsync($"/api/devices/{device}", new DeviceWatchesRequest([location], pushToken));
        Assert.Equal(HttpStatusCode.OK, registered.StatusCode);
        return (device, location);
    }

    /// <summary>One poller tick a minute after the last, so the widget area is due (and the list reloaded).</summary>
    private async Task TickAsync(params Flight[] flights)
    {
        provider.Flights = flights;
        clock.Advance(TimeSpan.FromSeconds(60));
        await using var scope = app.Services.CreateAsyncScope();
        await ActivatorUtilities.CreateInstance<PollerTick>(scope.ServiceProvider).RunAsync(default);
    }

    private static Flight Overhead(string hex) => FlightAt(hex, 53.43, -6.27);

    private static string? HexIn(PushMessage message) =>
        message.Data.TryGetValue("flight", out var json)
            ? JsonDocument.Parse(json).RootElement.GetProperty("hex").GetString()
            : null;

    [Fact]
    public async Task AircraftArrivesStaysAndLeaves_ChangesUrgentRefreshesNot()
    {
        var (_, location) = await RegisterHomeAsync("fid-1");

        await TickAsync();                    // first look, clear skies: nothing to say
        Assert.Empty(sender.Sent);

        await TickAsync(Overhead("abc123"));  // arrives: news
        var arrived = Assert.Single(sender.Sent);
        Assert.Equal("fid-1", arrived.Token);
        Assert.Equal(PushPriority.High, arrived.Priority);
        Assert.Equal("nearest", arrived.Data["type"]);
        Assert.Equal(location.ToString(), arrived.Data["locationId"]);
        Assert.Equal("abc123", HexIn(arrived));
        Assert.Equal("1", arrived.Data["count"]);

        await TickAsync(Overhead("abc123"));  // still there: a refresh, not urgent
        Assert.Equal(2, sender.Sent.Count);
        Assert.Equal(PushPriority.Normal, sender.Sent[1].Priority);
        Assert.Equal("abc123", HexIn(sender.Sent[1]));

        await TickAsync();                    // gone: "clear skies" is news, with no aircraft in it
        Assert.Equal(3, sender.Sent.Count);
        Assert.Equal(PushPriority.High, sender.Sent[2].Priority);
        Assert.Null(HexIn(sender.Sent[2]));
        Assert.Equal("0", sender.Sent[2].Data["count"]);

        await TickAsync();                    // still clear: nothing to refresh
        Assert.Equal(3, sender.Sent.Count);
    }

    [Fact]
    public async Task OnlyAircraftOnTheGround_NoRefreshes()
    {
        await RegisterHomeAsync("fid-1");
        await TickAsync();
        var parked = Overhead("park01") with { OnGround = true };

        await TickAsync(parked);              // the widget now shows it: a change
        await TickAsync(parked);              // still parked: nothing moves, nothing to refresh

        Assert.Equal(PushPriority.High, Assert.Single(sender.Sent).Priority);
    }

    [Fact]
    public async Task OutsideTheExactCircle_DoesNotCount()
    {
        await RegisterHomeAsync("fid-1");
        await TickAsync();

        // Inside the snapped area the poller fetches (it's bigger), but ~11 km from Home: outside its 10 km.
        await TickAsync(FlightAt("far", 53.521, -6.270));

        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task DeadAddress_IsClearedFromTheDevice()
    {
        var (device, _) = await RegisterHomeAsync("fid-dead");
        sender.Outcome = PushOutcome.TokenGone;
        await TickAsync();

        await TickAsync(Overhead("abc123"));

        await using var db = PostgresFixture.CreateContext(connectionString);
        var stored = await db.Devices.SingleAsync(d => d.Id == device);
        Assert.Null(stored.PushToken);
    }

    [Fact]
    public async Task DeviceWithoutAPushAddress_GetsNothing()
    {
        await RegisterHomeAsync(pushToken: null);
        await TickAsync();

        await TickAsync(Overhead("abc123"));

        Assert.Empty(sender.Sent);
    }
}
