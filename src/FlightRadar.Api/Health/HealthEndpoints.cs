using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FlightRadar.Api.Health;

public static class HealthTags
{
    /// <summary>Checks that decide whether this instance should receive traffic.</summary>
    public const string Ready = "ready";
}

/// <summary>
/// Two health endpoints answering different questions, the split Kubernetes and Azure Container Apps use:
/// <list type="bullet">
/// <item><c>/health/live</c>: is the process alive and serving HTTP? Runs no checks. If this fails, restart it.</item>
/// <item><c>/health/ready</c>: can it do its job? Runs checks tagged "ready" (Redis, when configured). If this
/// fails, stop sending it traffic until it recovers, but don't restart it.</item>
/// </list>
/// Status codes: Healthy and Degraded → 200, Unhealthy → 503.
/// </summary>
/// <remarks>
/// adsb.lol is deliberately not checked. If it goes down, every instance would fail together, the platform would
/// pull or restart all of them, and that fixes nothing; the circuit breaker already handles upstream outages and
/// users get a clear 502. Health checks should cover what restarting or rerouting <em>this</em> instance can fix.
/// <para>
/// Not public: nginx only forwards /api/, so these are reachable from inside the container (Docker's health
/// probe) and the Compose network only. Their detail (which dependency failed) is for operators, not the internet.
/// </para>
/// </remarks>
public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false, // no checks: answering at all is the test
            ResponseWriter = WriteJsonAsync,
        });

        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(HealthTags.Ready),
            ResponseWriter = WriteJsonAsync,
        });

        return app;
    }

    /// <summary>
    /// {"status":"Degraded","durationMs":3.1,"checks":{"redis":{"status":"Degraded","description":"…","durationMs":2.9}}}
    /// instead of the default plain "Degraded", so a human (or `docker inspect`) can see which check is unhappy.
    /// </summary>
    private static Task WriteJsonAsync(HttpContext context, HealthReport report) =>
        context.Response.WriteAsJsonAsync(new
        {
            status = report.Status.ToString(),
            durationMs = Math.Round(report.TotalDuration.TotalMilliseconds, 1),
            checks = report.Entries.ToDictionary(
                entry => entry.Key,
                entry => new
                {
                    status = entry.Value.Status.ToString(),
                    description = entry.Value.Description,
                    durationMs = Math.Round(entry.Value.Duration.TotalMilliseconds, 1),
                }),
        });
}
