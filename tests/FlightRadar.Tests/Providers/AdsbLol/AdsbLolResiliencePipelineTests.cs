using System.Diagnostics;
using System.Net;
using FlightRadar.Api.Providers.AdsbLol;
using FlightRadar.Core.Flights;
using FlightRadar.Core.Geo;
using FlightRadar.Tests.Fakes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Polly.CircuitBreaker;
using Polly.RateLimiting;
using Polly.Timeout;

namespace FlightRadar.Tests.Providers.AdsbLol;

/// <summary>
/// The provider exactly as the app wires it (typed HttpClient + resilience pipeline from
/// AddAdsbLolFlightDataProvider), with only the bottom network handler replaced by a fake. That way these tests
/// check the real retry, timeout and circuit-breaker behaviour rather than assumptions about it.
/// </summary>
public sealed class AdsbLolResiliencePipelineTests
{
    private static readonly GeoPoint Dublin = new(53.42, -6.27);

    private static IFlightDataProvider CreateProvider(
        FakeHttpMessageHandler network,
        int totalTimeoutSeconds = 8,
        int attemptTimeoutSeconds = 3,
        int maxConcurrentRequests = 10,
        int maxQueuedRequests = 20)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Providers:AdsbLol:BaseUrl"] = "https://api.adsb.lol/",
                ["Providers:AdsbLol:TotalTimeoutSeconds"] = totalTimeoutSeconds.ToString(),
                ["Providers:AdsbLol:AttemptTimeoutSeconds"] = attemptTimeoutSeconds.ToString(),
                ["Providers:AdsbLol:MaxRetries"] = "2",
                ["Providers:AdsbLol:MaxConcurrentRequests"] = maxConcurrentRequests.ToString(),
                ["Providers:AdsbLol:MaxQueuedRequests"] = maxQueuedRequests.ToString(),
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddAdsbLolFlightDataProvider();
        // Swap the real network (SocketsHttpHandler) at the bottom of every client's pipeline for the fake.
        services.ConfigureHttpClientDefaults(client => client.ConfigurePrimaryHttpMessageHandler(() => network));

        return services.BuildServiceProvider().GetRequiredService<AdsbLolFlightDataProvider>();
    }

    private static Task<HttpResponseMessage> Respond(HttpStatusCode status, string body = AdsbLolSamples.Empty) =>
        Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });

    [Fact]
    public async Task GetFlightsAsync_TransientFailureThenSuccess_RetriesAndSucceeds()
    {
        var calls = 0;
        var network = new FakeHttpMessageHandler((_, _) =>
            ++calls == 1 ? Respond(HttpStatusCode.ServiceUnavailable) : Respond(HttpStatusCode.OK, AdsbLolSamples.Mixed));
        var provider = CreateProvider(network);

        var flights = await provider.GetFlightsAsync(Dublin, 50, CancellationToken.None);

        Assert.Equal(3, flights.Count);
        Assert.Equal(2, network.Requests.Count); // one 503, one retry that succeeded
    }

    [Fact]
    public async Task GetFlightsAsync_ClientError_IsNotRetried()
    {
        var network = FakeHttpMessageHandler.Status(HttpStatusCode.BadRequest);
        var provider = CreateProvider(network);

        await Assert.ThrowsAsync<FlightDataUnavailableException>(
            () => provider.GetFlightsAsync(Dublin, 50, CancellationToken.None));

        Assert.Single(network.Requests); // a 400 means *our* request is wrong; repeating it can't help
    }

    [Fact]
    public async Task GetFlightsAsync_PersistentServerError_GivesUpAfterRetries()
    {
        var network = FakeHttpMessageHandler.Status(HttpStatusCode.InternalServerError);
        var provider = CreateProvider(network);

        await Assert.ThrowsAsync<FlightDataUnavailableException>(
            () => provider.GetFlightsAsync(Dublin, 50, CancellationToken.None));

        Assert.Equal(3, network.Requests.Count); // first attempt + MaxRetries (2)
    }

    [Fact]
    public async Task GetFlightsAsync_UpstreamKeepsFailing_CircuitOpensAndStopsCallingIt()
    {
        var network = FakeHttpMessageHandler.Status(HttpStatusCode.ServiceUnavailable);
        var provider = CreateProvider(network);

        // Enough failing attempts (3 per call) to pass the breaker's minimum of 5 at a 100% failure rate.
        for (var i = 0; i < 3; i++)
        {
            await Assert.ThrowsAsync<FlightDataUnavailableException>(
                () => provider.GetFlightsAsync(Dublin, 50, CancellationToken.None));
        }
        var requestsBefore = network.Requests.Count;

        var exception = await Assert.ThrowsAsync<FlightDataUnavailableException>(
            () => provider.GetFlightsAsync(Dublin, 50, CancellationToken.None));

        Assert.IsType<BrokenCircuitException>(exception.InnerException);
        Assert.Equal(requestsBefore, network.Requests.Count); // failed fast: no network call at all
    }

    [Fact]
    public async Task GetFlightsAsync_UpstreamTooSlow_TimesOutWithinTheTotalBudget()
    {
        var network = new FakeHttpMessageHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct); // never answers; the pipeline's timeouts cancel `ct`
            throw new UnreachableException();
        });
        var provider = CreateProvider(network, totalTimeoutSeconds: 2, attemptTimeoutSeconds: 1);
        var stopwatch = Stopwatch.StartNew();

        var exception = await Assert.ThrowsAsync<FlightDataUnavailableException>(
            () => provider.GetFlightsAsync(Dublin, 50, CancellationToken.None));

        Assert.IsType<TimeoutRejectedException>(exception.InnerException);
        Assert.InRange(stopwatch.Elapsed, TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(5)); // ~2 s, not forever
    }

    [Fact]
    public async Task GetFlightsAsync_MoreCallsThanTheConcurrencyLimit_QueuesSomeAndRejectsTheRest()
    {
        // The network holds every call until released, and records how many were in flight at once.
        var release = new TaskCompletionSource();
        int inFlight = 0, maxInFlight = 0;
        var network = new FakeHttpMessageHandler(async (_, ct) =>
        {
            var now = Interlocked.Increment(ref inFlight);
            InterlockedMax(ref maxInFlight, now);
            await release.Task.WaitAsync(ct);
            Interlocked.Decrement(ref inFlight);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(AdsbLolSamples.Empty) };
        });
        var provider = CreateProvider(network, maxConcurrentRequests: 2, maxQueuedRequests: 1);

        // 4 calls at once: 2 run, 1 waits in the queue, 1 is turned away immediately.
        var calls = Enumerable.Range(0, 4)
            .Select(_ => provider.GetFlightsAsync(Dublin, 50, CancellationToken.None))
            .ToList();
        var rejected = await Assert.ThrowsAsync<FlightDataUnavailableException>(() => calls[3]);
        Assert.IsType<RateLimiterRejectedException>(rejected.InnerException); // → 502 for that request
        Assert.Equal(2, network.Requests.Count);                             // the queued one hasn't started

        release.SetResult();
        await Task.WhenAll(calls.Take(3));

        Assert.Equal(3, network.Requests.Count); // the queued call ran once a slot was free
        Assert.Equal(2, maxInFlight);            // never more than the limit at the network
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while (value > (current = Volatile.Read(ref target)) &&
               Interlocked.CompareExchange(ref target, value, current) != current)
        {
        }
    }
}
