using System.Net;
using FlightRadar.Core.Enrichment;
using FlightRadar.Core.Geo;

namespace FlightRadar.Api.Routes;

/// <summary>
/// Routes from Virtual Radar Server's community route database (CC0), as one static JSON file per callsign served
/// by adsb.lol's mirror: <c>routes/{first two letters}/{callsign}.json</c>, 404 when there's no route.
/// </summary>
/// <remarks>
/// adsb.lol's own route API (<c>/api/0/routeset</c>) now returns an empty body and its single-route endpoint
/// redirects to these files, marked deprecated, so we read the files directly. Only the callsign is sent: never
/// the user's location.
/// </remarks>
public sealed class VrsRouteProvider(HttpClient httpClient) : IRouteProvider
{
    public async Task<FlightRoute?> GetRouteAsync(string callsign, CancellationToken cancellationToken)
    {
        // Escaped anyway, although callers only pass airline callsigns (letters and digits).
        var path = $"routes/{Uri.EscapeDataString(callsign[..2])}/{Uri.EscapeDataString(callsign)}.json";

        using var response = await httpClient.GetAsync(path, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null; // the database has no route for this callsign: an answer, not a failure
        }

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<VrsRouteResponse>(cancellationToken);
        return ToRoute(body);
    }

    private static FlightRoute? ToRoute(VrsRouteResponse? body)
    {
        // A route needs at least an origin and a destination, all with positions (the plausibility check uses
        // them). Anything less isn't usable, so it's treated as "no route" rather than shown half-complete.
        if (body?.Airports is not { Count: >= 2 } wireAirports)
        {
            return null;
        }

        var airports = new List<RouteAirport>(wireAirports.Count);
        foreach (var wireAirport in wireAirports)
        {
            if (ToAirport(wireAirport) is not { } airport)
            {
                return null;
            }

            airports.Add(airport);
        }

        return new FlightRoute(airports);
    }

    private static RouteAirport? ToAirport(VrsAirport airport)
    {
        if (string.IsNullOrWhiteSpace(airport.Icao) || airport.Lat is not { } lat || airport.Lon is not { } lon)
        {
            return null;
        }

        return new RouteAirport(
            Icao: airport.Icao.Trim(),
            Iata: NullIfBlank(airport.Iata),
            Name: NullIfBlank(airport.Name) ?? airport.Icao.Trim(),
            City: NullIfBlank(airport.Location),
            CountryCode: NullIfBlank(airport.CountryIso2),
            Position: new GeoPoint(lat, lon));
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
