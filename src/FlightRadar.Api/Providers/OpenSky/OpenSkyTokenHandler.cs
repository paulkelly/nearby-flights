using System.Net;
using System.Net.Http.Headers;

namespace FlightRadar.Api.Providers.OpenSky;

/// <summary>
/// Adds "Authorization: Bearer &lt;token&gt;" to every request to OpenSky, using <see cref="OpenSkyTokenProvider"/>.
/// Sits in the OpenSky HttpClient's handler pipeline, inside the resilience handler, so it runs for each attempt.
/// </summary>
public sealed class OpenSkyTokenHandler(OpenSkyTokenProvider tokens) : DelegatingHandler
{
    private const string AuthenticationScheme = "Bearer";

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!tokens.HasCredentials)
        {
            return await base.SendAsync(request, cancellationToken); // anonymous
        }

        var token = await tokens.GetTokenAsync(rejectedToken: null, cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue(AuthenticationScheme, token);

        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return response;
        }

        // A 401 means OpenSky rejected the token (expired or revoked early): refresh once and resend. A second 401 is
        // returned as it is, so wrong credentials can't loop. Token endpoint failures aren't caught here: they throw
        // HttpRequestException, which the resilience handler outside this one retries.
        response.Dispose();
        var freshToken = await tokens.GetTokenAsync(rejectedToken: token, cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue(AuthenticationScheme, freshToken);

        return await base.SendAsync(request, cancellationToken);
    }
}
