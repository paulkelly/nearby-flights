using System.Net;
using System.Text.Json;
using FlightRadar.Api.RateLimiting;
using FlightRadar.Core.Flights;
using FlightRadar.Tests.Fakes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace FlightRadar.Tests.RateLimiting;

/// <summary>
/// The real pipeline with a small bucket (3 tokens, 1 back per second) and a fake provider. Requests are sent straight
/// into the test server so each can come "from" a chosen client IP. Each test builds its own app, so buckets start full.
/// </summary>
public sealed class RateLimitingTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const int TokenLimit = 3;
    private const string FlightsQuery = "?lat=53.42&lon=-6.27&radiusKm=50";
    private static readonly IPAddress Alice = IPAddress.Parse("203.0.113.7");
    private static readonly IPAddress Bob = IPAddress.Parse("203.0.113.8");

    private (WebApplicationFactory<Program> App, FakeFlightDataProvider Provider) CreateApp(string? tokenLimit = null)
    {
        var provider = new FakeFlightDataProvider();
        var app = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("RateLimiting:TokenLimit", tokenLimit ?? TokenLimit.ToString());
            builder.UseSetting("RateLimiting:TokensPerPeriod", "1");
            builder.UseSetting("RateLimiting:ReplenishmentPeriodSeconds", "1");
            builder.ConfigureTestServices(services => services.AddSingleton<IFlightDataProvider>(provider));
        });
        return (app, provider);
    }

    private static Task<HttpContext> SendAsync(
        WebApplicationFactory<Program> app, IPAddress from, string path = "/api/flights", string query = FlightsQuery) =>
        app.Server.SendAsync(context =>
        {
            context.Request.Method = HttpMethods.Get;
            context.Request.Path = path;
            context.Request.QueryString = new QueryString(query);
            context.Connection.RemoteIpAddress = from;
        });

    private static async Task<List<int>> SendManyAsync(WebApplicationFactory<Program> app, IPAddress from, int count)
    {
        var statuses = new List<int>();
        for (var i = 0; i < count; i++)
        {
            statuses.Add((await SendAsync(app, from)).Response.StatusCode);
        }
        return statuses;
    }

    [Fact]
    public async Task Flights_BurstOverTheTokenLimit_Returns429ForTheExtraRequests()
    {
        var (app, provider) = CreateApp();

        var statuses = await SendManyAsync(app, Alice, TokenLimit + 2);

        Assert.Equal([200, 200, 200, 429, 429], statuses);
        Assert.Equal(TokenLimit, provider.Calls.Count); // rejected requests never reach the provider
    }

    [Fact]
    public async Task Flights_Rejected_ReturnsProblemDetailsWithRetryAfter()
    {
        var (app, _) = CreateApp();
        await SendManyAsync(app, Alice, TokenLimit);

        var rejected = await SendAsync(app, Alice);

        Assert.Equal(StatusCodes.Status429TooManyRequests, rejected.Response.StatusCode);
        Assert.Equal("1", rejected.Response.Headers.RetryAfter.ToString()); // the next token is ≤ 1 s away
        Assert.Equal("application/problem+json", rejected.Response.ContentType);
        using var body = await JsonDocument.ParseAsync(rejected.Response.Body);
        Assert.Equal("Too many requests", body.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Flights_DifferentClients_HaveSeparateBuckets()
    {
        var (app, _) = CreateApp();
        await SendManyAsync(app, Alice, TokenLimit + 1); // Alice is now being limited...

        var bob = await SendAsync(app, Bob);

        Assert.Equal(StatusCodes.Status200OK, bob.Response.StatusCode); // ...which doesn't affect Bob
    }

    [Fact]
    public async Task Flights_IPv4AndItsIPv6MappedForm_ShareOneBucket()
    {
        var (app, _) = CreateApp();
        await SendManyAsync(app, Alice, TokenLimit);

        var sameClient = await SendAsync(app, Alice.MapToIPv6()); // "::ffff:203.0.113.7"

        Assert.Equal(StatusCodes.Status429TooManyRequests, sameClient.Response.StatusCode);
    }

    [Fact]
    public async Task Flights_AfterTheReplenishmentPeriod_AllowsRequestsAgain()
    {
        var (app, _) = CreateApp();
        var statuses = await SendManyAsync(app, Alice, TokenLimit + 1);
        Assert.Equal(429, statuses[^1]); // limited first, so the request below proves the bucket refilled

        await Task.Delay(TimeSpan.FromSeconds(1.5)); // one replenishment period (1 s) and some slack

        Assert.Equal(StatusCodes.Status200OK, (await SendAsync(app, Alice)).Response.StatusCode);
    }

    [Fact]
    public async Task Health_WhileTheClientIsLimited_IsNeverLimited()
    {
        var (app, _) = CreateApp();
        await SendManyAsync(app, Alice, TokenLimit + 1);

        // Docker's health probe must always get through, however busy the API is.
        for (var i = 0; i < 10; i++)
        {
            var health = await SendAsync(app, Alice, path: "/health/live", query: "");
            Assert.Equal(StatusCodes.Status200OK, health.Response.StatusCode);
        }
    }

    [Fact]
    public void Startup_InvalidRateLimitOptions_FailsFast()
    {
        var (app, _) = CreateApp(tokenLimit: "0");

        var exception = Assert.ThrowsAny<Exception>(() => app.CreateClient());

        Assert.Contains(nameof(ClientRateLimitOptions.TokenLimit), exception.ToString());
    }
}
