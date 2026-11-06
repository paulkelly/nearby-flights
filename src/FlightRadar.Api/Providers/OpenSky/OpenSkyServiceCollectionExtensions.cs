using System.Net;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

namespace FlightRadar.Api.Providers.OpenSky;

public static class OpenSkyServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="OpenSkyFlightDataProvider"/> under its own type, configured from "Providers:OpenSky".
    /// Like adsb.lol, it isn't registered as IFlightDataProvider itself: the app chooses (and wraps) one.
    /// </summary>
    public static IServiceCollection AddOpenSkyFlightDataProvider(this IServiceCollection services)
    {
        services.AddOptions<OpenSkyOptions>()
            .BindConfiguration(OpenSkyOptions.SectionName)
            .ValidateDataAnnotations()
            .Validate(
                options => options.AttemptTimeoutSeconds < options.TotalTimeoutSeconds,
                "AttemptTimeoutSeconds must be less than TotalTimeoutSeconds.")
            .Validate(
                options => options.CredentialsAreComplete(),
                "Providers:OpenSky:ClientId is set but ClientSecret isn't. Set both (user-secrets or .env) or neither.")
            .ValidateOnStart();

        // OAuth2: one token provider for the whole app (it holds the cached token), its own plain HttpClient for the
        // token endpoint, and the handler that adds the token to OpenSky requests.
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<OpenSkyTokenProvider>();
        services.AddHttpClient(OpenSkyTokenProvider.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(10));
        services.AddTransient<OpenSkyTokenHandler>();

        var openSkyClient = services.AddHttpClient<OpenSkyFlightDataProvider>((serviceProvider, client) =>
            {
                var options = serviceProvider.GetRequiredService<IOptions<OpenSkyOptions>>().Value;
                client.BaseAddress = options.BaseUrl;
                client.DefaultRequestHeaders.UserAgent.ParseAdd("FlightRadar/0.1");
                client.Timeout = Timeout.InfiniteTimeSpan; // the pipeline owns timeouts (see adsb.lol)
            });

        // The same standard pipeline as adsb.lol (see AdsbLolServiceCollectionExtensions for each layer).
        openSkyClient.AddStandardResilienceHandler()
            .Configure((HttpStandardResilienceOptions resilience, IServiceProvider serviceProvider) =>
            {
                var options = serviceProvider.GetRequiredService<IOptions<OpenSkyOptions>>().Value;

                resilience.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(options.TotalTimeoutSeconds);
                resilience.AttemptTimeout.Timeout = TimeSpan.FromSeconds(options.AttemptTimeoutSeconds);
                resilience.Retry.MaxRetryAttempts = options.MaxRetries;
                resilience.Retry.Delay = TimeSpan.FromMilliseconds(200);

                // The standard retry treats 429 as transient. For OpenSky a 429 means "out of credits", usually for
                // hours: retrying in 200 ms can't succeed and just adds load. So: transient failures except 429.
                resilience.Retry.ShouldHandle = args => ValueTask.FromResult(
                    HttpClientResiliencePredicates.IsTransient(args.Outcome) &&
                    args.Outcome.Result?.StatusCode != HttpStatusCode.TooManyRequests);

                resilience.CircuitBreaker.FailureRatio = 0.5;
                resilience.CircuitBreaker.MinimumThroughput = 5;
                resilience.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
                resilience.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(15);

                resilience.RateLimiter.DefaultRateLimiterOptions.PermitLimit = options.MaxConcurrentRequests;
                resilience.RateLimiter.DefaultRateLimiterOptions.QueueLimit = options.MaxQueuedRequests;
            });

        // Added after the resilience handler, so it sits inside it: every attempt (including retries) goes through it,
        // and fetching a token counts against that attempt's timeout.
        openSkyClient.AddHttpMessageHandler<OpenSkyTokenHandler>();

        return services;
    }
}
