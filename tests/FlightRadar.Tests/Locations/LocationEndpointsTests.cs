using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FlightRadar.Api.Locations;
using FlightRadar.Api.Owners;
using FlightRadar.Tests.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace FlightRadar.Tests.Locations;

[Trait("Category", "Database")]
[Collection(PostgresCollection.Name)]
public sealed class LocationEndpointsTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly SavedLocationRequest Home = new("Home", 53.42134, -6.27007, 10);

    private WebApplicationFactory<Program> app = null!;
    private string connectionString = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Redis", "");
            builder.UseSetting("ConnectionStrings:Postgres", connectionString);
            builder.UseSetting("RateLimiting:TokenLimit", "1000"); // these tests send more than a person would
        });
    }

    public async Task DisposeAsync() => await app.DisposeAsync();

    /// <summary>A client signed in as a brand-new owner.</summary>
    private async Task<HttpClient> NewOwnerAsync()
    {
        var client = app.CreateClient();
        var response = await client.PostAsync("/api/owners", content: null);
        var token = (await response.Content.ReadFromJsonAsync<NewOwnerResponse>())!.Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<SavedLocationDto> SaveAsync(HttpClient client, SavedLocationRequest request)
    {
        var response = await client.PostAsJsonAsync("/api/locations", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SavedLocationDto>())!;
    }

    private static async Task<List<SavedLocationDto>> ListAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<SavedLocationDto>>("/api/locations"))!;

    // ---- The basics ----

    [Fact]
    public async Task Create_Valid_Returns201WithRoundedCoordinatesAndLocation()
    {
        var client = await NewOwnerAsync();

        var response = await client.PostAsJsonAsync("/api/locations", Home with { Name = " Home " });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var saved = (await response.Content.ReadFromJsonAsync<SavedLocationDto>())!;
        Assert.Equal(("Home", 53.421, -6.270, 10.0), (saved.Name, saved.Lat, saved.Lon, saved.RadiusKm));
        Assert.Equal($"/api/locations/{saved.Id}", response.Headers.Location?.OriginalString);
        Assert.Equal(saved, await client.GetFromJsonAsync<SavedLocationDto>(response.Headers.Location));
    }

    [Fact]
    public async Task List_Always_ReturnsOwnLocationsOldestFirst()
    {
        var client = await NewOwnerAsync();
        await SaveAsync(client, Home);
        await SaveAsync(client, Home with { Name = "Work" });

        Assert.Equal(["Home", "Work"], (await ListAsync(client)).Select(location => location.Name));
    }

    [Fact]
    public async Task Update_Valid_RenamesAndMoves()
    {
        var client = await NewOwnerAsync();
        var saved = await SaveAsync(client, Home);

        var response = await client.PutAsJsonAsync($"/api/locations/{saved.Id}", new SavedLocationRequest("Gran's", 51.5, -0.1, 25));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new SavedLocationDto(saved.Id, "Gran's", 51.5, -0.1, 25), Assert.Single(await ListAsync(client)));
    }

    [Fact]
    public async Task Update_KeepingItsOwnName_IsNotAConflict()
    {
        var client = await NewOwnerAsync();
        var saved = await SaveAsync(client, Home);

        var response = await client.PutAsJsonAsync($"/api/locations/{saved.Id}", Home with { RadiusKm = 50 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Delete_Own_Returns204AndItIsGone()
    {
        var client = await NewOwnerAsync();
        var saved = await SaveAsync(client, Home);

        var response = await client.DeleteAsync($"/api/locations/{saved.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await ListAsync(client));
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/locations/{saved.Id}")).StatusCode);
    }

    // ---- Owners are kept apart ----

    [Fact]
    public async Task List_TwoOwners_EachSeesOnlyTheirOwn()
    {
        var alice = await NewOwnerAsync();
        var bob = await NewOwnerAsync();
        await SaveAsync(alice, Home);
        await SaveAsync(bob, Home with { Name = "Bob's place" });

        Assert.Equal(["Home"], (await ListAsync(alice)).Select(location => location.Name));
        Assert.Equal(["Bob's place"], (await ListAsync(bob)).Select(location => location.Name));
    }

    [Fact]
    public async Task AnotherOwnersLocation_CanNotBeReadChangedOrDeleted_AndLooksMissing()
    {
        var alice = await NewOwnerAsync();
        var bob = await NewOwnerAsync();
        var alicesHome = await SaveAsync(alice, Home);
        string url = $"/api/locations/{alicesHome.Id}";

        // 404, the same as an ID that doesn't exist: 403 would tell Bob that Alice's location is real.
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PutAsJsonAsync(url, Home with { Name = "Mine now" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.DeleteAsync(url)).StatusCode);

        Assert.Equal(alicesHome, Assert.Single(await ListAsync(alice))); // untouched
    }

    [Fact]
    public async Task Create_SameNameAsAnotherOwner_IsFine()
    {
        await SaveAsync(await NewOwnerAsync(), Home);
        await SaveAsync(await NewOwnerAsync(), Home); // asserts 201
    }

    [Fact]
    public async Task Requests_WithoutAToken_Return401()
    {
        var anonymous = app.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/locations")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/locations", Home)).StatusCode);
    }

    // ---- Conflicts and validation ----

    [Fact]
    public async Task Create_NameAlreadyUsed_Returns409()
    {
        var client = await NewOwnerAsync();
        await SaveAsync(client, Home);

        var response = await client.PostAsJsonAsync("/api/locations", Home with { Lat = 0 });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Single(await ListAsync(client));
    }

    [Fact]
    public async Task Update_ToAnotherOfOwnNames_Returns409()
    {
        var client = await NewOwnerAsync();
        await SaveAsync(client, Home);
        var work = await SaveAsync(client, Home with { Name = "Work" });

        var response = await client.PutAsJsonAsync($"/api/locations/{work.Id}", Home);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Create_AtTheLimit_Returns409()
    {
        var client = await NewOwnerAsync();
        for (int i = 1; i <= SavedLocationRules.MaxPerOwner; i++)
        {
            await SaveAsync(client, Home with { Name = $"Place {i}" });
        }

        var response = await client.PostAsJsonAsync("/api/locations", Home with { Name = "One too many" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(SavedLocationRules.MaxPerOwner, (await ListAsync(client)).Count);
    }

    [Fact]
    public async Task Create_Invalid_Returns400WithEveryProblem()
    {
        var client = await NewOwnerAsync();

        var response = await client.PostAsJsonAsync("/api/locations", new SavedLocationRequest("", 91, 0, 0));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblem>();
        Assert.Equal(["lat", "name", "radiusKm"], problem!.Errors.Keys.Order());
    }

    // ---- The database itself ----

    [Fact]
    public async Task DeletingAnOwner_DeletesTheirLocations()
    {
        var client = await NewOwnerAsync();
        await SaveAsync(client, Home);

        await using var db = PostgresFixture.CreateContext(connectionString);
        await db.Owners.ExecuteDeleteAsync();

        Assert.Equal(0, await db.SavedLocations.CountAsync()); // the foreign key's ON DELETE CASCADE
    }

    private sealed record HttpValidationProblem(Dictionary<string, string[]> Errors);
}
