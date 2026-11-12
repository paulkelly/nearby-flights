using System.Globalization;
using System.Text;
using System.Text.Json;
using FlightRadar.Api.Data;
using FlightRadar.Api.Devices;
using FlightRadar.Api.Flights;
using FlightRadar.Api.Watching;
using FlightRadar.Core.Flights;
using FlightRadar.Core.Geo;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using HttpJsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

namespace FlightRadar.Api.Pushes;

/// <summary>
/// After the poller refreshes an area (step 9d): for each watched widget location inside it, works out the aircraft
/// its widget would show, and when that changed (another aircraft, or none, or one again) pushes it to the devices
/// showing that location. Runs next to the SignalR publisher; the poller doesn't know either exists.
/// </summary>
/// <remarks>
/// The push carries the aircraft as the API's own <see cref="FlightDto"/> JSON (airline, route…), so the app can
/// update the widget without asking. Push data is strings only, 4 KB in all: an unusually large one goes without
/// the aircraft, and the app fetches instead.
/// </remarks>
public sealed partial class WidgetPushPublisher(
    WidgetWatchSource widgets,
    WidgetNearestState state,
    IPushSender sender,
    FlightsResponseBuilder responses,
    AppDbContext db,
    IOptions<HttpJsonOptions> jsonOptions,
    TimeProvider timeProvider,
    ILogger<WidgetPushPublisher> logger) : IFlightUpdatePublisher
{
    /// <summary>FCM's limit is 4,096 bytes of data; room left for keys and FCM's own fields.</summary>
    private const int MaxDataBytes = 3500;

    public async Task PublishAsync(SearchArea area, IReadOnlyList<Flight> flights, CancellationToken cancellationToken)
    {
        var targets = widgets.TargetsIn(area);
        state.KeepOnly(widgets.WatchedLocationIds);
        if (targets.Count == 0)
        {
            return;
        }

        var messages = new List<PushMessage>();
        int changed = 0;
        foreach (var target in targets)
        {
            var inCircle = NearbyFlightsService.Within(target.Centre, target.RadiusKm, flights);
            var (hasChanged, chosen) = state.Update(target.LocationId, inCircle);
            if (!hasChanged)
            {
                continue;
            }
            changed++;
            if (target.PushTokens.Count == 0)
            {
                continue; // nobody to tell (devices without push addresses), but the state above still moved on
            }

            var data = await BuildDataAsync(target, chosen, inCircle.Count, cancellationToken);
            messages.AddRange(target.PushTokens.Select(token =>
                new PushMessage(token, CollapseKey(target.LocationId), data)));
        }

        if (messages.Count == 0)
        {
            return;
        }

        var outcomes = await sender.SendAsync(messages, cancellationToken);
        var gone = messages.Zip(outcomes).Where(pair => pair.Second == PushOutcome.TokenGone)
            .Select(pair => pair.First.Token).Distinct().ToList();
        if (gone.Count > 0)
        {
            // Those addresses will never work again: stop using them. The devices stay registered (still watched,
            // their widgets still refresh); the app sends a new address when it gets one.
            await db.Devices.Where(device => device.PushToken != null && gone.Contains(device.PushToken))
                .ExecuteUpdateAsync(set => set.SetProperty(device => device.PushToken, (string?)null), cancellationToken);
        }

        int sent = outcomes.Count(outcome => outcome == PushOutcome.Sent);
        int failed = outcomes.Count(outcome => outcome == PushOutcome.Failed);
        if (outcomes.All(outcome => outcome == PushOutcome.NotConfigured))
        {
            LogNotConfigured(logger, changed);
        }
        else
        {
            LogPushed(logger, changed, sent, gone.Count, failed);
        }
    }

    /// <summary>One collapse key per location: an offline phone gets only the latest news about each.</summary>
    public static string CollapseKey(Guid locationId) => $"nearest-{locationId:N}";

    private async Task<IReadOnlyDictionary<string, string>> BuildDataAsync(
        WidgetPushTarget target, NearbyFlight? chosen, int count, CancellationToken cancellationToken)
    {
        var data = new Dictionary<string, string>
        {
            ["type"] = "nearest",
            ["locationId"] = target.LocationId.ToString(),
            ["fetchedAt"] = timeProvider.GetUtcNow().ToString("O", CultureInfo.InvariantCulture),
            ["count"] = count.ToString(CultureInfo.InvariantCulture), // aircraft in the circle, in the air or not
        };
        if (chosen is null)
        {
            return data; // nothing near: "clear skies"
        }

        // The same JSON as GET /api/flights gives for this aircraft (names, route, airport guess).
        var response = await responses.BuildAsync(target.Centre, target.RadiusKm, [chosen], cancellationToken);
        var flight = JsonSerializer.Serialize(response.Flights[0], jsonOptions.Value.SerializerOptions);
        if (Encoding.UTF8.GetByteCount(flight) + 200 <= MaxDataBytes)
        {
            data["flight"] = flight;
        }
        else
        {
            data["flightOmitted"] = "true"; // too big for a push: the app fetches it
        }
        return data;
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Widget pushes: {Changed} locations changed, {Sent} sent, {Gone} dead addresses cleared, {Failed} failed")]
    private static partial void LogPushed(ILogger logger, int changed, int sent, int gone, int failed);

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Widget pushes: {Changed} locations changed, not sent (Firebase isn't configured)")]
    private static partial void LogNotConfigured(ILogger logger, int changed);
}
