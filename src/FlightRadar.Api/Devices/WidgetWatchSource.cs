using FlightRadar.Api.Caching;
using FlightRadar.Api.Data;
using FlightRadar.Api.Watching;
using FlightRadar.Core.Geo;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FlightRadar.Api.Devices;

/// <summary>A watched widget location, and where to push when its aircraft changes (step 9d).</summary>
/// <param name="PushTokens">The FIDs of the devices showing it that can take pushes; may be empty.</param>
public sealed record WidgetPushTarget(Guid LocationId, GeoPoint Centre, double RadiusKm, IReadOnlyList<string> PushTokens);

/// <summary>
/// The poller's second source of areas (step 9c): saved locations with a home-screen widget, kept fresh around the
/// clock at <see cref="WatchingOptions.WidgetIntervalSeconds"/> (60 s), even with nobody looking. A chunk loader:
/// the area keeps ticking with no player in it, just more slowly.
/// </summary>
/// <remarks>
/// The poller asks every tick (10 s), so this hands each area out only when it's due. The list comes from Postgres,
/// reloaded on the same 60 s rhythm rather than every tick, and kept if a reload fails (the database being down
/// shouldn't stop widgets being watched). The "last handed out" times are this instance's own: with several
/// instances an area could be fetched a little more often than every 60 s, which is harmless (Azure runs one).
/// </remarks>
public sealed partial class WidgetWatchSource(
    IServiceScopeFactory scopes,
    IOptions<WatchingOptions> options,
    TimeProvider timeProvider,
    ILogger<WidgetWatchSource> logger) : IWatchedAreaSource
{
    /// <summary>
    /// Rows read per reload at most, oldest first. Far more than the caps can use; a limit so a flood of
    /// registrations can't make each reload read the whole table.
    /// </summary>
    private const int MaxRows = 2000;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<SearchArea, DateTimeOffset> _lastHandedOut = [];
    private IReadOnlyList<SearchArea> _areas = [];

    // Replaced whole on each reload, never changed in place: readers on other threads (the push publisher, during a
    // tick) see either the old map or the new one. Volatile: so they see the new one promptly.
    private volatile IReadOnlyDictionary<SearchArea, IReadOnlyList<WidgetPushTarget>> _targets =
        new Dictionary<SearchArea, IReadOnlyList<WidgetPushTarget>>();
    private DateTimeOffset? _loadedAt;

    public string Name => "widgets";

    /// <summary>The watched widget locations inside a (snapped) area, as of the last reload.</summary>
    public IReadOnlyList<WidgetPushTarget> TargetsIn(SearchArea area) =>
        _targets.TryGetValue(area, out var targets) ? targets : [];

    /// <summary>Every watched location's ID, as of the last reload.</summary>
    public IEnumerable<Guid> WatchedLocationIds => _targets.Values.SelectMany(targets => targets).Select(t => t.LocationId);

    public async Task<IReadOnlyCollection<SearchArea>> GetWatchedAreasAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (settings.MaxWidgetAreas == 0)
        {
            return [];
        }

        var every = TimeSpan.FromSeconds(settings.WidgetIntervalSeconds);
        var tick = TimeSpan.FromSeconds(settings.IntervalSeconds);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var now = timeProvider.GetUtcNow();
            if (_loadedAt is not { } loadedAt || now - loadedAt >= every - tick / 2)
            {
                _loadedAt = now; // also after a failure: try again next round, not every tick
                await ReloadAsync(settings, now, cancellationToken);
            }

            var due = WidgetWatchPlanner.Due(_areas, _lastHandedOut, now, every, tick);
            foreach (var area in due)
            {
                _lastHandedOut[area] = now;
            }
            foreach (var gone in _lastHandedOut.Keys.Except(_areas).ToList())
            {
                _lastHandedOut.Remove(gone); // no longer watched: don't keep its time for ever
            }
            return due;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task ReloadAsync(WatchingOptions settings, DateTimeOffset now, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // Snapped exactly as the cache snaps requests, so a widget and a browser on the same spot share a fetch.
            double maxRadiusKm = scope.ServiceProvider.GetRequiredService<CachingFlightDataProvider>().MaxRadiusKm;

            // Devices that stopped registering (uninstalled, or long switched off) are forgotten. One DELETE.
            var cutoff = now - TimeSpan.FromDays(settings.WidgetDeviceDays);
            await db.Devices.Where(device => device.LastSeenAt < cutoff).ExecuteDeleteAsync(cancellationToken);

            var rows = await (
                    from watch in db.DeviceWatches
                    join device in db.Devices on watch.DeviceId equals device.Id
                    join location in db.SavedLocations on watch.LocationId equals location.Id
                    orderby watch.CreatedAt
                    select new
                    {
                        device.OwnerId,
                        device.PushToken,
                        LocationId = location.Id,
                        location.Latitude,
                        location.Longitude,
                        location.RadiusKm,
                        watch.CreatedAt,
                    })
                .Take(MaxRows)
                .ToListAsync(cancellationToken);

            var watches = rows.Select(row => new
                {
                    Row = row,
                    Watch = new WidgetWatch(
                        row.OwnerId,
                        row.LocationId,
                        AreaSnapping.Snap(new GeoPoint(row.Latitude, row.Longitude), row.RadiusKm, maxRadiusKm),
                        row.CreatedAt),
                })
                .ToList();
            var plan = WidgetWatchPlanner.Choose(
                watches.Select(w => w.Watch), settings.WidgetLocationsPerOwner, settings.MaxWidgetAreas);

            _areas = plan.Areas;
            // Push targets: each chosen location once, with the FIDs of every device showing it.
            _targets = watches
                .Where(w => plan.Locations.Contains(w.Watch.LocationId))
                .GroupBy(w => w.Watch.LocationId)
                .Select(group =>
                {
                    var first = group.First();
                    return (first.Watch.Area, Target: new WidgetPushTarget(
                        group.Key,
                        new GeoPoint(first.Row.Latitude, first.Row.Longitude),
                        first.Row.RadiusKm,
                        group.Select(w => w.Row.PushToken).OfType<string>().Distinct().ToList()));
                })
                .GroupBy(t => t.Area)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<WidgetPushTarget>)g.Select(t => t.Target).ToList());
            LogLoaded(logger, rows.Count, plan.Areas.Count, plan.LocationsOverOwnerLimit);
            if (plan.AreasOverLimit > 0)
            {
                LogOverLimit(logger, plan.AreasOverLimit, settings.MaxWidgetAreas);
            }
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Keep watching what we had. Warning: once a minute at most, while the database is unreachable.
            LogLoadFailed(logger, exception.Message);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Widget watches: {Watches} rows, {Areas} areas watched, {OverOwnerLimit} locations over their owner's limit")]
    private static partial void LogLoaded(ILogger logger, int watches, int areas, int overOwnerLimit);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "{Count} widget areas over the limit of {Limit}: those widgets refresh on their own schedule only")]
    private static partial void LogOverLimit(ILogger logger, int count, int limit);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't reload widget watches; keeping the last list: {Reason}")]
    private static partial void LogLoadFailed(ILogger logger, string reason);
}
