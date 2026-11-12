using System.ComponentModel;
using System.Security.Claims;
using FlightRadar.Api.Data;
using FlightRadar.Api.Locations;
using FlightRadar.Api.Owners;
using FlightRadar.Api.RateLimiting;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FlightRadar.Api.Devices;

/// <summary>What the app sends: every saved location its home-screen widgets show right now.</summary>
public sealed record DeviceWatchesRequest(
    [property: Description($"IDs of your saved locations that this device's widgets show. Empty stops watching. At most {DeviceEndpoints.MaxLocationIdsText}.")]
    IReadOnlyList<Guid>? LocationIds);

/// <summary>The locations the server took: the request's, minus any that don't exist (or aren't yours).</summary>
public sealed record DeviceWatchesDto(IReadOnlyList<Guid> LocationIds);

/// <summary>
/// Android devices tell the server which saved locations their widgets show (step 9c), so the poller can keep those
/// areas fresh around the clock. Which of them are actually watched (per-owner and overall caps) is the watcher's
/// business, not this endpoint's: it only records what each device wants.
/// </summary>
public static class DeviceEndpoints
{
    /// <summary>One device can't ask for more locations than an owner can save.</summary>
    public const int MaxLocationIds = SavedLocationRules.MaxPerOwner;

    public const string MaxLocationIdsText = "20";

    public static IEndpointRouteBuilder MapDeviceEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPut("/api/devices/{id:guid}", PutAsync)
            .WithTags("Devices")
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitingServiceCollectionExtensions.PerClientPolicy)
            .WithName("PutDeviceWatches")
            .WithSummary("Sets which of your saved locations this device's widgets show (a full replace).")
            .WithDescription(
                "Unknown IDs, and other people's, are left out of the reply rather than refused: a widget can still " +
                "show a location deleted in the meantime. 404 if the device ID belongs to someone else.")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return app;
    }

    private static async Task<Results<Ok<DeviceWatchesDto>, NotFound, ValidationProblem, ProblemHttpResult>> PutAsync(
        [Description("The app's installation ID: a random UUID it made once.")] Guid id,
        DeviceWatchesRequest request,
        ClaimsPrincipal user, HttpContext httpContext, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        if (request.LocationIds is not { } requested)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["locationIds"] = ["Location IDs are required (an empty list stops watching)."],
            });
        }
        if (requested.Count > MaxLocationIds)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["locationIds"] = [$"At most {MaxLocationIds} location IDs."],
            });
        }

        var db = httpContext.RequireDatabase();
        var ownerId = user.OwnerId();
        var now = timeProvider.GetUtcNow();

        var device = await db.Devices.SingleOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (device is not null && device.OwnerId != ownerId)
        {
            return TypedResults.NotFound(); // someone else's device: looks like nothing, as everywhere else
        }

        // Only the caller's own locations; the rest are dropped without saying why (deleted, or never theirs).
        var distinct = requested.Distinct().ToList();
        var known = (await db.SavedLocations
                .Where(location => location.OwnerId == ownerId && distinct.Contains(location.Id))
                .Select(location => location.Id)
                .ToListAsync(cancellationToken))
            .ToHashSet();
        var accepted = distinct.Where(known.Contains).ToList();

        if (accepted.Count == 0)
        {
            // Nothing to watch: forget the device altogether (its watches go with it, by cascade).
            if (device is not null)
            {
                db.Devices.Remove(device);
                await db.SaveChangesAsync(cancellationToken);
            }
            return TypedResults.Ok(new DeviceWatchesDto([]));
        }

        if (device is null)
        {
            device = new Device { Id = id, OwnerId = ownerId, CreatedAt = now };
            db.Devices.Add(device);
        }
        device.LastSeenAt = now;

        // A diff, not delete-all-then-insert: watches already there keep their CreatedAt (their place in the queue).
        var existing = await db.DeviceWatches.Where(watch => watch.DeviceId == id).ToListAsync(cancellationToken);
        db.DeviceWatches.RemoveRange(existing.Where(watch => !known.Contains(watch.LocationId)));
        var already = existing.Select(watch => watch.LocationId).ToHashSet();
        db.DeviceWatches.AddRange(accepted
            .Where(locationId => !already.Contains(locationId))
            .Select(locationId => new DeviceWatch { DeviceId = id, LocationId = locationId, CreatedAt = now }));

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.ForeignKeyViolation,
        })
        {
            // Two registrations of a new device at once, or a location deleted between our check and this write.
            // Rare, and sending again sorts it out.
            return TypedResults.Problem(
                "Something changed at the same time. Please try again.",
                statusCode: StatusCodes.Status409Conflict,
                title: "Can't register this device");
        }

        return TypedResults.Ok(new DeviceWatchesDto(accepted));
    }
}
