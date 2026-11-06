using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text.Json;
using FlightRadar.Core.Flights;
using FlightRadar.Core.Geo;
using Polly;

namespace FlightRadar.Api.Providers.OpenSky;

/// <summary>
/// Fetches aircraft from the OpenSky Network: GET /states/all with a bounding box. OpenSky searches by box, not
/// circle, so this asks for the box(es) around the circle; NearbyFlightsService trims to the exact circle afterwards.
/// </summary>
/// <remarks>
/// OpenSky has no aircraft type or registration in state vectors, so those are always null with this provider.
/// </remarks>
public sealed partial class OpenSkyFlightDataProvider(HttpClient httpClient, ILogger<OpenSkyFlightDataProvider> logger)
    : IFlightDataProvider
{
    /// <summary>OpenSky takes any box size (bigger boxes cost more credits), so no radius limit of its own.</summary>
    public double MaxRadiusKm => double.PositiveInfinity;

    public async Task<IReadOnlyList<Flight>> GetFlightsAsync(
        GeoPoint centre, double radiusKm, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();

        // Usually one box; two (two requests) when the circle crosses the ±180° meridian.
        var flights = new List<Flight>();
        foreach (var box in BoundingBox.Around(centre, radiusKm))
        {
            flights.AddRange(await GetBoxAsync(box, cancellationToken));
        }

        // An aircraft can't really be in both halves of a split box, but de-duplicate anyway: two entries with the
        // same hex would show two markers for one aircraft.
        var distinct = flights.DistinctBy(flight => flight.Hex).ToList();
        LogFetched(distinct.Count, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        return distinct;
    }

    private async Task<IEnumerable<Flight>> GetBoxAsync(BoundingBox box, CancellationToken cancellationToken)
    {
        // InvariantCulture: "53.42", never "53,42".
        var path = string.Create(CultureInfo.InvariantCulture,
            $"states/all?lamin={box.LatMin}&lomin={box.LonMin}&lamax={box.LatMax}&lomax={box.LonMax}");

        OpenSkyResponse? response;
        try
        {
            using var message = await httpClient.GetAsync(path, cancellationToken);

            if (message.StatusCode == HttpStatusCode.TooManyRequests)
            {
                // Out of credits. OpenSky says how long until more in its own header, not the standard Retry-After.
                var retryAfter = Header(message, "X-Rate-Limit-Retry-After-Seconds");
                LogOutOfCredits(retryAfter);
                throw new FlightDataUnavailableException($"OpenSky credits exhausted; retry after {retryAfter} s.");
            }

            message.EnsureSuccessStatusCode();
            LogCredits(Header(message, "X-Rate-Limit-Remaining"), box.AreaSquareDegrees);
            response = await message.Content.ReadFromJsonAsync<OpenSkyResponse>(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // The caller cancelled. Not an upstream failure.
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException
                                       or ExecutionRejectedException)
        {
            // Same mapping as AdsbLolFlightDataProvider: upstream failures, including the resilience pipeline giving
            // up (timeouts, open circuit, concurrency limit full), become a 502 rather than a 500.
            throw new FlightDataUnavailableException("OpenSky request failed.", ex);
        }

        return response?.States is { } states
            ? states.Select(ToFlight).OfType<Flight>() // drops the nulls
            : [];
    }

    private static Flight? ToFlight(JsonElement state)
    {
        var hex = StateVector.String(state, StateVector.Icao24);
        if (string.IsNullOrWhiteSpace(hex) ||
            StateVector.Number(state, StateVector.Latitude) is not { } lat ||
            StateVector.Number(state, StateVector.Longitude) is not { } lon)
        {
            return null; // No position (or no ID): nothing to put on a map.
        }

        var onGround = StateVector.Bool(state, StateVector.OnGround);
        // Last position report; fall back to last contact of any kind if OpenSky hasn't one.
        var positionSeconds = StateVector.Number(state, StateVector.TimePosition)
                              ?? StateVector.Number(state, StateVector.LastContact)
                              ?? 0;

        return new Flight
        {
            Hex = hex.Trim().ToLowerInvariant(),
            Callsign = StateVector.String(state, StateVector.Callsign)?.Trim() is { Length: > 0 } callsign ? callsign : null,
            Registration = null, // not in OpenSky state vectors
            AircraftType = null, // likewise
            Position = new GeoPoint(lat, lon),
            PositionTimestamp = DateTimeOffset.FromUnixTimeSeconds((long)positionSeconds),
            // Metric on the wire; feet, knots and ft/min in our model (the units adsb.lol and the frontend use).
            AltitudeFt = onGround ? null : RoundToInt(StateVector.Number(state, StateVector.BaroAltitude), Units.MetresToFeet),
            OnGround = onGround,
            GroundSpeedKt = StateVector.Number(state, StateVector.Velocity) is { } velocity
                ? Math.Round(Units.MetresPerSecondToKnots(velocity), 1)
                : null,
            TrackDeg = StateVector.Number(state, StateVector.TrueTrack),
            VerticalRateFpm = RoundToInt(StateVector.Number(state, StateVector.VerticalRate), Units.MetresPerSecondToFeetPerMinute),
        };
    }

    private static int? RoundToInt(double? value, Func<double, double> convert) =>
        value is { } v ? (int)Math.Round(convert(v)) : null;

    private static string? Header(HttpResponseMessage message, string name) =>
        message.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;

    [LoggerMessage(Level = LogLevel.Information, Message = "OpenSky returned {AircraftCount} aircraft in {ElapsedMs:0} ms")]
    private partial void LogFetched(int aircraftCount, double elapsedMs);

    // Credits are the scarce resource with OpenSky, so make the balance easy to watch (e.g. in Seq).
    [LoggerMessage(Level = LogLevel.Information,
        Message = "OpenSky credits remaining: {CreditsRemaining} (box {BoxSquareDegrees:0.#} sq°)")]
    private partial void LogCredits(string? creditsRemaining, double boxSquareDegrees);

    [LoggerMessage(Level = LogLevel.Warning, Message = "OpenSky credits exhausted; more in {RetryAfterSeconds} s")]
    private partial void LogOutOfCredits(string? retryAfterSeconds);
}
