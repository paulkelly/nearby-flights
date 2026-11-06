using FlightRadar.Core.Flights;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

namespace FlightRadar.Api.Providers.AdsbLol;

public static class AdsbLolServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="AdsbLolFlightDataProvider"/> under its own type, configured from "Providers:AdsbLol".
    /// It isn't registered as <see cref="IFlightDataProvider"/>: the app wraps it in a cache that is.
    /// </summary>
    public static IServiceCollection AddAdsbLolFlightDataProvider(this IServiceCollection services)
    {
        services.AddOptions<AdsbLolOptions>()
            .BindConfiguration(AdsbLolOptions.SectionName)
            .ValidateDataAnnotations()
            .Validate(
                options => options.AttemptTimeoutSeconds < options.TotalTimeoutSeconds,
                "AttemptTimeoutSeconds must be less than TotalTimeoutSeconds, or a single attempt could use the whole budget.")
            .ValidateOnStart(); // bad config fails at startup, not on the first request

        // Typed client: IHttpClientFactory creates the HttpClient and pools the underlying connections.
        services.AddHttpClient<AdsbLolFlightDataProvider>((serviceProvider, client) =>
            {
                var options = serviceProvider.GetRequiredService<IOptions<AdsbLolOptions>>().Value;
                client.BaseAddress = options.BaseUrl;
                client.DefaultRequestHeaders.UserAgent.ParseAdd("FlightRadar/0.1");

                // HttpClient.Timeout wraps the *whole* handler pipeline, retries included, so any value here would
                // silently cut retries short. The resilience pipeline below owns all timeouts instead.
                client.Timeout = Timeout.InfiniteTimeSpan;
            })
            // Microsoft's recommended pipeline (built on Polly). Each request passes through, outermost first:
            // concurrency limit → total timeout → retry → circuit breaker → per-attempt timeout → the network.
            .AddStandardResilienceHandler()
            .Configure((HttpStandardResilienceOptions resilience, IServiceProvider serviceProvider) =>
            {
                var options = serviceProvider.GetRequiredService<IOptions<AdsbLolOptions>>().Value;

                resilience.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(options.TotalTimeoutSeconds);
                resilience.AttemptTimeout.Timeout = TimeSpan.FromSeconds(options.AttemptTimeoutSeconds);

                // Exponential backoff (~200 ms, ~400 ms, ...) with jitter: random spread so many clients that
                // failed together don't all retry at the same instant. Honours Retry-After on 429/503.
                resilience.Retry.MaxRetryAttempts = options.MaxRetries;
                resilience.Retry.Delay = TimeSpan.FromMilliseconds(200);

                // Circuit breaker: if at least half of 5+ attempts in 30 s fail, stop calling adsb.lol for 15 s.
                // While "open", calls fail instantly without touching the network: users get a fast 502 instead
                // of a slow one, and a struggling upstream isn't hammered. After 15 s one trial call is let
                // through ("half-open"); success closes the circuit again. The default minimum of 100 calls
                // would never trip at this app's traffic.
                resilience.CircuitBreaker.FailureRatio = 0.5;
                resilience.CircuitBreaker.MinimumThroughput = 5;
                resilience.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
                resilience.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(15);

                // Concurrency limit (a "bulkhead"): at most this many calls to adsb.lol in flight, a short queue behind
                // them, and the rest rejected at once with RateLimiterRejectedException (→ 502). The default is 1000
                // in flight, which at this app's scale means no limit at all. It's outermost, so a whole call, retries
                // included, holds one slot, and time spent queueing isn't taken from the 8 s budget.
                resilience.RateLimiter.DefaultRateLimiterOptions.PermitLimit = options.MaxConcurrentRequests;
                resilience.RateLimiter.DefaultRateLimiterOptions.QueueLimit = options.MaxQueuedRequests;
            });

        return services;
    }
}
