using System.Globalization;
using System.Text.Json;
using FlightRadar.Core.Flights;
using FlightRadar.Core.Geo;

namespace FlightRadar.Api.Providers.AdsbLol;

/// <summary>Fetches aircraft from adsb.lol's free, keyless API: GET /v2/point/{lat}/{lon}/{radiusNm}.</summary>
public sealed class AdsbLolFlightDataProvider(HttpClient httpClient) : IFlightDataProvider
{
    /// <summary>adsb.lol rejects larger radii.</summary>
    public const int MaxRadiusNm = 250;

    public async Task<IReadOnlyList<Flight>> GetFlightsAsync(
        GeoPoint centre, double radiusKm, CancellationToken cancellationToken)
    {
        // Round up so aircraft near the edge aren't missed; the caller trims to the exact km radius.
        var radiusNm = Math.Clamp((int)Math.Ceiling(Units.KmToNauticalMiles(radiusKm)), 1, MaxRadiusNm);

        // InvariantCulture: on a machine set to e.g. German, 53.42 would otherwise format as "53,42".
        var path = string.Create(
            CultureInfo.InvariantCulture, $"v2/point/{centre.Latitude}/{centre.Longitude}/{radiusNm}");

        AdsbLolResponse? response;
        try
        {
            response = await httpClient.GetFromJsonAsync<AdsbLolResponse>(path, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // The caller cancelled (e.g. the browser went away). Not an upstream failure.
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            // HttpRequestException: network error or non-2xx status. JsonException: unexpected body.
            // TaskCanceledException without caller cancellation: HttpClient.Timeout elapsed.
            throw new FlightDataUnavailableException("adsb.lol request failed.", ex);
        }

        if (response?.Aircraft is null)
        {
            return [];
        }

        var snapshotTime = DateTimeOffset.FromUnixTimeMilliseconds(response.Now);
        return response.Aircraft
            .Select(aircraft => ToFlight(aircraft, snapshotTime))
            .OfType<Flight>() // drops the nulls
            .ToList();
    }

    private static Flight? ToFlight(AdsbLolAircraft aircraft, DateTimeOffset snapshotTime)
    {
        if (string.IsNullOrWhiteSpace(aircraft.Hex) || aircraft.Lat is not { } lat || aircraft.Lon is not { } lon)
        {
            return null; // No position (or no ID): nothing to put on a map.
        }

        return new Flight
        {
            Hex = aircraft.Hex,
            Callsign = NullIfBlank(aircraft.Flight),
            Registration = NullIfBlank(aircraft.Registration),
            AircraftType = NullIfBlank(aircraft.AircraftType),
            Position = new GeoPoint(lat, lon),
            PositionTimestamp = snapshotTime - TimeSpan.FromSeconds(aircraft.SeenPos ?? 0),
            AltitudeFt = aircraft.AltBaro.Feet,
            OnGround = aircraft.AltBaro.OnGround,
            GroundSpeedKt = aircraft.GroundSpeed,
            TrackDeg = aircraft.Track,
            // Barometric rate is the standard; some transponders only send the GPS-derived one.
            VerticalRateFpm = RoundToInt(aircraft.BaroRate ?? aircraft.GeomRate),
        };
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static int? RoundToInt(double? value) =>
        value is { } v ? (int)Math.Round(v) : null;
}
