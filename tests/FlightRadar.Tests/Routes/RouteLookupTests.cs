using FlightRadar.Api.Routes;
using FlightRadar.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FlightRadar.Tests.Routes;

public sealed class RouteLookupTests
{
    private static RouteLookup Create(FakeRouteProvider routes, int waitMilliseconds = 100) =>
        new(routes,
            Options.Create(new RouteOptions { WaitMilliseconds = waitMilliseconds }),
            TimeProvider.System,
            NullLogger<RouteLookup>.Instance);

    [Fact]
    public async Task GetRoutesAsync_KnownRoutes_ReturnsThemByCallsign()
    {
        var routes = new FakeRouteProvider().WithRoute("EIN123", "EIDW", "KORD").WithRoute("RYR4CZ", "LEBL", "EGGW");

        var result = await Create(routes).GetRoutesAsync(["EIN123", "RYR4CZ"], CancellationToken.None);

        Assert.Equal(["EIDW", "KORD"], result["EIN123"].Airports.Select(a => a.Icao));
        Assert.Equal(["LEBL", "EGGW"], result["ryr4cz"].Airports.Select(a => a.Icao)); // keys ignore case
    }

    [Fact]
    public async Task GetRoutesAsync_UnknownRoute_IsLeftOut()
    {
        var routes = new FakeRouteProvider();

        var result = await Create(routes).GetRoutesAsync(["EIN999"], CancellationToken.None);

        Assert.Empty(result);
        Assert.Equal(["EIN999"], routes.Calls); // asked, and the answer was "none"
    }

    [Theory]
    [InlineData("EIGWY")]   // a registration: the route database only has scheduled flights
    [InlineData("N123AB")]
    [InlineData(null)]
    [InlineData("")]
    public async Task GetRoutesAsync_NotAnAirlineCallsign_IsNotLookedUp(string? callsign)
    {
        var routes = new FakeRouteProvider();

        await Create(routes).GetRoutesAsync([callsign], CancellationToken.None);

        Assert.Empty(routes.Calls);
    }

    [Fact]
    public async Task GetRoutesAsync_RepeatedOrPaddedCallsign_IsLookedUpOnceNormalised()
    {
        var routes = new FakeRouteProvider();

        await Create(routes).GetRoutesAsync(["EIN123", " ein123 ", "EIN123"], CancellationToken.None);

        Assert.Equal(["EIN123"], routes.Calls);
    }

    [Fact]
    public async Task GetRoutesAsync_LookupFails_LeavesThatRouteOutAndReturnsTheRest()
    {
        var routes = new FakeRouteProvider().WithRoute("EIN123", "EIDW", "KORD");
        routes.Failing.Add("RYR4CZ");

        var result = await Create(routes).GetRoutesAsync(["EIN123", "RYR4CZ"], CancellationToken.None);

        Assert.Equal(["EIN123"], result.Keys); // routes are extras: a failure never fails the response
    }

    [Fact]
    public async Task GetRoutesAsync_SlowLookup_ReturnsWithoutItAfterTheBudget()
    {
        var routes = new FakeRouteProvider().WithRoute("EIN123", "EIDW", "KORD").WithSlowRoute("RYR4CZ", "LEBL", "EGGW");

        var result = await Create(routes, waitMilliseconds: 50).GetRoutesAsync(["EIN123", "RYR4CZ"], CancellationToken.None);

        Assert.Equal(["EIN123"], result.Keys); // didn't wait for RYR4CZ
        routes.Release("RYR4CZ");
    }

    [Fact]
    public async Task GetRoutesAsync_SlowLookup_KeepsRunningAfterTheResponse()
    {
        // What the next poll relies on: the lookup isn't cancelled when we stop waiting, so it can finish and
        // (through the caching provider) be ready next time.
        // The request's token is cancelled once the response is sent, as ASP.NET Core does when a request ends,
        // so this fails if the lookup was handed the request's token.
        var routes = new FakeRouteProvider().WithSlowRoute("RYR4CZ", "LEBL", "EGGW");
        using var request = new CancellationTokenSource();
        await Create(routes, waitMilliseconds: 50).GetRoutesAsync(["RYR4CZ"], request.Token);
        await request.CancelAsync();

        routes.Release("RYR4CZ");

        await WaitUntil(() => routes.Finished.Contains("RYR4CZ"));
    }

    /// <summary>Polls until the background work the test is waiting for has happened, or fails after 5 s.</summary>
    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for the background lookup to finish.");
            await Task.Delay(10);
        }
    }

    [Fact]
    public async Task GetRoutesAsync_AllReadyEarly_DoesNotWaitOutTheBudget()
    {
        var routes = new FakeRouteProvider().WithRoute("EIN123", "EIDW", "KORD");
        var lookup = Create(routes, waitMilliseconds: 5_000);

        var started = TimeProvider.System.GetTimestamp();
        await lookup.GetRoutesAsync(["EIN123"], CancellationToken.None);

        Assert.True(TimeProvider.System.GetElapsedTime(started) < TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task GetRoutesAsync_CallerCancels_Throws()
    {
        var routes = new FakeRouteProvider().WithSlowRoute("RYR4CZ", "LEBL", "EGGW");
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Create(routes, waitMilliseconds: 5_000).GetRoutesAsync(["RYR4CZ"], cancelled.Token));
        routes.Release("RYR4CZ");
    }
}
