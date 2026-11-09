using FlightRadar.Core.Enrichment;
using Microsoft.Extensions.Options;

namespace FlightRadar.Api.Routes;

/// <summary>
/// Looks up the routes for a batch of callsigns (one /api/flights response) within a short time budget.
/// </summary>
/// <remarks>
/// <para>
/// Every lookup starts at once (bounded by the HttpClient's concurrency limit); then we wait for all of them or for
/// <see cref="RouteOptions.WaitMilliseconds"/>, whichever comes first, and return what's ready. Cached routes
/// are ready almost instantly, so a warm cache returns everything. Lookups still running aren't cancelled: they
/// finish in the background and fill the cache, so the browser's next poll gets them. Like streaming in a game:
/// draw the level with what's loaded, and pop in the rest as it arrives, rather than freezing the frame.
/// </para>
/// <para>
/// Routes are extras, so they never fail a request: a lookup that fails is logged and that flight simply has no
/// route this time.
/// </para>
/// </remarks>
public sealed partial class RouteLookup(
    IRouteProvider routes,
    IOptions<RouteOptions> options,
    TimeProvider timeProvider,
    ILogger<RouteLookup> logger)
{
    /// <summary>Routes for those of <paramref name="callsigns"/> that are airline callsigns and are ready in time.</summary>
    /// <returns>Keyed by callsign (case-insensitive). Callsigns without a (ready) route are absent.</returns>
    public async Task<IReadOnlyDictionary<string, FlightRoute>> GetRoutesAsync(
        IEnumerable<string?> callsigns, CancellationToken cancellationToken)
    {
        // Only airline callsigns ("EIN123"): the database is of scheduled flights, so looking up registrations
        // ("EIGWY") would just be a 404 each.
        var lookups = callsigns
            .Select(callsign => callsign?.Trim().ToUpperInvariant())
            .OfType<string>() // drops nulls, and tells the compiler so
            .Where(callsign => AirlineDirectory.AirlineCode(callsign) is not null)
            .Distinct()
            .ToDictionary(callsign => callsign, LookUpAsync);

        if (lookups.Count == 0)
        {
            return EmptyRoutes;
        }

        // Whichever finishes first: every lookup, or the budget. The linked token also ends the wait if the caller
        // goes away, and lets us stop the budget's timer when the lookups win.
        using var stopWaiting = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var all = Task.WhenAll(lookups.Values);
        var budget = Task.Delay(TimeSpan.FromMilliseconds(options.Value.WaitMilliseconds), timeProvider, stopWaiting.Token);
        await Task.WhenAny(all, budget);
        await stopWaiting.CancelAsync();
        cancellationToken.ThrowIfCancellationRequested(); // the caller went away: no response to build

        // Only finished lookups. The && order matters: reading .Result of an unfinished task would *block* until it
        // finishes, which is exactly the wait we're avoiding. (A property pattern doesn't promise an order.)
        var ready = new Dictionary<string, FlightRoute>(StringComparer.OrdinalIgnoreCase);
        foreach (var (callsign, lookup) in lookups)
        {
            if (lookup.IsCompletedSuccessfully && lookup.Result is { } route)
            {
                ready[callsign] = route;
            }
        }

        return ready;
    }

    private static readonly IReadOnlyDictionary<string, FlightRoute> EmptyRoutes =
        new Dictionary<string, FlightRoute>(StringComparer.OrdinalIgnoreCase);

    /// <summary>One lookup that never throws: a failure becomes "no route this time".</summary>
    /// <remarks>
    /// CancellationToken.None, not the request's token: the lookup should outlive this request (that's how the
    /// next poll gets it), and the HttpClient's own timeout bounds it. Catching here also means a failure that
    /// finishes after we stopped waiting is still observed, rather than surfacing as an unobserved task exception.
    /// </remarks>
    private async Task<FlightRoute?> LookUpAsync(string callsign)
    {
        try
        {
            return await routes.GetRouteAsync(callsign, CancellationToken.None);
        }
        catch (Exception exception)
        {
            LogLookupFailed(callsign, exception.Message);
            return null;
        }
    }

    // Debug, not Warning: when the source is down every airline callsign fails on every poll, and the resilience
    // pipeline already logs the underlying HTTP failures.
    [LoggerMessage(Level = LogLevel.Debug, Message = "Route lookup for {Callsign} failed: {Reason}")]
    private partial void LogLookupFailed(string callsign, string reason);
}
