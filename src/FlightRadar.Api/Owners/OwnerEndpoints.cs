using System.ComponentModel;
using System.Security.Claims;
using FlightRadar.Api.Data;
using FlightRadar.Api.RateLimiting;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace FlightRadar.Api.Owners;

[Description("A new owner's token. Shown once: only its hash is stored, so it can't be looked up again.")]
public sealed record NewOwnerResponse(
    [property: Description("Send it as `Authorization: Bearer <token>`. Keep it secret: it's the owner's only key.")]
    string Token);

public sealed record OwnerResponse(DateTimeOffset CreatedAt);

public static class OwnerEndpoints
{
    public static IEndpointRouteBuilder MapOwnerEndpoints(this IEndpointRouteBuilder app)
    {
        var owners = app.MapGroup("/api/owners").WithTags("Owners");

        owners.MapPost("/", CreateAsync)
            .WithName("CreateOwner")
            .WithSummary("Creates an anonymous owner for saved locations and returns its token.")
            .WithDescription("Call this once, when a client first saves something, and keep the token.")
            .RequireRateLimiting(RateLimitingServiceCollectionExtensions.NewOwnerPolicy)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        owners.MapGet("/me", GetMeAsync)
            .WithName("GetOwner")
            .WithSummary("The owner the token belongs to. 401 means the token isn't (or is no longer) valid.")
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitingServiceCollectionExtensions.PerClientPolicy)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return app;
    }

    private static async Task<Created<NewOwnerResponse>> CreateAsync(
        HttpContext httpContext, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var db = httpContext.RequireDatabase();
        var (token, hash) = OwnerToken.Create();

        db.Owners.Add(new Owner { TokenHash = hash, CreatedAt = timeProvider.GetUtcNow() });
        await db.SaveChangesAsync(cancellationToken);

        // 201 without a Location header: the new owner has no URL of its own other than /me with this token.
        return TypedResults.Created((string?)null, new NewOwnerResponse(token));
    }

    private static async Task<Results<Ok<OwnerResponse>, NotFound>> GetMeAsync(
        ClaimsPrincipal user, HttpContext httpContext, CancellationToken cancellationToken)
    {
        var ownerId = user.OwnerId();
        var createdAt = await httpContext.RequireDatabase().Owners
            .Where(owner => owner.Id == ownerId)
            .Select(owner => (DateTimeOffset?)owner.CreatedAt)
            .SingleOrDefaultAsync(cancellationToken);

        // Deleted between authentication and this query: vanishingly rare, but say so rather than crash.
        return createdAt is { } value ? TypedResults.Ok(new OwnerResponse(value)) : TypedResults.NotFound();
    }
}
