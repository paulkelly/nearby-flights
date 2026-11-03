using FlightRadar.Core.Flights;
using FlightRadar.Core.Geo;
using Microsoft.AspNetCore.Http.HttpResults;

namespace FlightRadar.Api.Flights;

public static class FlightsEndpoints
{
    /// <summary>250 nm, adsb.lol's maximum query radius.</summary>
    public const double MaxRadiusKm = 463;

    public static IEndpointRouteBuilder MapFlightsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/flights").WithTags("Flights");

        group.MapGet("", GetFlightsAsync)
            .WithName("GetFlights")
            .WithSummary("Live aircraft within a radius of a point, nearest first.")
            .ProducesProblem(StatusCodes.Status502BadGateway);

        return app;
    }

    // The return type lists every outcome, so the compiler checks each return and OpenAPI can describe them.
    private static async Task<Results<Ok<FlightsResponse>, ValidationProblem>> GetFlightsAsync(
        double? lat, double? lon, double? radiusKm, // from the query string; nullable so we can report "missing"
        NearbyFlightsService nearbyFlights,         // from DI
        CancellationToken cancellationToken)        // fires if the client disconnects
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

        return TypedResults.Ok(new FlightsResponse(
            centre.Latitude,
            centre.Longitude,
            radius,
            flights.Count,
            DateTimeOffset.UtcNow,
            flights.Select(FlightDto.From).ToList()));
    }

    private static Dictionary<string, string[]> Validate(double? lat, double? lon, double? radiusKm)
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
