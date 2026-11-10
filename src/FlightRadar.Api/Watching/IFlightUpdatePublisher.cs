using FlightRadar.Core.Flights;
using FlightRadar.Core.Geo;

namespace FlightRadar.Api.Watching;

/// <summary>
/// Where the poller sends each area's fresh flights after refreshing it (step 7b). The poller only knows "these
/// flights for this area are new"; who receives them, and how, is someone else's business: SignalR's hub today
/// (<c>Streaming/HubFlightUpdatePublisher</c>), push notifications to the Android widget perhaps in step 9.
/// </summary>
public interface IFlightUpdatePublisher
{
    /// <param name="area">The snapped area that was refreshed.</param>
    /// <param name="flights">Everything the provider returned for it (not yet trimmed to the circle).</param>
    Task PublishAsync(SearchArea area, IReadOnlyList<Flight> flights, CancellationToken cancellationToken);
}
