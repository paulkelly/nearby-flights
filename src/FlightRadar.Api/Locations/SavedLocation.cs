namespace FlightRadar.Api.Locations;

/// <summary>A place an owner saved to come back to: a named centre and radius, like a waypoint on a game map.</summary>
public sealed class SavedLocation
{
    public Guid Id { get; set; }

    /// <summary>Whose it is. Every query filters on this: an owner never sees, changes or learns of anyone else's.</summary>
    public Guid OwnerId { get; set; }

    /// <summary>Unique per owner, as typed (trimmed). At most <see cref="SavedLocationRules.MaxNameLength"/> characters.</summary>
    public required string Name { get; set; }

    /// <summary>Rounded to <see cref="SavedLocationRules.CoordinateDecimals"/> decimal places (~100 m) before saving.</summary>
    public double Latitude { get; set; }

    public double Longitude { get; set; }

    public double RadiusKm { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
