using System.ComponentModel;
using FlightRadar.Api.Flights;

namespace FlightRadar.Api.Locations;

/// <summary>What a client sends to save or change a location. Nullable so missing fields can be reported by name.</summary>
public sealed record SavedLocationRequest(
    [property: Description("A name of your choice, e.g. Home. 1 to 50 characters, unique among your locations.")]
    string? Name,
    [property: Description("Latitude of the centre, -90 to 90. Stored to 3 decimal places (about 100 m).")]
    double? Lat,
    [property: Description("Longitude of the centre, -180 to 180. Stored to 3 decimal places (about 100 m).")]
    double? Lon,
    [property: Description($"Search radius in kilometres, greater than 0 and at most {FlightsEndpoints.MaxRadiusKmText}.")]
    double? RadiusKm);

public sealed record SavedLocationDto(Guid Id, string Name, double Lat, double Lon, double RadiusKm)
{
    public static SavedLocationDto From(SavedLocation location) =>
        new(location.Id, location.Name, location.Latitude, location.Longitude, location.RadiusKm);
}

/// <summary>A request that passed validation, tidied up: name trimmed, coordinates rounded.</summary>
public sealed record ValidSavedLocation(string Name, double Latitude, double Longitude, double RadiusKm);

/// <summary>The rules for saved locations, as pure functions so they're easy to test.</summary>
public static class SavedLocationRules
{
    public const int MaxNameLength = 50;

    /// <summary>Plenty for favourite places; a cap so one owner can't fill the database.</summary>
    public const int MaxPerOwner = 20;

    /// <summary>
    /// 3 decimal places ≈ 111 m north-south (less east-west away from the equator). Saved locations are often homes:
    /// "a plane is near Home" doesn't need a front door, so the database never holds one.
    /// </summary>
    public const int CoordinateDecimals = 3;

    /// <summary>
    /// The errors in <paramref name="request"/> (keyed by field, as ValidationProblem expects), or none and the
    /// tidied-up location. The same checks as /api/flights for the numbers.
    /// </summary>
    public static (Dictionary<string, string[]> Errors, ValidSavedLocation? Location) Validate(SavedLocationRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        string? name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name))
            errors["name"] = ["name is required."];
        else if (name.Length > MaxNameLength)
            errors["name"] = [$"name must be at most {MaxNameLength} characters."];
        else if (name.Any(char.IsControl))
            errors["name"] = ["name can't contain control characters such as line breaks or tabs."];

        if (request.Lat is not { } lat)
            errors["lat"] = ["lat is required."];
        else if (!double.IsFinite(lat) || lat is < -90 or > 90)
            errors["lat"] = ["lat must be between -90 and 90."];

        if (request.Lon is not { } lon)
            errors["lon"] = ["lon is required."];
        else if (!double.IsFinite(lon) || lon is < -180 or > 180)
            errors["lon"] = ["lon must be between -180 and 180."];

        if (request.RadiusKm is not { } radius)
            errors["radiusKm"] = ["radiusKm is required."];
        else if (!double.IsFinite(radius) || radius is <= 0 or > FlightsEndpoints.MaxRadiusKm)
            errors["radiusKm"] = [$"radiusKm must be greater than 0 and at most {FlightsEndpoints.MaxRadiusKm}."];

        if (errors.Count > 0)
        {
            return (errors, null);
        }

        return (errors, new ValidSavedLocation(name!, Round(request.Lat!.Value), Round(request.Lon!.Value), request.RadiusKm!.Value));
    }

    private static double Round(double degrees) =>
        Math.Round(degrees, CoordinateDecimals, MidpointRounding.AwayFromZero);
}
