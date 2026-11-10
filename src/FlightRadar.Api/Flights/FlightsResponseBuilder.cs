using FlightRadar.Api.Routes;
using FlightRadar.Core.Enrichment;
using FlightRadar.Core.Flights;
using FlightRadar.Core.Geo;

namespace FlightRadar.Api.Flights;

/// <summary>
/// Turns aircraft in a circle into the public <see cref="FlightsResponse"/>: airline names, routes (within their
/// short time budget), airport guesses. One place for it, used by GET /api/flights, by the hub's first answer to a
/// subscription, and by the poller's pushes (step 7b), so every way of getting flights gives the same JSON.
/// </summary>
public sealed class FlightsResponseBuilder(
    AirlineDirectory airlines,
    AirportDirectory airports,
    RouteLookup routeLookup,
    TimeProvider timeProvider)
{
    public async Task<FlightsResponse> BuildAsync(
        GeoPoint centre, double radiusKm, IReadOnlyList<NearbyFlight> flights, CancellationToken cancellationToken)
    {
        // Routes for the flights inside the circle only, and only those ready within a short budget (the rest
        // arrive on a later poll). Never fails.
        var routes = await routeLookup.GetRoutesAsync(flights.Select(nearby => nearby.Flight.Callsign), cancellationToken);

        return new FlightsResponse(
            centre.Latitude,
            centre.Longitude,
            radiusKm,
            flights.Count,
            timeProvider.GetUtcNow(),
            flights.Select(nearby => FlightDto.From(nearby, airlines, routes, airports)).ToList());
    }
}
