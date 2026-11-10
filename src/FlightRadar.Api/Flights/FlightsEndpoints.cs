using System.ComponentModel;
using FlightRadar.Api.RateLimiting;
using FlightRadar.Core.Flights;
using FlightRadar.Core.Geo;
using Microsoft.AspNetCore.Http.HttpResults;

namespace FlightRadar.Api.Flights;

public static class FlightsEndpoints
{
    /// <summary>
    /// The largest search radius users may ask for: a product choice, not a provider limit. Keep it below the
    /// provider's own limit (adsb.lol: 250 nm ≈ 463 km), with room for the cache's snapping margin.
    /// To change it, change this, <see cref="MaxRadiusKmText"/> and MAX_RADIUS_KM in web/src/components/radiusScale.ts;
    /// a test fails if the first two disagree.
    /// </summary>
    public const int MaxRadiusKm = 250;

    /// <summary>The same number as text: attribute arguments (the OpenAPI description below) must be constants.</summary>
    internal const string MaxRadiusKmText = "250";

    public static IEndpointRouteBuilder MapFlightsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/flights")
            .WithTags("Flights")
            .RequireRateLimiting(RateLimitingServiceCollectionExtensions.PerClientPolicy); // per client IP: see RateLimiting/

        group.MapGet("", GetFlightsAsync)
            .WithName("GetFlights")
            .WithSummary("Live aircraft within a radius of a point, nearest first.")
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status502BadGateway);

        return app;
    }

    // The return type lists every outcome, so the compiler checks each return and OpenAPI can describe them.
    // lat/lon/radiusKm come from the query string; nullable so we can report "missing" ourselves.
    // [Description] text shows up in the OpenAPI docs.
    private static async Task<Results<Ok<FlightsResponse>, ValidationProblem>> GetFlightsAsync(
        [Description("Latitude of the centre, -90 to 90.")] double? lat,
        [Description("Longitude of the centre, -180 to 180.")] double? lon,
        [Description($"Search radius in kilometres, greater than 0 and at most {MaxRadiusKmText}.")] double? radiusKm,
        NearbyFlightsService nearbyFlights,   // from DI
        FlightsResponseBuilder responses,     // from DI
        CancellationToken cancellationToken)  // fires if the client disconnects
    {
        var errors = Validate(lat, lon, radiusKm);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        // Validate() guarantees all three have values.
        var centre = new GeoPoint(lat!.Value, lon!.Value);
        var radius = radiusKm!.Value;

        // FlightDataUnavailableException is deliberately not caught here: the exception handler turns it into a 502.
        var flights = await nearbyFlights.GetNearbyFlightsAsync(centre, radius, cancellationToken);

        // Airline names, routes (within their short budget), airport guesses: shared with the hub (step 7b).
        return TypedResults.Ok(await responses.BuildAsync(centre, radius, flights, cancellationToken));
    }

    /// <summary>The query's problems, keyed by parameter (empty if none). Also used by the hub's Watch.</summary>
    internal static Dictionary<string, string[]> Validate(double? lat, double? lon, double? radiusKm)
    {
        var errors = new Dictionary<string, string[]>();

        // IsFinite matters: "?lat=NaN" binds successfully, and NaN fails every < and > comparison,
        // so a plain range check would let it through.
        if (lat is not { } latitude)
            errors["lat"] = ["lat is required."];
        else if (!double.IsFinite(latitude) || latitude is < -90 or > 90)
            errors["lat"] = ["lat must be between -90 and 90."];

        if (lon is not { } longitude)
            errors["lon"] = ["lon is required."];
        else if (!double.IsFinite(longitude) || longitude is < -180 or > 180)
            errors["lon"] = ["lon must be between -180 and 180."];

        if (radiusKm is not { } radius)
            errors["radiusKm"] = ["radiusKm is required."];
        else if (!double.IsFinite(radius) || radius is <= 0 or > MaxRadiusKm)
            errors["radiusKm"] = [$"radiusKm must be greater than 0 and at most {MaxRadiusKm}."];

        return errors;
    }
}
