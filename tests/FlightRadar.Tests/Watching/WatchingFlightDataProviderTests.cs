using FlightRadar.Api.Watching;
using FlightRadar.Core.Geo;
using FlightRadar.Tests.Fakes;

namespace FlightRadar.Tests.Watching;

public sealed class WatchingFlightDataProviderTests
{
    private sealed class RecordingRegistry : IWatchedAreaRegistry
    {
        public List<SearchArea> Marked { get; } = [];

        public string Name => "test";

        public Task MarkWatchedAsync(SearchArea area, CancellationToken cancellationToken)
        {
            Marked.Add(area);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyCollection<SearchArea>> GetWatchedAreasAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<SearchArea>>(Marked);

        public Task PruneExpiredAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [Fact]
    public async Task GetFlights_Always_MarksTheSnappedAreaAndPassesTheRequestOnUnchanged()
    {
        var inner = new FakeFlightDataProvider { MaxRadiusKm = 463 };
        var registry = new RecordingRegistry();
        var provider = new WatchingFlightDataProvider(inner, registry);
        var centre = new GeoPoint(53.4213, -6.2701);

        await provider.GetFlightsAsync(centre, 10, default);

        // The area the cache will use for its key, so the poller refreshes exactly the entry these requests read.
        Assert.Equal([AreaSnapping.Snap(centre, 10, 463)], registry.Marked);
        // The request itself is untouched: the cache below does its own snapping.
        Assert.Equal([(centre, 10.0)], inner.Calls);
    }
}
