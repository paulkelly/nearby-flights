using System.ComponentModel.DataAnnotations;

namespace FlightRadar.Api.Providers.OpenSky;

/// <summary>Settings for the OpenSky provider, bound from the "Providers:OpenSky" config section.</summary>
/// <remarks>
/// Without credentials, requests are anonymous: 400 credits a day and 10-second data. With an account (stage 5f
/// piece 3), 4,000 a day and 5-second data. A box up to 25 square degrees (a ~250 km search) costs 1 credit.
/// </remarks>
public sealed class OpenSkyOptions
{
    public const string SectionName = "Providers:OpenSky";

    /// <summary>API root; must end with "/" (see AdsbLolOptions.BaseUrl).</summary>
    [Required]
    public Uri? BaseUrl { get; set; }

    [Range(1, 60)]
    public int TotalTimeoutSeconds { get; set; } = 8;

    [Range(1, 30)]
    public int AttemptTimeoutSeconds { get; set; } = 4;

    /// <summary>
    /// Retries for transient failures. Fewer than adsb.lol: every attempt can cost credits. 429 ("out of credits")
    /// is never retried, whatever this says.
    /// </summary>
    [Range(0, 5)]
    public int MaxRetries { get; set; } = 1;

    /// <summary>Calls in flight at once (see AdsbLolOptions.MaxConcurrentRequests). Lower: credits are scarce.</summary>
    [Range(1, 100)]
    public int MaxConcurrentRequests { get; set; } = 4;

    [Range(0, 1000)]
    public int MaxQueuedRequests { get; set; } = 10;

    /// <summary>
    /// OAuth2 client credentials, from an API client created on the OpenSky account page. Optional: without them,
    /// requests are anonymous. A secret, so never in appsettings or git: user-secrets locally, .env in Docker.
    /// </summary>
    public string? ClientId { get; set; }

    /// <inheritdoc cref="ClientId"/>
    public string? ClientSecret { get; set; }

    /// <summary>Where access tokens come from (OpenSky's Keycloak server).</summary>
    [Required]
    public Uri? TokenUrl { get; set; } =
        new("https://auth.opensky-network.org/auth/realms/opensky-network/protocol/openid-connect/token");

    /// <summary>Credentials are configured: requests are authenticated. Blank values count as not configured.</summary>
    public bool HasCredentials => !string.IsNullOrWhiteSpace(ClientId);

    /// <summary>A client ID without its secret is a configuration mistake: fail at startup, not on the first 401.</summary>
    internal bool CredentialsAreComplete() => !HasCredentials || !string.IsNullOrWhiteSpace(ClientSecret);
}
