using System.Text.Json.Serialization;

namespace FlightRadar.Api.Routes;

// Wire format of one route file, e.g. https://vrs-standing-data.adsb.lol/routes/EI/EIN123.json:
//   { "callsign": "EIN123", "airline_code": "EIN", "airport_codes": "EIDW-KORD", "_airport_codes_iata": "DUB-ORD",
//     "_airports": [ { "name": "Dublin Airport", "icao": "EIDW", "iata": "DUB", "location": "Dublin",
//                      "countryiso2": "IE", "lat": 53.421299, "lon": -6.27007, ... }, ... ] }
// Internal: mapped to FlightRoute straight away, so this shape never leaves the provider.

internal sealed record VrsRouteResponse
{
    [JsonPropertyName("callsign")] public string? Callsign { get; init; }

    /// <summary>The airports in order, origin first. Routes with stops have more than two.</summary>
    [JsonPropertyName("_airports")] public IReadOnlyList<VrsAirport>? Airports { get; init; }
}

internal sealed record VrsAirport
{
    [JsonPropertyName("icao")] public string? Icao { get; init; }
    [JsonPropertyName("iata")] public string? Iata { get; init; }
    [JsonPropertyName("name")] public string? Name { get; init; }
    [JsonPropertyName("location")] public string? Location { get; init; }
    [JsonPropertyName("countryiso2")] public string? CountryIso2 { get; init; }
    [JsonPropertyName("lat")] public double? Lat { get; init; }
    [JsonPropertyName("lon")] public double? Lon { get; init; }
}
