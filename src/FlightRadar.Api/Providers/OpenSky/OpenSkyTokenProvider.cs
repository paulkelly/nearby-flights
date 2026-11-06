using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace FlightRadar.Api.Providers.OpenSky;

/// <summary>
/// Gets OAuth2 access tokens for OpenSky (client credentials flow) and keeps the current one until shortly before it
/// expires. A singleton, so every request shares one token.
/// </summary>
/// <remarks>
/// Why this is separate from <see cref="OpenSkyTokenHandler"/>: IHttpClientFactory recreates message handlers every
/// couple of minutes (to pick up DNS changes), so a token cached in the handler would be thrown away with it. State
/// that must outlive that lives here instead.
/// </remarks>
public sealed class OpenSkyTokenProvider(
    IHttpClientFactory httpClientFactory, IOptions<OpenSkyOptions> options, TimeProvider timeProvider)
{
    private sealed record OpenSkyToken(string AccessToken, DateTimeOffset ExpiresAt);
    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresInSeconds);

    /// <summary>The named HttpClient for the token endpoint (plain: no token handler on it, obviously).</summary>
    public const string HttpClientName = "OpenSkyAuth";

    /// <summary>Refresh this long before the token's stated expiry, so it can't expire on its way to OpenSky.</summary>
    public static readonly TimeSpan RefreshMargin = TimeSpan.FromSeconds(30);

    public bool HasCredentials => options.Value.HasCredentials;

    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private OpenSkyToken? _cachedToken;

    /// <summary>
    /// A valid access token: the cached one if it's still good, otherwise a new one from the token endpoint.
    /// </summary>
    /// <param name="rejectedToken">
    /// A token OpenSky has just answered 401 to, or null for a normal request. The cached token is used only if it
    /// isn't this one. So when several requests are rejected with the same token at once, the first to get the lock
    /// fetches a replacement and the rest find it already in the cache: one refresh, not one per request.
    /// </param>
    /// <exception cref="HttpRequestException">The token endpoint failed or rejected the credentials.</exception>
    /// <remarks>
    /// Only one caller at a time can fetch (<see cref="_refreshLock"/>); everyone else waits, then re-checks the cache
    /// before fetching, because the caller ahead of them has usually just filled it. A failed fetch leaves the cache as
    /// it was, so the next request simply tries again. The client secret never appears in exceptions or logs.
    /// </remarks>
    public async Task<string> GetTokenAsync(string? rejectedToken, CancellationToken cancellationToken)
    {
        var cached = _cachedToken;
        if (IsTokenValid(cached, rejectedToken))
        {
            return cached.AccessToken;
        }

        await _refreshLock.WaitAsync(cancellationToken);

        try
        {
            cached = _cachedToken;
            if (IsTokenValid(cached, rejectedToken))
            {
                return cached.AccessToken;
            }

            var client = httpClientFactory.CreateClient(HttpClientName);
            using var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = options.Value.ClientId!,
                ["client_secret"] = options.Value.ClientSecret!,
            });

            using var response = await client.PostAsync(options.Value.TokenUrl, form, cancellationToken);
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken);
            if (body is null || string.IsNullOrEmpty(body.AccessToken))
            {
                throw new HttpRequestException("OpenSky token endpoint returned an invalid response.");
            }

            // OpenSky's tokens last 1800 s; fall back to that if expires_in is missing (0), as OpenSky's own example does.
            var expiresInSeconds = body.ExpiresInSeconds > 0 ? body.ExpiresInSeconds : 1800;
            var fetched = new OpenSkyToken(body.AccessToken, timeProvider.GetUtcNow().AddSeconds(expiresInSeconds));
            _cachedToken = fetched;
            return fetched.AccessToken;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    /// <summary>Present, not within <see cref="RefreshMargin"/> of expiring, and not the token OpenSky just rejected.</summary>
    private bool IsTokenValid([NotNullWhen(true)] OpenSkyToken? token, string? rejectedToken) =>
        token is not null &&
        token.ExpiresAt - RefreshMargin > timeProvider.GetUtcNow() &&
        token.AccessToken != rejectedToken;
}
