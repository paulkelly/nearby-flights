using FlightRadar.Api.Caching;
using FlightRadar.Api.Watching;
using FlightRadar.Core.Flights;
using FlightRadar.Core.Geo;
using FlightRadar.Tests.Fakes;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using static FlightRadar.Tests.Fakes.FakeFlightDataProvider;

namespace FlightRadar.Tests.Watching;

/// <summary>Single ticks, run directly: fake provider, real in-memory HybridCache, in-memory registry.</summary>
public sealed class PollerTickTests
{
    // A browser's request, and the watched area it becomes: snapped, as WatchingFlightDataProvider marks it (the fake
    // provider has no radius limit). Areas in the registry are always snapped from real requests like this one.
    private static readonly GeoPoint DublinRequestCentre = new(53.4213, -6.2701);
    private const double DublinRequestRadiusKm = 10;
    private static readonly SearchArea Dublin =
        AreaSnapping.Snap(DublinRequestCentre, DublinRequestRadiusKm, double.PositiveInfinity);
    private static readonly SearchArea London = new(new GeoPoint(51.5, -0.1), 25);
    private static readonly SearchArea Paris = new(new GeoPoint(48.9, 2.4), 25);

    private readonly FakeTimeProvider clock = new(DateTimeOffset.UnixEpoch);
    private readonly InMemoryWatchedAreaRegistry liveViewers;
    private readonly HybridCache hybridCache;

    public PollerTickTests()
    {
        liveViewers = new InMemoryWatchedAreaRegistry(Options.Create(new WatchingOptions()), clock);
        var services = new ServiceCollection();
        services.AddHybridCache();
        hybridCache = services.BuildServiceProvider().GetRequiredService<HybridCache>();
    }

    private sealed class FixedSource(string name, params SearchArea[] areas) : IWatchedAreaSource
    {
        public string Name => name;
        public Exception? ExceptionToThrow { get; init; }

        public Task<IReadOnlyCollection<SearchArea>> GetWatchedAreasAsync(CancellationToken cancellationToken) =>
            ExceptionToThrow is null ? Task.FromResult<IReadOnlyCollection<SearchArea>>(areas) : throw ExceptionToThrow;
    }

    /// <summary>Records what was published, or throws like a backplane with Redis down.</summary>
    private sealed class RecordingPublisher : IFlightUpdatePublisher
    {
        public List<(SearchArea Area, int FlightCount)> Published { get; } = [];
        public Exception? ExceptionToThrow { get; init; }

        public Task PublishAsync(SearchArea area, IReadOnlyList<Flight> flights, CancellationToken cancellationToken)
        {
            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }
            lock (Published)
            {
                Published.Add((area, flights.Count));
            }
            return Task.CompletedTask;
        }
    }

    private readonly RecordingPublisher publisher = new();

    /// <summary>Claims only the areas in <paramref name="mine"/>; the rest "belong to another instance".</summary>
    private sealed class SomeClaims(params SearchArea[] mine) : IPollClaims
    {
        public Task<bool> TryClaimAsync(SearchArea area, CancellationToken cancellationToken) =>
            Task.FromResult(mine.Contains(area));
    }

    private CachingFlightDataProvider Cache(IFlightDataProvider inner) => new(
        inner, hybridCache, Options.Create(new FlightCacheOptions()), NullLogger<CachingFlightDataProvider>.Instance);

    private PollerTick Tick(
        IFlightDataProvider inner,
        IEnumerable<IWatchedAreaSource>? sources = null,
        IPollClaims? claims = null,
        int maxWatchedAreas = 50,
        IFlightUpdatePublisher? publishTo = null) => new(
            sources ?? [liveViewers],
            liveViewers,
            claims ?? new SingleInstancePollClaims(),
            Cache(inner),
            [publishTo ?? publisher],
            Options.Create(new WatchingOptions { MaxWatchedAreas = maxWatchedAreas }),
            Options.Create(new FlightCacheOptions()),
            NullLogger<PollerTick>.Instance);

    [Fact]
    public async Task Run_WatchedAreas_FetchesEachOnceAndFillsTheCache()
    {
        await liveViewers.MarkWatchedAsync(Dublin, default);
        await liveViewers.MarkWatchedAsync(London, default);
        var provider = new FakeFlightDataProvider(FlightAt("abc123", 53.4, -6.3));

        var result = await Tick(provider).RunAsync(default);

        Assert.Equal(new PollerTickResult(Watched: 2, Fetched: 2, ClaimedElsewhere: 0, Failed: 0, OverLimit: 0), result);
        Assert.Equal(2, provider.Calls.Count);

        // The browser's next request (snapped by the cache to the same area, so the same key) finds the poller's
        // entry: no call of its own.
        var request = new FakeFlightDataProvider();
        var flights = await Cache(request).GetFlightsAsync(DublinRequestCentre, DublinRequestRadiusKm, default);
        Assert.Empty(request.Calls);
        Assert.Equal("abc123", Assert.Single(flights).Hex);
    }

    [Fact]
    public async Task Run_FetchedArea_IsPublishedWithEverythingFetched()
    {
        await liveViewers.MarkWatchedAsync(Dublin, default);
        var provider = new FakeFlightDataProvider(FlightAt("a", 53.4, -6.3), FlightAt("b", 53.5, -6.3));

        await Tick(provider).RunAsync(default);

        Assert.Equal([(Dublin, 2)], publisher.Published);
    }

    [Fact]
    public async Task Run_AreaClaimedElsewhere_IsNotPublishedHere()
    {
        // The instance that fetched it publishes it; the backplane delivers it to subscribers everywhere.
        IWatchedAreaSource[] sources = [new FixedSource("browsers", Dublin, London)];

        await Tick(new FakeFlightDataProvider(), sources, new SomeClaims(London)).RunAsync(default);

        Assert.Equal([London], publisher.Published.Select(p => p.Area));
    }

    [Fact]
    public async Task Run_PublishFails_StillCountsAsFetchedAndCached()
    {
        await liveViewers.MarkWatchedAsync(Dublin, default);
        var broken = new RecordingPublisher { ExceptionToThrow = new InvalidOperationException("Redis is down") };

        var result = await Tick(new FakeFlightDataProvider(FlightAt("new", 53.4, -6.3)), publishTo: broken).RunAsync(default);

        Assert.Equal((1, 0, 1), (result.Fetched, result.Failed, result.PushFailed));
        // HTTP polling (the browsers' fallback) still gets the fresh data from the cache.
        var flights = await Cache(new FakeFlightDataProvider())
            .GetFlightsAsync(DublinRequestCentre, DublinRequestRadiusKm, default);
        Assert.Equal("new", Assert.Single(flights).Hex);
    }

    [Fact]
    public async Task Run_NothingWatched_FetchesNothing()
    {
        var provider = new FakeFlightDataProvider();

        var result = await Tick(provider).RunAsync(default);

        Assert.Equal(0, result.Watched);
        Assert.Empty(provider.Calls);
    }

    [Fact]
    public async Task Run_SameAreaFromTwoSources_FetchesItOnce()
    {
        // Step 9: a widget subscription and a browser on the same area cost one call, not two.
        var provider = new FakeFlightDataProvider();
        IWatchedAreaSource[] sources = [new FixedSource("browsers", Dublin), new FixedSource("widgets", Dublin, London)];

        var result = await Tick(provider, sources).RunAsync(default);

        Assert.Equal(2, result.Fetched);
        Assert.Equal(2, provider.Calls.Count);
    }

    [Fact]
    public async Task Run_AreaClaimedByAnotherInstance_SkipsIt()
    {
        var provider = new FakeFlightDataProvider();
        IWatchedAreaSource[] sources = [new FixedSource("browsers", Dublin, London)];

        var result = await Tick(provider, sources, new SomeClaims(Dublin)).RunAsync(default);

        Assert.Equal((1, 1), (result.Fetched, result.ClaimedElsewhere));
        Assert.Equal([(Dublin.Centre, Dublin.RadiusKm)], provider.Calls);
    }

    [Fact]
    public async Task Run_ProviderFails_CountsItAndKeepsTheOldEntry()
    {
        await liveViewers.MarkWatchedAsync(Dublin, default);
        await Tick(new FakeFlightDataProvider(FlightAt("old", 53.4, -6.3))).RunAsync(default);

        var failing = new FakeFlightDataProvider { ExceptionToThrow = new FlightDataUnavailableException("down") };
        var result = await Tick(failing).RunAsync(default);

        Assert.Equal((0, 1), (result.Fetched, result.Failed));
        var flights = await Cache(new FakeFlightDataProvider())
            .GetFlightsAsync(DublinRequestCentre, DublinRequestRadiusKm, default);
        Assert.Equal("old", Assert.Single(flights).Hex); // still served from the last good tick
    }

    [Fact]
    public async Task Run_OneSourceFails_UsesTheOthers()
    {
        var provider = new FakeFlightDataProvider();
        IWatchedAreaSource[] sources =
        [
            new FixedSource("broken") { ExceptionToThrow = new InvalidOperationException("Redis is down") },
            new FixedSource("browsers", London),
        ];

        var result = await Tick(provider, sources).RunAsync(default);

        Assert.Equal([(London.Centre, London.RadiusKm)], provider.Calls);
        Assert.Equal(1, result.Fetched);
    }

    [Fact]
    public async Task Run_MoreAreasThanTheLimit_FetchesOnlyTheLimit()
    {
        var provider = new FakeFlightDataProvider();
        IWatchedAreaSource[] sources = [new FixedSource("browsers", Dublin, London, Paris)];

        var result = await Tick(provider, sources, maxWatchedAreas: 2).RunAsync(default);

        Assert.Equal((3, 2, 1), (result.Watched, result.Fetched, result.OverLimit));
        Assert.Equal(2, provider.Calls.Count);
    }

    [Fact]
    public async Task Run_Always_PrunesExpiredLiveViewers()
    {
        await liveViewers.MarkWatchedAsync(Dublin, default);
        clock.Advance(TimeSpan.FromSeconds(31));

        await Tick(new FakeFlightDataProvider()).RunAsync(default);
        clock.Advance(TimeSpan.FromSeconds(-31)); // only what survived pruning would show up now

        Assert.Empty(await liveViewers.GetWatchedAreasAsync(default));
    }
}
