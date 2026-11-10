using System.Globalization;
using FlightRadar.Api.Flights;
using FlightRadar.Api.Watching;
using FlightRadar.Core.Flights;
using FlightRadar.Core.Geo;
using Microsoft.AspNetCore.SignalR;

namespace FlightRadar.Api.Streaming;

/// <summary>
/// Live flights over a WebSocket (step 7b). A browser calls <see cref="Watch"/> with its search; it joins that area's
/// group and gets the current flights straight back. From then on, whenever the poller refreshes the area it pushes
/// the new flights to the whole group (<see cref="FlightsUpdatedMethod"/>), so the browser no longer needs to poll.
/// </summary>
/// <remarks>
/// <b>Groups</b> are SignalR's interest management: one per snapped area (the same area the cache and the poller
/// use), so one push reaches everyone watching it, built once however many are. Browsers with different exact
/// circles share a group when they snap to the same area; each trims the data to its own circle, as it already does
/// with the HTTP response.
/// <para>
/// A hub instance lives for one call only (like a controller per request), so nothing is stored on it: per-connection
/// state lives in <see cref="FlightSubscriptions"/>.
/// </para>
/// </remarks>
public sealed partial class FlightsHub(
    FlightSubscriptions subscriptions,
    NearbyFlightsService nearbyFlights,
    FlightsResponseBuilder responses,
    IFlightDataProvider provider,
    ILogger<FlightsHub> logger) : Hub
{
    public const string Path = "/hubs/flights";

    /// <summary>The client-side method name pushes are sent to: <c>connection.on("flightsUpdated", …)</c>.</summary>
    public const string FlightsUpdatedMethod = "flightsUpdated";

    /// <summary>The SignalR group for an area. Shared with the poller, which pushes to it.</summary>
    public static string GroupFor(SearchArea area) => "area:" + WatchedAreaKey.Format(area);

    /// <summary>
    /// Watch this circle instead of whatever the connection watched before. Returns the flights in it now, like
    /// GET /api/flights; updates follow as pushes. Errors reach the client as a HubException message.
    /// </summary>
    public async Task<FlightsResponse> Watch(double lat, double lon, double radiusKm)
    {
        var errors = FlightsEndpoints.Validate(lat, lon, radiusKm);
        if (errors.Count > 0)
        {
            throw new HubException(string.Join(" ", errors.Values.SelectMany(messages => messages)));
        }

        var centre = new GeoPoint(lat, lon);
        var area = AreaSnapping.Snap(centre, radiusKm, provider.MaxRadiusKm);

        var (allowed, previous, retryAfter) = subscriptions.TryWatch(Context.ConnectionId, area);
        if (!allowed)
        {
            LogThrottled(logger, Context.ConnectionId);
            throw new HubException(string.Create(CultureInfo.InvariantCulture,
                $"Too many area changes: try again in {Math.Max(0.1, retryAfter.TotalSeconds):0.0} s."));
        }

        // Join the new group before leaving the old one: a push arriving in between then reaches this connection
        // twice (harmless) rather than not at all.
        if (previous != area)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, GroupFor(area), Context.ConnectionAborted);
            if (previous is { } old)
            {
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupFor(old), Context.ConnectionAborted);
            }
        }

        try
        {
            // Through the usual provider chain: watching (marks the area for the poller), cache, providers.
            var flights = await nearbyFlights.GetNearbyFlightsAsync(centre, radiusKm, Context.ConnectionAborted);
            return await responses.BuildAsync(centre, radiusKm, flights, Context.ConnectionAborted);
        }
        catch (FlightDataUnavailableException)
        {
            // Still subscribed: pushes start as soon as the poller manages a fetch. Plain text for the client, like
            // the HTTP API's 502 (exception messages can hold upstream details).
            throw new HubException("Flight data unavailable right now; updates will follow when it's back.");
        }
    }

    /// <summary>Stop receiving pushes (the tab was hidden, say). The connection stays open for the next Watch.</summary>
    public async Task Unwatch()
    {
        if (subscriptions.Unwatch(Context.ConnectionId) is { } previous)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupFor(previous), Context.ConnectionAborted);
        }
    }

    public override Task OnConnectedAsync()
    {
        LogConnected(logger, Context.ConnectionId);
        return base.OnConnectedAsync();
    }

    /// <summary>SignalR removes the connection from its groups itself; we forget its subscription and throttle.</summary>
    public override Task OnDisconnectedAsync(Exception? exception)
    {
        subscriptions.Remove(Context.ConnectionId);
        LogDisconnected(logger, Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Flights hub: {ConnectionId} connected")]
    private static partial void LogConnected(ILogger logger, string connectionId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Flights hub: {ConnectionId} disconnected")]
    private static partial void LogDisconnected(ILogger logger, string connectionId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Flights hub: {ConnectionId} changed area too often")]
    private static partial void LogThrottled(ILogger logger, string connectionId);
}
