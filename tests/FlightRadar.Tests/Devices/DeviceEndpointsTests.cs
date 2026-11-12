using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FlightRadar.Api.Devices;
using FlightRadar.Api.Locations;
using FlightRadar.Api.Owners;
using FlightRadar.Tests.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace FlightRadar.Tests.Devices;

[Trait("Category", "Database")]
[Collection(PostgresCollection.Name)]
public sealed class DeviceEndpointsTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly SavedLocationRequest Home = new("Home", 53.421, -6.270, 10);

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
        });
    }

    public async Task DisposeAsync() => await app.DisposeAsync();

    private async Task<HttpClient> NewOwnerAsync()
    {
        var client = app.CreateClient();
        var response = await client.PostAsync("/api/owners", content: null);
        var token = (await response.Content.ReadFromJsonAsync<NewOwnerResponse>())!.Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<Guid> SaveAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/locations", Home with { Name = name });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SavedLocationDto>())!.Id;
    }

    private static async Task<HttpResponseMessage> PutAsync(HttpClient client, Guid deviceId, params Guid[] locationIds) =>
        await client.PutAsJsonAsync($"/api/devices/{deviceId}", new DeviceWatchesRequest(locationIds));

    private static async Task<IReadOnlyList<Guid>> AcceptedAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<DeviceWatchesDto>())!.LocationIds;
    }

    private async Task<List<DeviceWatch>> WatchesAsync(Guid deviceId)
    {
        await using var db = PostgresFixture.CreateContext(connectionString);
        return await db.DeviceWatches.Where(watch => watch.DeviceId == deviceId).ToListAsync();
    }

    [Fact]
    public async Task Put_OwnLocations_StoresThemAndEchoesThem()
    {
        var client = await NewOwnerAsync();
        var home = await SaveAsync(client, "Home");
        var work = await SaveAsync(client, "Work");
        var device = Guid.NewGuid();

        var accepted = await AcceptedAsync(await PutAsync(client, device, home, work));

        Assert.Equal([home, work], accepted);
        Assert.Equal(new HashSet<Guid> { home, work }, (await WatchesAsync(device)).Select(w => w.LocationId).ToHashSet());
    }

    [Fact]
    public async Task Put_UnknownAndOtherOwnersLocations_AreLeftOutNotRefused()
    {
        var alice = await NewOwnerAsync();
        var bob = await NewOwnerAsync();
        var home = await SaveAsync(alice, "Home");
        var bobsHome = await SaveAsync(bob, "Home");
        var device = Guid.NewGuid();

        var accepted = await AcceptedAsync(await PutAsync(alice, device, home, bobsHome, Guid.NewGuid(), home));

        Assert.Equal([home], accepted); // duplicates once, Bob's and the made-up one silently dropped
        Assert.Equal(home, Assert.Single(await WatchesAsync(device)).LocationId);
    }

    [Fact]
    public async Task Put_AgainWithMore_KeepsTheFirstWatchsTimeAndAddsTheNew()
    {
        var client = await NewOwnerAsync();
        var home = await SaveAsync(client, "Home");
        var work = await SaveAsync(client, "Work");
        var device = Guid.NewGuid();
        await PutAsync(client, device, home);
        var first = Assert.Single(await WatchesAsync(device));

        await AcceptedAsync(await PutAsync(client, device, work, home));

        var watches = await WatchesAsync(device);
        Assert.Equal(2, watches.Count);
        Assert.Equal(first.CreatedAt, watches.Single(w => w.LocationId == home).CreatedAt); // still first in line
        Assert.True(watches.Single(w => w.LocationId == work).CreatedAt >= first.CreatedAt);
    }

    [Fact]
    public async Task Put_AgainWithFewer_RemovesTheDropped()
    {
        var client = await NewOwnerAsync();
        var home = await SaveAsync(client, "Home");
        var work = await SaveAsync(client, "Work");
        var device = Guid.NewGuid();
        await PutAsync(client, device, home, work);

        await AcceptedAsync(await PutAsync(client, device, work));

        Assert.Equal(work, Assert.Single(await WatchesAsync(device)).LocationId);
    }

    [Fact]
    public async Task Put_Again_UpdatesLastSeen()
    {
        var client = await NewOwnerAsync();
        var home = await SaveAsync(client, "Home");
        var device = Guid.NewGuid();
        await PutAsync(client, device, home);
        await using var db = PostgresFixture.CreateContext(connectionString);
        var before = (await db.Devices.SingleAsync(d => d.Id == device)).LastSeenAt;

        await Task.Delay(10);
        await PutAsync(client, device, home);

        db.ChangeTracker.Clear();
        Assert.True((await db.Devices.SingleAsync(d => d.Id == device)).LastSeenAt > before);
    }

    [Fact]
    public async Task Put_Empty_ForgetsTheDevice()
    {
        var client = await NewOwnerAsync();
        var home = await SaveAsync(client, "Home");
        var device = Guid.NewGuid();
        await PutAsync(client, device, home);

        Assert.Empty(await AcceptedAsync(await PutAsync(client, device)));

        await using var db = PostgresFixture.CreateContext(connectionString);
        Assert.False(await db.Devices.AnyAsync(d => d.Id == device));
        Assert.Empty(await WatchesAsync(device));
    }

    [Fact]
    public async Task Put_NothingKnownForANewDevice_StoresNothing()
    {
        var client = await NewOwnerAsync();
        var device = Guid.NewGuid();

        Assert.Empty(await AcceptedAsync(await PutAsync(client, device, Guid.NewGuid())));

        await using var db = PostgresFixture.CreateContext(connectionString);
        Assert.False(await db.Devices.AnyAsync(d => d.Id == device));
    }

    [Fact]
    public async Task Put_AnotherOwnersDevice_Returns404AndLeavesItAlone()
    {
        var alice = await NewOwnerAsync();
        var bob = await NewOwnerAsync();
        var home = await SaveAsync(alice, "Home");
        var bobsHome = await SaveAsync(bob, "Home");
        var device = Guid.NewGuid();
        await PutAsync(alice, device, home);

        var response = await PutAsync(bob, device, bobsHome);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(home, Assert.Single(await WatchesAsync(device)).LocationId);
    }

    private async Task<string?> PushTokenAsync(Guid deviceId)
    {
        await using var db = PostgresFixture.CreateContext(connectionString);
        return (await db.Devices.SingleAsync(d => d.Id == deviceId)).PushToken;
    }

    [Fact]
    public async Task Put_WithAPushToken_StoresIt_AndLeavingItOutClearsIt()
    {
        var client = await NewOwnerAsync();
        var home = await SaveAsync(client, "Home");
        var device = Guid.NewGuid();

        await AcceptedAsync(await client.PutAsJsonAsync($"/api/devices/{device}", new DeviceWatchesRequest([home], " fid-1 ")));
        Assert.Equal("fid-1", await PushTokenAsync(device));

        await AcceptedAsync(await PutAsync(client, device, home)); // a full replace: no token = no pushes
        Assert.Null(await PushTokenAsync(device));
    }

    [Fact]
    public async Task Put_PushTokenTooLong_Returns400()
    {
        var client = await NewOwnerAsync();
        var home = await SaveAsync(client, "Home");

        var response = await client.PutAsJsonAsync($"/api/devices/{Guid.NewGuid()}",
            new DeviceWatchesRequest([home], new string('x', DeviceEndpoints.MaxPushTokenLength + 1)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_WithoutAToken_Returns401()
    {
        var response = await PutAsync(app.CreateClient(), Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_NoListOrTooMany_Returns400()
    {
        var client = await NewOwnerAsync();
        string url = $"/api/devices/{Guid.NewGuid()}";

        var missing = await client.PutAsJsonAsync(url, new { });
        var tooMany = await client.PutAsJsonAsync(url, new DeviceWatchesRequest(
            Enumerable.Range(0, DeviceEndpoints.MaxLocationIds + 1).Select(_ => Guid.NewGuid()).ToList()));

        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooMany.StatusCode);
    }

    [Fact]
    public async Task DeletingALocation_EndsItsWatches()
    {
        var client = await NewOwnerAsync();
        var home = await SaveAsync(client, "Home");
        var device = Guid.NewGuid();
        await PutAsync(client, device, home);

        await client.DeleteAsync($"/api/locations/{home}");

        Assert.Empty(await WatchesAsync(device)); // ON DELETE CASCADE
    }

    [Fact]
    public async Task DeletingAnOwner_DeletesTheirDevices()
    {
        var client = await NewOwnerAsync();
        var device = Guid.NewGuid();
        await PutAsync(client, device, await SaveAsync(client, "Home"));

        await using var db = PostgresFixture.CreateContext(connectionString);
        await db.Owners.ExecuteDeleteAsync();

        Assert.Equal(0, await db.Devices.CountAsync());
        Assert.Equal(0, await db.DeviceWatches.CountAsync());
    }

    [Fact]
    public void MaxLocationIdsText_MatchesTheLimit() =>
        Assert.Equal(DeviceEndpoints.MaxLocationIdsText, DeviceEndpoints.MaxLocationIds.ToString());
}
