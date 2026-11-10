using FlightRadar.Api.Flights;
using FlightRadar.Api.Watching;
using FlightRadar.Core.Flights;
using FlightRadar.Core.Geo;
using Microsoft.AspNetCore.SignalR;

namespace FlightRadar.Api.Streaming;

/// <summary>
/// Pushes a refreshed area to everyone watching it: builds the <see cref="FlightsResponse"/> once (airline names,
/// routes, airport guesses) and sends it to the area's group. Browsers trim it to their own exact circle.
/// </summary>
/// <remarks>
/// <see cref="IHubContext{THub}"/> is how code outside a hub talks to its clients. With one instance, SignalR
/// delivers to the group's connections directly. With Redis configured, every send goes through the Redis backplane
/// (pub/sub), which hands it to each instance with connections in the group: the poller on instance A reaches
/// browsers connected to instance B.
/// <para>
/// Pushed to every refreshed area, whether or not anyone here subscribes: a group's members may all be on another
/// instance, which only the backplane knows. An empty group costs one small message.
/// </para>
/// </remarks>
public sealed class HubFlightUpdatePublisher(IHubContext<FlightsHub> hub, FlightsResponseBuilder responses)
    : IFlightUpdatePublisher
{
    public async Task PublishAsync(SearchArea area, IReadOnlyList<Flight> flights, CancellationToken cancellationToken)
    {
        var nearby = NearbyFlightsService.Within(area.Centre, area.RadiusKm, flights);
        var response = await responses.BuildAsync(area.Centre, area.RadiusKm, nearby, cancellationToken);
        await hub.Clients.Group(FlightsHub.GroupFor(area))
            .SendAsync(FlightsHub.FlightsUpdatedMethod, response, cancellationToken);
    }
}
