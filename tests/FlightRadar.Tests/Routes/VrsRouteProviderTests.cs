using System.Net;
using FlightRadar.Api.Routes;
using FlightRadar.Core.Enrichment;
using FlightRadar.Core.Geo;
using FlightRadar.Tests.Fakes;

namespace FlightRadar.Tests.Routes;

public sealed class VrsRouteProviderTests
{
    /// <summary>The real file for EIN123, trimmed (https://vrs-standing-data.adsb.lol/routes/EI/EIN123.json).</summary>
    private const string Ein123 = """
        {
            "callsign": "EIN123", "number": "123", "airline_code": "EIN",
            "airport_codes": "EIDW-KORD", "_airport_codes_iata": "DUB-ORD",
            "_airports": [
                { "name": "Dublin Airport", "icao": "EIDW", "iata": "DUB", "location": "Dublin", "countryiso2": "IE",
                  "lat": 53.421299, "lon": -6.27007, "alt_feet": 242.0, "alt_meters": 73.76 },
                { "name": "Chicago O'Hare International Airport", "icao": "KORD", "iata": "ORD", "location": "Chicago",
                  "countryiso2": "US", "lat": 41.9786, "lon": -87.9048, "alt_feet": 672.0, "alt_meters": 204.83 }
            ]
        }
        """;

    private static VrsRouteProvider Create(FakeHttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://vrs-standing-data.adsb.lol/") });

    [Fact]
    public async Task GetRouteAsync_Always_RequestsTheCallsignsFile()
    {
        var handler = FakeHttpMessageHandler.Json(Ein123);

        await Create(handler).GetRouteAsync("EIN123", CancellationToken.None);

        // Files are grouped in folders by the callsign's first two letters.
        Assert.Equal("https://vrs-standing-data.adsb.lol/routes/EI/EIN123.json",
            Assert.Single(handler.Requests).RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task GetRouteAsync_KnownRoute_MapsTheAirportsInOrder()
    {
        var route = await Create(FakeHttpMessageHandler.Json(Ein123)).GetRouteAsync("EIN123", CancellationToken.None);

        Assert.NotNull(route);
        Assert.Equal(
            [
                new RouteAirport("EIDW", "DUB", "Dublin Airport", "Dublin", "IE", new GeoPoint(53.421299, -6.27007)),
                new RouteAirport("KORD", "ORD", "Chicago O'Hare International Airport", "Chicago", "US", new GeoPoint(41.9786, -87.9048)),
            ],
            route.Airports);
    }

    [Fact]
    public async Task GetRouteAsync_RouteWithAStop_KeepsEveryAirport()
    {
        var json = """
            { "callsign": "TLK11", "_airports": [
                { "icao": "LEPA", "name": "Palma", "lat": 39.55, "lon": 2.73 },
                { "icao": "OTHH", "name": "Doha", "lat": 25.27, "lon": 51.61 },
                { "icao": "EGLL", "name": "Heathrow", "lat": 51.47, "lon": -0.46 } ] }
            """;

        var route = await Create(FakeHttpMessageHandler.Json(json)).GetRouteAsync("TLK11", CancellationToken.None);

        Assert.Equal(["LEPA", "OTHH", "EGLL"], route!.Airports.Select(a => a.Icao));
    }

    [Fact]
    public async Task GetRouteAsync_NotFound_ReturnsNull()
    {
        // The database has no route for this callsign: a normal answer, not an error.
        var route = await Create(FakeHttpMessageHandler.Status(HttpStatusCode.NotFound))
            .GetRouteAsync("EIN999", CancellationToken.None);

        Assert.Null(route);
    }

    [Theory]
    [InlineData("""{ "callsign": "EIN1" }""")]                                        // no airports
    [InlineData("""{ "_airports": [ { "icao": "EIDW", "lat": 53.4, "lon": -6.3 } ] }""")] // only one
    [InlineData("""{ "_airports": [ { "icao": "EIDW", "lat": 53.4, "lon": -6.3 }, { "icao": "KORD" } ] }""")] // no position
    [InlineData("""{ "_airports": [ { "icao": "EIDW", "lat": 53.4, "lon": -6.3 }, { "lat": 41.9, "lon": -87.9 } ] }""")] // no code
    public async Task GetRouteAsync_IncompleteRoute_ReturnsNull(string json)
    {
        var route = await Create(FakeHttpMessageHandler.Json(json)).GetRouteAsync("EIN1", CancellationToken.None);

        Assert.Null(route);
    }

    [Fact]
    public async Task GetRouteAsync_BlankOptionalFields_BecomeNull()
    {
        var json = """
            { "_airports": [
                { "icao": "EIDW", "iata": "", "name": "Dublin", "location": " ", "lat": 53.4, "lon": -6.3 },
                { "icao": "EGLL", "lat": 51.47, "lon": -0.46 } ] }
            """;

        var route = await Create(FakeHttpMessageHandler.Json(json)).GetRouteAsync("EIN1", CancellationToken.None);

        var (dublin, heathrow) = (route!.Airports[0], route.Airports[1]);
        Assert.Null(dublin.Iata);
        Assert.Null(dublin.City);
        Assert.Equal("EGLL", heathrow.Name); // no name: fall back to the code rather than show nothing
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task GetRouteAsync_ErrorStatus_Throws(HttpStatusCode status)
    {
        // "Couldn't ask" must not look like "no route", or the cache would store it as an answer for an hour.
        await Assert.ThrowsAsync<HttpRequestException>(
            () => Create(FakeHttpMessageHandler.Status(status)).GetRouteAsync("EIN1", CancellationToken.None));
    }
}
