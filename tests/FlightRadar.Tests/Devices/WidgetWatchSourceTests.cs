using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FlightRadar.Api.Caching;
using FlightRadar.Api.Devices;
using FlightRadar.Api.Locations;
using FlightRadar.Api.Owners;
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

namespace FlightRadar.Tests.Devices;

/// <summary>
/// The widget source against a real database, through the real endpoints: register a device, then ask the source
/// (as the poller would each tick) with a clock the test moves.
/// </summary>
[Trait("Category", "Database")]
[Collection(PostgresCollection.Name)]
public sealed class WidgetWatchSourceTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly SavedLocationRequest Home = new("Home", 53.421, -6.270, 10);
    private static readonly SearchArea HomeArea =
        AreaSnapping.Snap(new GeoPoint(53.421, -6.270), 10, double.PositiveInfinity); // the fake provider has no limit

    private readonly FakeTimeProvider clock = new(new DateTimeOffset(2026, 11, 12, 12, 0, 0, TimeSpan.Zero));
    private readonly FakeFlightDataProvider provider = new();
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
                services.AddTransient(serviceProvider => new CachingFlightDataProvider(
                    provider,
                    serviceProvider.GetRequiredService<HybridCache>(),
                    serviceProvider.GetRequiredService<IOptions<FlightCacheOptions>>(),
                    NullLogger<CachingFlightDataProvider>.Instance));
            });
        });
    }

    public async Task DisposeAsync() => await app.DisposeAsync();

    private WidgetWatchSource Source => app.Services.GetRequiredService<WidgetWatchSource>();

    private async Task<HttpClient> NewOwnerAsync()
    {
        var client = app.CreateClient();
        var response = await client.PostAsync("/api/owners", content: null);
        var token = (await response.Content.ReadFromJsonAsync<NewOwnerResponse>())!.Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<Guid> SaveAsync(HttpClient client, SavedLocationRequest request)
    {
        var response = await client.PostAsJsonAsync("/api/locations", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SavedLocationDto>())!.Id;
    }

    private static async Task RegisterAsync(HttpClient client, Guid device, params Guid[] locationIds)
    {
        var response = await client.PutAsJsonAsync($"/api/devices/{device}", new DeviceWatchesRequest(locationIds));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private Task<IReadOnlyCollection<SearchArea>> TickAsync() => Source.GetWatchedAreasAsync(default);

    [Fact]
    public async Task RegisteredLocation_IsHandedOutAtOnceThenEveryMinute()
    {
        var client = await NewOwnerAsync();
        await RegisterAsync(client, Guid.NewGuid(), await SaveAsync(client, Home));

        Assert.Equal([HomeArea], await TickAsync());
        for (int i = 0; i < 5; i++) // the next five 10 s ticks: not due
        {
            clock.Advance(TimeSpan.FromSeconds(10));
            Assert.Empty(await TickAsync());
        }
        clock.Advance(TimeSpan.FromSeconds(10)); // 60 s after the first
        Assert.Equal([HomeArea], await TickAsync());
    }

    [Fact]
    public async Task NewRegistration_IsPickedUpAtTheNextReload()
    {
        Assert.Empty(await TickAsync()); // loads an empty list
        var client = await NewOwnerAsync();
        await RegisterAsync(client, Guid.NewGuid(), await SaveAsync(client, Home));

        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Empty(await TickAsync()); // not reloaded yet
        clock.Advance(TimeSpan.FromSeconds(50));
        Assert.Equal([HomeArea], await TickAsync());
    }

    [Fact]
    public async Task OwnersFourthLocation_IsNotWatched()
    {
        var client = await NewOwnerAsync();
        var ids = new List<Guid>();
        for (int i = 1; i <= 4; i++)
        {
            ids.Add(await SaveAsync(client, Home with { Name = $"Place {i}", Lat = 40 + i }));
            clock.Advance(TimeSpan.FromSeconds(1)); // so each is registered after the previous
            await RegisterAsync(client, Guid.NewGuid(), ids[^1]);
        }

        var areas = await TickAsync();

        Assert.Equal(3, areas.Count);
        Assert.DoesNotContain(areas, area => Math.Abs(area.Centre.Latitude - 44) < 0.5); // the newest one
    }

    [Fact]
    public async Task DeviceSilentForAWeek_IsForgotten()
    {
        var client = await NewOwnerAsync();
        var device = Guid.NewGuid();
        await RegisterAsync(client, device, await SaveAsync(client, Home));
        Assert.Single(await TickAsync());

        clock.Advance(TimeSpan.FromDays(7) + TimeSpan.FromMinutes(1));

        Assert.Empty(await TickAsync());
        await using var db = PostgresFixture.CreateContext(connectionString);
        Assert.False(await db.Devices.AnyAsync(d => d.Id == device)); // deleted, not just ignored
    }

    [Fact]
    public async Task DeletedLocation_StopsBeingWatched()
    {
        var client = await NewOwnerAsync();
        var home = await SaveAsync(client, Home);
        await RegisterAsync(client, Guid.NewGuid(), home);
        Assert.Single(await TickAsync());

        await client.DeleteAsync($"/api/locations/{home}");
        clock.Advance(TimeSpan.FromSeconds(60));

        Assert.Empty(await TickAsync());
    }

    [Fact]
    public async Task PollerTick_WidgetArea_IsFetchedWithNobodyWatching()
    {
        var client = await NewOwnerAsync();
        await RegisterAsync(client, Guid.NewGuid(), await SaveAsync(client, Home));

        await using var scope = app.Services.CreateAsyncScope();
        var result = await ActivatorUtilities.CreateInstance<PollerTick>(scope.ServiceProvider).RunAsync(default);

        Assert.Equal(1, result.Fetched);
        Assert.Equal(HomeArea.Centre, Assert.Single(provider.Calls).Centre);
    }

    [Fact]
    public void WithoutADatabase_TheSourceIsNotRegistered()
    {
        using var noDatabase = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Redis", "");
            builder.UseSetting("ConnectionStrings:Postgres", "");
        });

        Assert.DoesNotContain(
            noDatabase.Services.GetServices<IWatchedAreaSource>(), source => source is WidgetWatchSource);
    }
}
