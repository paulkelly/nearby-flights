using System.Security.Claims;
using System.Text.Encodings.Web;
using FlightRadar.Api.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FlightRadar.Api.Owners;

/// <summary>
/// ASP.NET Core authentication for owner tokens: reads <c>Authorization: Bearer &lt;token&gt;</c>, looks up the owner
/// by the token's hash and, if found, makes the request's <see cref="HttpContext.User"/> that owner. Endpoints just
/// say <c>.RequireAuthorization()</c> and read the owner ID with <see cref="OwnerClaims.OwnerId"/>; none of them
/// handle tokens. Swapping in real sign-in later (step 9 or beyond) means another scheme, not changed endpoints.
/// </summary>
/// <remarks>
/// Authentication answers "who is this?" and only three ways: Success (this owner), NoResult (no token: anonymous)
/// or Fail (a token we don't accept). Authorization ("may they do this?") then turns anonymous or failed into 401.
/// Runs only for endpoints that require an owner (see <see cref="OwnersServiceCollectionExtensions.AddOwners"/>),
/// after the rate limiter, so floods of guessed tokens are cut off before they reach the database.
/// </remarks>
public sealed class OwnerTokenAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "OwnerToken";
    private const string BearerPrefix = "Bearer ";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? header = Request.Headers.Authorization;
        if (header is null || !header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult(); // no token: anonymous, which these endpoints then refuse
        }

        // Fail without asking the database when it can't be one of our tokens.
        if (!OwnerToken.TryHash(header[BearerPrefix.Length..].Trim(), out var hash))
        {
            return AuthenticateResult.Fail("Malformed owner token.");
        }

        // No database → DatabaseNotConfiguredException → 503 (via the exception handler), not a misleading 401.
        var ownerId = await Context.RequireDatabase().Owners
            .Where(owner => owner.TokenHash == hash)
            .Select(owner => (Guid?)owner.Id)
            .SingleOrDefaultAsync(Context.RequestAborted);

        if (ownerId is not { } id)
        {
            return AuthenticateResult.Fail("Unknown owner token.");
        }

        var identity = new ClaimsIdentity([new Claim(OwnerClaims.OwnerIdType, id.ToString())], SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }

    /// <summary>401 with <c>WWW-Authenticate: Bearer</c>, the standard hint for which kind of credentials to send.</summary>
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Headers.WWWAuthenticate = "Bearer";
        return base.HandleChallengeAsync(properties);
    }
}

public static class OwnerClaims
{
    public const string OwnerIdType = ClaimTypes.NameIdentifier;

    /// <summary>The signed-in owner's ID. Only call it in endpoints that require authorization.</summary>
    public static Guid OwnerId(this ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue(OwnerIdType)
            ?? throw new InvalidOperationException("No owner: does the endpoint call RequireAuthorization()?"));
}
