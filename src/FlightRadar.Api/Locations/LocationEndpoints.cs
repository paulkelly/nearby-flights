using System.ComponentModel;
using System.Security.Claims;
using FlightRadar.Api.Data;
using FlightRadar.Api.Owners;
using FlightRadar.Api.RateLimiting;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FlightRadar.Api.Locations;

/// <summary>
/// Saved locations: the signed-in owner's own, and nobody else's. Every query filters on the owner ID from the token,
/// and someone else's location answers 404 exactly like one that doesn't exist (403 would confirm it exists).
/// </summary>
public static class LocationEndpoints
{
    public static IEndpointRouteBuilder MapLocationEndpoints(this IEndpointRouteBuilder app)
    {
        var locations = app.MapGroup("/api/locations")
            .WithTags("Saved locations")
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitingServiceCollectionExtensions.PerClientPolicy)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        locations.MapGet("/", ListAsync)
            .WithName("ListSavedLocations")
            .WithSummary("Your saved locations, oldest first.");

        locations.MapGet("/{id:guid}", GetAsync)
            .WithName("GetSavedLocation")
            .WithSummary("One of your saved locations.");

        locations.MapPost("/", CreateAsync)
            .WithName("CreateSavedLocation")
            .WithSummary("Saves a location. 409 if the name is taken or you have the maximum already.")
            .ProducesProblem(StatusCodes.Status409Conflict);

        locations.MapPut("/{id:guid}", UpdateAsync)
            .WithName("UpdateSavedLocation")
            .WithSummary("Renames or moves one of your saved locations. 409 if the new name is taken.")
            .ProducesProblem(StatusCodes.Status409Conflict);

        locations.MapDelete("/{id:guid}", DeleteAsync)
            .WithName("DeleteSavedLocation")
            .WithSummary("Deletes one of your saved locations.");

        return app;
    }

    private static async Task<Ok<List<SavedLocationDto>>> ListAsync(
        ClaimsPrincipal user, HttpContext httpContext, CancellationToken cancellationToken)
    {
        var ownerId = user.OwnerId();
        var locations = await httpContext.RequireDatabase().SavedLocations
            .Where(location => location.OwnerId == ownerId)
            .OrderBy(location => location.CreatedAt).ThenBy(location => location.Id)
            .Select(location => new SavedLocationDto(
                location.Id, location.Name, location.Latitude, location.Longitude, location.RadiusKm))
            .ToListAsync(cancellationToken);
        return TypedResults.Ok(locations);
    }

    private static async Task<Results<Ok<SavedLocationDto>, NotFound>> GetAsync(
        [Description("The saved location's ID.")] Guid id,
        ClaimsPrincipal user, HttpContext httpContext, CancellationToken cancellationToken)
    {
        var location = await FindOwnAsync(httpContext.RequireDatabase(), user.OwnerId(), id, cancellationToken);
        return location is null ? TypedResults.NotFound() : TypedResults.Ok(SavedLocationDto.From(location));
    }

    private static async Task<Results<Created<SavedLocationDto>, ValidationProblem, ProblemHttpResult>> CreateAsync(
        SavedLocationRequest request,
        ClaimsPrincipal user, HttpContext httpContext, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var (errors, valid) = SavedLocationRules.Validate(request);
        if (valid is null)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var db = httpContext.RequireDatabase();
        var ownerId = user.OwnerId();

        // Checked here for a friendly answer. Two saves at once could both pass the count check and end up at 21: harmless,
        // so not worth a lock. They can't both take one name, though: the unique index catches that race (below).
        if (await db.SavedLocations.CountAsync(location => location.OwnerId == ownerId, cancellationToken)
            >= SavedLocationRules.MaxPerOwner)
        {
            return Conflict($"You can save up to {SavedLocationRules.MaxPerOwner} locations. Delete one to save another.");
        }
        if (await NameTakenAsync(db, ownerId, valid.Name, exceptId: null, cancellationToken))
        {
            return NameTaken(valid.Name);
        }

        var location = new SavedLocation
        {
            OwnerId = ownerId,
            Name = valid.Name,
            Latitude = valid.Latitude,
            Longitude = valid.Longitude,
            RadiusKm = valid.RadiusKm,
            CreatedAt = timeProvider.GetUtcNow(),
        };
        db.SavedLocations.Add(location);
        if (!await TrySaveAsync(db, cancellationToken))
        {
            return NameTaken(valid.Name);
        }

        return TypedResults.Created($"/api/locations/{location.Id}", SavedLocationDto.From(location));
    }

    private static async Task<Results<Ok<SavedLocationDto>, NotFound, ValidationProblem, ProblemHttpResult>> UpdateAsync(
        [Description("The saved location's ID.")] Guid id,
        SavedLocationRequest request,
        ClaimsPrincipal user, HttpContext httpContext, CancellationToken cancellationToken)
    {
        var (errors, valid) = SavedLocationRules.Validate(request);
        if (valid is null)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var db = httpContext.RequireDatabase();
        var ownerId = user.OwnerId();
        var location = await FindOwnAsync(db, ownerId, id, cancellationToken);
        if (location is null)
        {
            return TypedResults.NotFound();
        }
        if (await NameTakenAsync(db, ownerId, valid.Name, exceptId: id, cancellationToken))
        {
            return NameTaken(valid.Name);
        }

        // The DbContext tracks entities it loaded: changing properties is enough, SaveChanges writes only what changed.
        location.Name = valid.Name;
        location.Latitude = valid.Latitude;
        location.Longitude = valid.Longitude;
        location.RadiusKm = valid.RadiusKm;
        if (!await TrySaveAsync(db, cancellationToken))
        {
            return NameTaken(valid.Name);
        }

        return TypedResults.Ok(SavedLocationDto.From(location));
    }

    private static async Task<Results<NoContent, NotFound>> DeleteAsync(
        [Description("The saved location's ID.")] Guid id,
        ClaimsPrincipal user, HttpContext httpContext, CancellationToken cancellationToken)
    {
        var ownerId = user.OwnerId();
        // One DELETE … WHERE statement, no load first. The owner filter is what stops anyone deleting another's.
        int deleted = await httpContext.RequireDatabase().SavedLocations
            .Where(location => location.Id == id && location.OwnerId == ownerId)
            .ExecuteDeleteAsync(cancellationToken);
        return deleted == 0 ? TypedResults.NotFound() : TypedResults.NoContent();
    }

    private static Task<SavedLocation?> FindOwnAsync(
        AppDbContext db, Guid ownerId, Guid id, CancellationToken cancellationToken) =>
        db.SavedLocations.SingleOrDefaultAsync(
            location => location.Id == id && location.OwnerId == ownerId, cancellationToken);

    private static Task<bool> NameTakenAsync(
        AppDbContext db, Guid ownerId, string name, Guid? exceptId, CancellationToken cancellationToken) =>
        db.SavedLocations.AnyAsync(
            location => location.OwnerId == ownerId && location.Name == name && location.Id != exceptId,
            cancellationToken);

    /// <summary>False if the unique index rejected the name: another save took it between our check and this write.</summary>
    private static async Task<bool> TrySaveAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return false;
        }
    }

    private static ProblemHttpResult NameTaken(string name) =>
        Conflict($"You already have a location called \"{name}\".");

    private static ProblemHttpResult Conflict(string detail) =>
        TypedResults.Problem(detail, statusCode: StatusCodes.Status409Conflict, title: "Can't save that location");
}
