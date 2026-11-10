using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FlightRadar.Api.Owners;

public static class OwnersServiceCollectionExtensions
{
    /// <summary>
    /// Owner tokens as an authentication scheme, and <c>.RequireAuthorization()</c> meaning "an owner, proven by
    /// their token". Registered whether or not a database is configured: without one, requests to owner endpoints get
    /// 503 rather than a confusing 401.
    /// </summary>
    /// <remarks>
    /// Deliberately <em>not</em> the default authentication scheme: a default scheme runs on every request, so
    /// /api/flights with a token would cost a database lookup (and fail when the database is down). Named in the
    /// default authorization policy instead, the token is checked only by endpoints that require an owner.
    /// </remarks>
    public static IServiceCollection AddOwners(this IServiceCollection services)
    {
        // Since ASP.NET Core 7, when exactly one scheme is registered it silently becomes the default anyway. This
        // documented switch turns that off, so the owner check really runs only where it's required.
        AppContext.SetSwitch("Microsoft.AspNetCore.Authentication.SuppressAutoDefaultScheme", true);

        services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, OwnerTokenAuthenticationHandler>(
                OwnerTokenAuthenticationHandler.SchemeName, configureOptions: null);
        services.AddAuthorizationBuilder()
            .SetDefaultPolicy(new AuthorizationPolicyBuilder(OwnerTokenAuthenticationHandler.SchemeName)
                .RequireAuthenticatedUser()
                .Build());
        services.TryAddSingleton(TimeProvider.System);
        return services;
    }
}
