using System.Net;
using FlightRadar.Api.Logos;
using FlightRadar.Tests.Fakes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace FlightRadar.Tests.Logos;

/// <summary>The real API in memory with a fake logo source (the real airline list decides which codes exist).</summary>
public sealed class LogoEndpointsTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private (HttpClient Client, FakeAirlineLogoSource Source) CreateClient()
    {
        var source = new FakeAirlineLogoSource { Logos = { ["EIN"] = HttpAirlineLogoSourceTests.TinyPng } };
        var client = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
                services.AddSingleton<IAirlineLogoSource>(source)))
            .CreateClient();
        return (client, source);
    }

    [Fact]
    public async Task GetLogo_KnownAirlineWithLogo_ReturnsThePngForBrowsersToCache()
    {
        var (client, _) = CreateClient();

        var response = await client.GetAsync("/api/airlines/EIN/logo");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal(HttpAirlineLogoSourceTests.TinyPng, await response.Content.ReadAsByteArrayAsync());
        Assert.True(response.Headers.CacheControl!.Public);
        Assert.Equal(TimeSpan.FromHours(24), response.Headers.CacheControl.MaxAge);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
    }

    [Fact]
    public async Task GetLogo_LowerCaseCode_IsAccepted()
    {
        var (client, source) = CreateClient();

        var response = await client.GetAsync("/api/airlines/ein/logo");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["EIN"], source.Calls);
    }

    [Fact]
    public async Task GetLogo_KnownAirlineWithoutLogo_Returns404ForBrowsersToCache()
    {
        var (client, _) = CreateClient();

        var response = await client.GetAsync("/api/airlines/RYR/logo"); // in the airline list, but no fake logo

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(TimeSpan.FromHours(24), response.Headers.CacheControl!.MaxAge); // so the browser stops asking
    }

    [Theory]
    [InlineData("ZZZ")]       // not an airline in our list (careful: "XXX" is one!)
    [InlineData("EINX")]      // not a code at all
    [InlineData("..%2F..%2Fsecrets")]
    public async Task GetLogo_UnknownCode_Returns404WithoutAskingTheSource(string code)
    {
        // Only codes from our airline list reach the source: the endpoint can't be used to fetch arbitrary paths.
        var (client, source) = CreateClient();

        var response = await client.GetAsync($"/api/airlines/{code}/logo");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(source.Calls);
    }

    [Fact]
    public async Task GetLogo_SourceDown_Returns503ThatBrowsersDoNotCache()
    {
        var (client, source) = CreateClient();
        source.Failing = true;

        var response = await client.GetAsync("/api/airlines/EIN/logo");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore);
    }

    [Fact]
    public async Task GetLogo_ManyRequests_AreNotRateLimitedLikeFlights()
    {
        // One list view asks for a logo per airline on screen; the 20-request flights bucket would cut that short.
        var (client, _) = CreateClient();

        for (int i = 0; i < 30; i++)
        {
            var response = await client.GetAsync("/api/airlines/EIN/logo");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }
}
