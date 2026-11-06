using Serilog;
using Serilog.Events;

namespace FlightRadar.Api.Logging;

public static class LoggingExtensions
{
    /// <summary>
    /// Serilog as the logging provider. Everything that logs through <see cref="ILogger{T}"/> (our code, ASP.NET
    /// Core, HttpClient, Polly, HybridCache, Redis) now goes through it.
    /// </summary>
    public static IServiceCollection AddAppLogging(this IServiceCollection services, IConfiguration configuration)
    {
        return services.AddSerilog((serviceProvider, logger) =>
        {
            logger
                .ReadFrom.Configuration(configuration)    // levels, overrides, console format: appsettings*.json
                .ReadFrom.Services(serviceProvider)
                .Enrich.FromLogContext()                  // properties added for a scope, e.g. by request logging
                .Enrich.WithProperty("Application", "FlightRadar.Api");

            // Optional, like Redis: only if a Seq server is configured. The sink batches events in the background,
            // so a missing or slow Seq never delays a request; if it's unreachable, events are retried then dropped.
            if (configuration["Seq:ServerUrl"] is { Length: > 0 } seqUrl)
            {
                logger.WriteTo.Seq(seqUrl);
            }
        });
    }

    /// <summary>
    /// One event per request: "HTTP GET /api/flights responded 200 in 12.3 ms", with the parts as properties.
    /// </summary>
    public static IApplicationBuilder UseAppRequestLogging(this IApplicationBuilder app)
    {
        return app.UseSerilogRequestLogging(options =>
        {
            // The query string is deliberately not logged: lat/lon can be someone's exact location ("Use my
            // location"), which is personal data. Logs are kept, copied and read by more people than the database.
            // The cache logs the snapped (~10 km) area instead, which is enough to diagnose problems.
            options.EnrichDiagnosticContext = (diagnostics, httpContext) =>
                diagnostics.Set("ClientIp", httpContext.Connection.RemoteIpAddress?.ToString());

            // The default is 5xx → Error, everything else → Information. Changes:
            // - 502/503/504 → Warning: an upstream outage isn't a bug in our code (the 502 handler logs it at
            //   Warning too). Error is kept for 500s, which are.
            // - Health probes (stage 5d piece 3) → Verbose, so a check every few seconds doesn't flood the logs.
            options.GetLevel = (httpContext, _, exception) =>
                httpContext.Response.StatusCode switch
                {
                    StatusCodes.Status502BadGateway or StatusCodes.Status503ServiceUnavailable
                        or StatusCodes.Status504GatewayTimeout => LogEventLevel.Warning,
                    >= 500 => LogEventLevel.Error,
                    _ when exception is not null => LogEventLevel.Error,
                    _ when httpContext.Request.Path.StartsWithSegments("/health") => LogEventLevel.Verbose,
                    _ => LogEventLevel.Information,
                };
        });
    }
}
