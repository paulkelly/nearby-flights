using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FlightRadar.Tests.Owners;

/// <summary>Owners when the database is missing or down: 503, never a 500 or a misleading 401. No Docker needed.</summary>
public sealed class OwnerEndpointsWithoutDatabaseTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string WellFormedToken = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    /// <param name="postgres">"" = none configured; anything else is used as the connection string.</param>
    private HttpClient CreateClient(string postgres) =>
        factory.WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:Redis", "");
                builder.UseSetting("ConnectionStrings:Postgres", postgres);
            })
            .CreateClient();

    public static TheoryData<string> Databases => new()
    {
        "", // not configured
        "Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x", // configured, nothing listening: refused at once
    };

    [Theory]
    [MemberData(nameof(Databases))]
    public async Task Create_NoDatabase_Returns503(string postgres)
    {
        var response = await CreateClient(postgres).PostAsync("/api/owners", content: null);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [MemberData(nameof(Databases))]
    public async Task Me_NoDatabase_Returns503NotUnauthorized(string postgres)
    {
        // The token can't be checked, which is not the same as being wrong: a 401 would tell the browser to throw
        // its token away.
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/owners/me");
        request.Headers.Authorization = new("Bearer", WellFormedToken);

        var response = await CreateClient(postgres).SendAsync(request);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task Flights_WithATokenButNoDatabase_StillWork()
    {
        // Authentication runs for every request carrying a token, but endpoints that don't require an owner must not
        // depend on the database. (Only reaches the endpoint's own validation: no flight provider is called.)
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/flights?lat=999&lon=0&radiusKm=10");
        request.Headers.Authorization = new("Bearer", WellFormedToken);

        var response = await CreateClient("").SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
