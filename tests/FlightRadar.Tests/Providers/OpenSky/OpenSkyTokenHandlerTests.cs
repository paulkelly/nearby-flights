using System.Collections.Concurrent;
using System.Net;
using FlightRadar.Api.Providers.OpenSky;
using FlightRadar.Tests.Fakes;
using Microsoft.Extensions.Options;

namespace FlightRadar.Tests.Providers.OpenSky;

/// <summary>
/// The contract for OpenSkyTokenProvider + OpenSkyTokenHandler (the exercise). Two fake servers: a token endpoint
/// that hands out "token-1", "token-2", ... (each valid for 1800 s, like OpenSky's), and the OpenSky API, which
/// records the Authorization header of every request. The clock is fake, so "30 minutes later" is instant.
/// </summary>
public sealed class OpenSkyTokenHandlerTests
{
    private const string ClientId = "test-client";
    private const string ClientSecret = "s3cret-value";
    private const int TokenLifetimeSeconds = 1800;

    /// <summary>Everything one test needs: a client to send through, and what each fake server saw.</summary>
    private sealed class Rig
    {
        public required HttpClient Client { get; init; }
        public required FakeTimeProvider Clock { get; init; }
        /// <summary>The form body of each token request, e.g. "grant_type=client_credentials&amp;client_id=...".</summary>
        public ConcurrentQueue<string> TokenRequests { get; } = new();
        /// <summary>The Authorization header of each API request ("" if none).</summary>
        public ConcurrentQueue<string> ApiAuthorizations { get; } = new();
    }

    /// <param name="credentials">Configure a client ID and secret (otherwise anonymous).</param>
    /// <param name="api">How the fake API answers, given the call number (1, 2, ...). Default: always 200.</param>
    /// <param name="apiByAuthorization">
    /// Alternatively, how it answers given the Authorization header (e.g. "Bearer token-1"), and it may take its time
    /// (await something) before answering. Takes precedence over <paramref name="api"/>.
    /// </param>
    /// <param name="tokenGate">If set, the token endpoint waits for it before answering.</param>
    /// <param name="tokenStatus">The token endpoint's status code (e.g. 401 for wrong credentials).</param>
    private static Rig CreateRig(
        bool credentials = true,
        Func<int, HttpStatusCode>? api = null,
        Func<string, Task<HttpStatusCode>>? apiByAuthorization = null,
        Task? tokenGate = null,
        HttpStatusCode tokenStatus = HttpStatusCode.OK)
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero));
        var tokenCount = 0;
        var apiCount = 0;
        Rig? rig = null;

        var tokenEndpoint = new FakeHttpMessageHandler(async (request, ct) =>
        {
            rig!.TokenRequests.Enqueue(await request.Content!.ReadAsStringAsync(ct));
            if (tokenGate is not null) await tokenGate.WaitAsync(ct);
            var number = Interlocked.Increment(ref tokenCount);
            return new HttpResponseMessage(tokenStatus)
            {
                Content = new StringContent(tokenStatus == HttpStatusCode.OK
                    ? $$"""{"access_token":"token-{{number}}","expires_in":{{TokenLifetimeSeconds}},"token_type":"Bearer"}"""
                    : """{"error":"unauthorized_client"}"""),
            };
        });

        var openSkyApi = new FakeHttpMessageHandler(async (request, _) =>
        {
            var authorization = request.Headers.Authorization?.ToString() ?? "";
            rig!.ApiAuthorizations.Enqueue(authorization);
            var callNumber = Interlocked.Increment(ref apiCount);
            var status = apiByAuthorization is not null
                ? await apiByAuthorization(authorization)
                : api?.Invoke(callNumber) ?? HttpStatusCode.OK;
            return new HttpResponseMessage(status);
        });

        var options = Options.Create(new OpenSkyOptions
        {
            BaseUrl = new Uri("https://opensky-network.org/api/"),
            ClientId = credentials ? ClientId : null,
            ClientSecret = credentials ? ClientSecret : null,
        });
        var tokens = new OpenSkyTokenProvider(new SingleClientFactory(tokenEndpoint), options, clock);
        var handler = new OpenSkyTokenHandler(tokens) { InnerHandler = openSkyApi };

        rig = new Rig
        {
            Client = new HttpClient(handler) { BaseAddress = new Uri("https://opensky-network.org/api/") },
            Clock = clock,
        };
        return rig;
    }

    private static Task<HttpResponseMessage> CallApiAsync(Rig rig) => rig.Client.GetAsync("states/all");

    // ---- Anonymous ----

    [Fact]
    public async Task NoCredentials_SendsRequestsAnonymously()
    {
        var rig = CreateRig(credentials: false);

        await CallApiAsync(rig);

        Assert.Equal([""], rig.ApiAuthorizations);
        Assert.Empty(rig.TokenRequests);
    }

    // ---- Getting and using a token ----

    [Fact]
    public async Task FirstRequest_FetchesATokenAndSendsItAsBearer()
    {
        var rig = CreateRig();

        var response = await CallApiAsync(rig);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["Bearer token-1"], rig.ApiAuthorizations);
    }

    [Fact]
    public async Task TokenRequest_UsesTheClientCredentialsGrant()
    {
        var rig = CreateRig();

        await CallApiAsync(rig);

        var form = System.Web.HttpUtility.ParseQueryString(Assert.Single(rig.TokenRequests));
        Assert.Equal("client_credentials", form["grant_type"]);
        Assert.Equal(ClientId, form["client_id"]);
        Assert.Equal(ClientSecret, form["client_secret"]);
    }

    [Fact]
    public async Task LaterRequests_WhileTheTokenIsValid_ReuseIt()
    {
        var rig = CreateRig();

        await CallApiAsync(rig);
        rig.Clock.Advance(TimeSpan.FromMinutes(20));
        await CallApiAsync(rig);

        Assert.Single(rig.TokenRequests);
        Assert.Equal(["Bearer token-1", "Bearer token-1"], rig.ApiAuthorizations);
    }

    // ---- Expiry ----

    [Fact]
    public async Task TokenOutsideTheRefreshMargin_IsStillUsed()
    {
        var rig = CreateRig();
        await CallApiAsync(rig);

        // 31 s before expiry: still more than the 30 s margin left.
        rig.Clock.Advance(TimeSpan.FromSeconds(TokenLifetimeSeconds) - OpenSkyTokenProvider.RefreshMargin - TimeSpan.FromSeconds(1));
        await CallApiAsync(rig);

        Assert.Single(rig.TokenRequests);
    }

    [Fact]
    public async Task TokenInsideTheRefreshMargin_IsReplacedBeforeUse()
    {
        var rig = CreateRig();
        await CallApiAsync(rig);

        // 29 s before expiry: it could expire on its way to OpenSky, so get a new one first.
        rig.Clock.Advance(TimeSpan.FromSeconds(TokenLifetimeSeconds) - OpenSkyTokenProvider.RefreshMargin + TimeSpan.FromSeconds(1));
        await CallApiAsync(rig);

        Assert.Equal(2, rig.TokenRequests.Count);
        Assert.Equal(["Bearer token-1", "Bearer token-2"], rig.ApiAuthorizations);
    }

    // ---- Concurrency ----

    [Fact]
    public async Task ManyConcurrentRequests_WithNoToken_FetchOnlyOne()
    {
        var gate = new TaskCompletionSource();
        var rig = CreateRig(tokenGate: gate.Task);

        // 10 requests start while the first token is still being fetched...
        var calls = Enumerable.Range(0, 10).Select(_ => CallApiAsync(rig)).ToList();
        await Task.Delay(100); // let them all reach the token provider
        gate.SetResult();      // ...then the token endpoint answers.
        await Task.WhenAll(calls);

        Assert.Single(rig.TokenRequests);
        Assert.All(rig.ApiAuthorizations, auth => Assert.Equal("Bearer token-1", auth));
    }

    // ---- 401 from OpenSky ----

    [Fact]
    public async Task Unauthorized_GetsAFreshTokenAndRetriesOnce()
    {
        // OpenSky rejects the first token (e.g. revoked early), then accepts.
        var rig = CreateRig(api: call => call == 1 ? HttpStatusCode.Unauthorized : HttpStatusCode.OK);

        var response = await CallApiAsync(rig);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["Bearer token-1", "Bearer token-2"], rig.ApiAuthorizations);
    }

    [Fact]
    public async Task UnauthorizedTwice_GivesUpAndReturnsThe401()
    {
        // Always 401 (e.g. the API client was deleted): one retry, then stop. No infinite loop.
        var rig = CreateRig(api: _ => HttpStatusCode.Unauthorized);

        var response = await CallApiAsync(rig);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(2, rig.ApiAuthorizations.Count);
    }

    [Fact]
    public async Task ManyConcurrent401s_ForTheSameToken_RefreshItOnlyOnce()
    {
        // OpenSky accepts token-1 at first, then revokes it: from then on it rejects token-1 and accepts newer ones.
        // Each rejection is held back until all 5 requests have been rejected, so all 5 really do get a 401 for
        // token-1 at the same time (otherwise the first could finish refreshing before the others even start).
        var revoked = false;
        var rejectedSoFar = 0;
        var allFiveRejected = new TaskCompletionSource();
        var rig = CreateRig(apiByAuthorization: async authorization =>
        {
            if (!revoked || authorization != "Bearer token-1")
            {
                return HttpStatusCode.OK;
            }
            if (Interlocked.Increment(ref rejectedSoFar) == 5)
            {
                allFiveRejected.SetResult();
            }
            // Time-limited, so a broken implementation makes this test fail rather than hang.
            await allFiveRejected.Task.WaitAsync(TimeSpan.FromSeconds(5));
            return HttpStatusCode.Unauthorized;
        });

        await CallApiAsync(rig); // token-1 is fetched, cached and accepted
        revoked = true;

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => CallApiAsync(rig)));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        // One refresh for all five: the first to get the lock fetches token-2; the other four see that token-1 has
        // already been replaced and use token-2. (Forcing a refresh each time would fetch token-2 ... token-6.)
        Assert.Equal(2, rig.TokenRequests.Count);
        Assert.Equal(5, rig.ApiAuthorizations.Count(authorization => authorization == "Bearer token-2"));
    }

    // ---- Token endpoint failures ----

    [Fact]
    public async Task TokenEndpointRejectsTheCredentials_ThrowsHttpRequestExceptionWithoutTheSecret()
    {
        var rig = CreateRig(tokenStatus: HttpStatusCode.Unauthorized);

        // HttpRequestException is what OpenSkyFlightDataProvider maps to a 502.
        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => CallApiAsync(rig));

        Assert.DoesNotContain(ClientSecret, exception.ToString()); // secrets never end up in logs via exceptions
        Assert.Empty(rig.ApiAuthorizations);                        // and OpenSky was never called without a token
    }
}
