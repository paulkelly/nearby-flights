using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using FlightRadar.Core.Flights;
using FlightRadar.Core.Geo;
using Polly;

namespace FlightRadar.Api.Providers.AdsbLol;

/// <summary>Fetches aircraft from adsb.lol's free, keyless API: GET /v2/point/{lat}/{lon}/{radiusNm}.</summary>
public sealed partial class AdsbLolFlightDataProvider(HttpClient httpClient, ILogger<AdsbLolFlightDataProvider> logger)
    : IFlightDataProvider
{
    /// <summary>adsb.lol rejects larger radii.</summary>
    public const int MaxRadiusNm = 250;

    /// <summary>250 nm ≈ 463 km. Derived, so <see cref="MaxRadiusNm"/> stays the single authority.</summary>
    public double MaxRadiusKm => MaxRadiusNm * Units.KmPerNauticalMile;

    public async Task<IReadOnlyList<Flight>> GetFlightsAsync(
        GeoPoint centre, double radiusKm, CancellationToken cancellationToken)
    {
        // Round up so aircraft near the edge aren't missed; the caller trims to the exact km radius.
        var radiusNm = Math.Clamp((int)Math.Ceiling(Units.KmToNauticalMiles(radiusKm)), 1, MaxRadiusNm);

        // InvariantCulture: on a machine set to e.g. German, 53.42 would otherwise format as "53,42".
        var path = string.Create(
            CultureInfo.InvariantCulture, $"v2/point/{centre.Latitude}/{centre.Longitude}/{radiusNm}");

        var started = Stopwatch.GetTimestamp();
        AdsbLolResponse? response;
        try
        {
            response = await httpClient.GetFromJsonAsync<AdsbLolResponse>(path, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // The caller cancelled (e.g. the browser went away). Not an upstream failure.
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException
                                       or ExecutionRejectedException)
        {
            // HttpRequestException: network error or non-2xx status (after any retries). JsonException: unexpected
            // body. TaskCanceledException without caller cancellation: a plain HttpClient timeout.
            // ExecutionRejectedException: the resilience pipeline gave up without a response; its subclasses are
            // TimeoutRejectedException (attempt/total timeout), BrokenCircuitException (circuit open, so no
            // network call was even made) and RateLimiterRejectedException (too many calls already in flight). Without this, those would escape as 500s instead of 502s.
            throw new FlightDataUnavailableException("adsb.lol request failed.", ex);
        }

        // Failures are logged by FlightDataUnavailableExceptionHandler; successes here, with timing (which includes any
        // retries) so slow upstream calls are easy to find. No coordinates: see UseAppRequestLogging.
        var flights = response?.Aircraft is null
            ? []
            : response.Aircraft
                .Select(aircraft => ToFlight(aircraft, DateTimeOffset.FromUnixTimeMilliseconds(response.Now)))
                .OfType<Flight>() // drops the nulls
                .ToList();
        LogFetched(flights.Count, radiusNm, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        return flights;
    }

    // [LoggerMessage]: the source generator writes this method's body at compile time. The template is checked
    // against the parameters, and nothing is formatted or allocated when Information is switched off.
    [LoggerMessage(Level = LogLevel.Information,
        Message = "adsb.lol returned {AircraftCount} aircraft within {RadiusNm} nm in {ElapsedMs:0} ms")]
    private partial void LogFetched(int aircraftCount, int radiusNm, double elapsedMs);

    private static Flight? ToFlight(AdsbLolAircraft aircraft, DateTimeOffset snapshotTime)
    {
        if (string.IsNullOrWhiteSpace(aircraft.Hex) || aircraft.Lat is not { } lat || aircraft.Lon is not { } lon)
        {
            return null; // No position (or no ID): nothing to put on a map.
        }

        return new Flight
        {
            Hex = aircraft.Hex,
            Callsign = Callsigns.Clean(aircraft.Flight),
            Registration = NullIfBlank(aircraft.Registration),
            AircraftType = NullIfBlank(aircraft.AircraftType),
            Position = new GeoPoint(lat, lon),
            PositionTimestamp = snapshotTime - TimeSpan.FromSeconds(aircraft.SeenPos ?? 0),
            AltitudeFt = aircraft.AltBaro.Feet,
            OnGround = aircraft.AltBaro.OnGround,
            GroundSpeedKt = aircraft.GroundSpeed,
            TrackDeg = aircraft.Track ?? GroundHeading(aircraft),
            // Barometric rate is the standard; some transponders only send the GPS-derived one.
            VerticalRateFpm = RoundToInt(aircraft.BaroRate ?? aircraft.GeomRate),
        };
    }

    /// <summary>
    /// For aircraft on the ground, which adsb.lol sends with a heading but usually no track: on its wheels an
    /// aircraft moves the way its nose points, so heading is a good stand-in. Not in the air, where wind makes them
    /// differ (an airliner can crab 10-20° into a crosswind), so airborne aircraft without a track keep none.
    /// </summary>
    private static double? GroundHeading(AdsbLolAircraft aircraft) =>
        aircraft.AltBaro.OnGround ? aircraft.TrueHeading : null;

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static int? RoundToInt(double? value) =>
        value is { } v ? (int)Math.Round(v) : null;
}
