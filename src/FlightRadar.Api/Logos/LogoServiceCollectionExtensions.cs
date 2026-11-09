using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

namespace FlightRadar.Api.Logos;

public static class LogoServiceCollectionExtensions
{
    /// <summary>
    /// Registers airline logos: <see cref="CachingAirlineLogoSource"/> (as <see cref="IAirlineLogoSource"/>) →
    /// <see cref="HttpAirlineLogoSource"/>, configured from "Logos".
    /// </summary>
    public static IServiceCollection AddAirlineLogos(this IServiceCollection services)
    {
        services.AddOptions<LogoOptions>()
            .BindConfiguration(LogoOptions.SectionName)
            .ValidateDataAnnotations()
            .Validate(
                options => options.UrlTemplates.All(IsHttpsTemplate),
                "Each Logos:UrlTemplates entry must be an absolute https URL containing {code}.")
            .Validate(
                options => options.AttemptTimeoutSeconds < options.TotalTimeoutSeconds,
                "AttemptTimeoutSeconds must be less than TotalTimeoutSeconds.")
            .ValidateOnStart();

        services.AddHttpClient<HttpAirlineLogoSource>((serviceProvider, client) =>
            {
                var options = serviceProvider.GetRequiredService<IOptions<LogoOptions>>().Value;
                client.DefaultRequestHeaders.UserAgent.ParseAdd("FlightRadar/0.1");
                client.Timeout = Timeout.InfiniteTimeSpan; // the resilience pipeline owns timeouts
                // Reading a bigger body than this throws, so an unexpected huge response can't fill memory.
                client.MaxResponseContentBufferSize = options.MaxBytes;
            })
            .AddStandardResilienceHandler()
            .Configure((HttpStandardResilienceOptions resilience, IServiceProvider serviceProvider) =>
            {
                var options = serviceProvider.GetRequiredService<IOptions<LogoOptions>>().Value;
                resilience.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(options.TotalTimeoutSeconds);
                resilience.AttemptTimeout.Timeout = TimeSpan.FromSeconds(options.AttemptTimeoutSeconds);
                resilience.Retry.MaxRetryAttempts = options.MaxRetries;
                resilience.Retry.Delay = TimeSpan.FromMilliseconds(200);
                resilience.CircuitBreaker.FailureRatio = 0.5;
                resilience.CircuitBreaker.MinimumThroughput = 5;
                resilience.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
                resilience.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(30);
                resilience.RateLimiter.DefaultRateLimiterOptions.PermitLimit = options.MaxConcurrentRequests;
                resilience.RateLimiter.DefaultRateLimiterOptions.QueueLimit = options.MaxQueuedRequests;
            });

        services.AddHybridCache();
        services.AddTransient<IAirlineLogoSource>(serviceProvider => new CachingAirlineLogoSource(
            serviceProvider.GetRequiredService<HttpAirlineLogoSource>(),
            serviceProvider.GetRequiredService<HybridCache>(),
            serviceProvider.GetRequiredService<IOptions<LogoOptions>>()));

        return services;
    }

    private static bool IsHttpsTemplate(string template) =>
        template.Contains("{code}", StringComparison.Ordinal)
        && Uri.TryCreate(template.Replace("{code}", "EIN", StringComparison.Ordinal), UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps;
}
