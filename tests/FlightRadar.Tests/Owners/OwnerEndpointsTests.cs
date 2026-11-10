using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Buffers.Text;
using FlightRadar.Api.Owners;
using FlightRadar.Tests.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace FlightRadar.Tests.Owners;

[Trait("Category", "Database")]
[Collection(PostgresCollection.Name)]
public sealed class OwnerEndpointsTests(PostgresFixture postgres)
{
    /// <summary>The app on its own fresh, migrated database, without Redis.</summary>
    private async Task<(WebApplicationFactory<Program> App, string ConnectionString)> CreateAppAsync(
        string? newOwnerLimit = null)
    {
        string connectionString = await postgres.CreateMigratedDatabaseAsync();
        var app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Redis", "");
            builder.UseSetting("ConnectionStrings:Postgres", connectionString);
            if (newOwnerLimit is not null)
            {
                builder.UseSetting("RateLimiting:NewOwners:PermitLimit", newOwnerLimit);
            }
        });
        return (app, connectionString);
    }

    private static async Task<string> CreateOwnerAsync(HttpClient client)
    {
        var response = await client.PostAsync("/api/owners", content: null);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<NewOwnerResponse>())!.Token;
    }

    private static Task<HttpResponseMessage> GetMeAsync(HttpClient client, string? authorization)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/owners/me");
        if (authorization is not null)
        {
            request.Headers.TryAddWithoutValidation("Authorization", authorization);
        }
        return client.SendAsync(request);
    }

    [Fact]
    public async Task Create_Always_ReturnsATokenAndStoresOnlyItsHash()
    {
        var (app, connectionString) = await CreateAppAsync();
        await using var _ = app;

        string token = await CreateOwnerAsync(app.CreateClient());

        await using var db = PostgresFixture.CreateContext(connectionString);
        var stored = await db.Owners.SingleAsync();
        Assert.Equal(SHA256.HashData(Base64Url.DecodeFromChars(token)), stored.TokenHash);
        Assert.NotEqual(Base64Url.DecodeFromChars(token), stored.TokenHash); // the token itself is nowhere
    }

    [Fact]
    public async Task Me_WithItsToken_ReturnsTheOwner()
    {
        var (app, _) = await CreateAppAsync();
        await using var __ = app;
        var client = app.CreateClient();
        string token = await CreateOwnerAsync(client);

        var response = await GetMeAsync(client, $"Bearer {token}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var owner = await response.Content.ReadFromJsonAsync<OwnerResponse>();
        Assert.True(DateTimeOffset.UtcNow - owner!.CreatedAt < TimeSpan.FromMinutes(1));
    }

    [Theory]
    [InlineData(null)]                                                      // no token: anonymous
    [InlineData("Bearer not-a-token")]                                      // malformed
    [InlineData("Bearer AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]      // well-formed, but nobody's
    [InlineData("Basic dXNlcjpwYXNz")]                                      // another scheme altogether
    public async Task Me_WithoutAValidToken_Returns401WithBearerChallenge(string? authorization)
    {
        var (app, _) = await CreateAppAsync();
        await using var __ = app;
        var client = app.CreateClient();
        await CreateOwnerAsync(client); // an owner exists, but the request doesn't prove it's them

        var response = await GetMeAsync(client, authorization);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", Assert.Single(response.Headers.WwwAuthenticate).Scheme);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Me_TokenSchemeInAnyCase_IsAccepted()
    {
        // The scheme name is case-insensitive in HTTP (RFC 9110), the token itself isn't.
        var (app, _) = await CreateAppAsync();
        await using var __ = app;
        var client = app.CreateClient();
        string token = await CreateOwnerAsync(client);

        Assert.Equal(HttpStatusCode.OK, (await GetMeAsync(client, $"bearer {token}")).StatusCode);
    }

    [Fact]
    public async Task Create_TooManyFromOneClient_Returns429()
    {
        var (app, connectionString) = await CreateAppAsync(newOwnerLimit: "2");
        await using var __ = app;
        var client = app.CreateClient();
        await CreateOwnerAsync(client);
        await CreateOwnerAsync(client);

        var third = await client.PostAsync("/api/owners", content: null);

        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        Assert.NotNull(third.Headers.RetryAfter);
        await using var db = PostgresFixture.CreateContext(connectionString);
        Assert.Equal(2, await db.Owners.CountAsync()); // the rejected request never reached the database
    }
}
