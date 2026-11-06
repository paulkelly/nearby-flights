using System.Net;
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
            .ValidateOnStart();

        services.AddHttpClient<OpenSkyFlightDataProvider>((serviceProvider, client) =>
            {
                var options = serviceProvider.GetRequiredService<IOptions<OpenSkyOptions>>().Value;
                client.BaseAddress = options.BaseUrl;
                client.DefaultRequestHeaders.UserAgent.ParseAdd("FlightRadar/0.1");
                client.Timeout = Timeout.InfiniteTimeSpan; // the pipeline owns timeouts (see adsb.lol)
            })
            // The same standard pipeline as adsb.lol (see AdsbLolServiceCollectionExtensions for each layer).
            .AddStandardResilienceHandler()
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

        return services;
    }
}
