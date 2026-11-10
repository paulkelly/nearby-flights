using System.ComponentModel.DataAnnotations;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace FlightRadar.Api.RateLimiting;

public static partial class RateLimitingServiceCollectionExtensions
{
    /// <summary>The policy name endpoints opt into with <c>.RequireRateLimiting(...)</c>.</summary>
    public const string PerClientPolicy = "per-client";

    /// <summary>The much stricter policy for creating owners (see <see cref="NewOwnerRateLimitOptions"/>).</summary>
    public const string NewOwnerPolicy = "new-owner";

    /// <summary>
    /// ASP.NET Core's built-in rate limiter with two policies: a token bucket per client IP (/api/flights, saved
    /// locations) and a fixed window per client IP for creating owners. Only endpoints that ask for one are limited;
    /// /health never is, so Docker's probe always gets through.
    /// </summary>
    /// <remarks>
    /// The client IP is the real one thanks to UseForwardedHeaders (stage 5d); without it every user would share
    /// nginx's bucket. Buckets live in this process's memory, so with several API instances each has its own and the
    /// effective limit multiplies (a Redis-backed limiter would share them: see the roadmap).
    /// </remarks>
    public static IServiceCollection AddClientRateLimiting(this IServiceCollection services)
    {
        services.AddOptions<ClientRateLimitOptions>()
            .BindConfiguration(ClientRateLimitOptions.SectionName)
            .ValidateDataAnnotations()
            // ValidateDataAnnotations checks only the top level, not the nested NewOwners section.
            .Validate(
                options => Validator.TryValidateObject(
                    options.NewOwners, new ValidationContext(options.NewOwners), null, validateAllProperties: true),
                "RateLimiting:NewOwners: PermitLimit must be 1-1000 and WindowMinutes 1-1440.")
            .ValidateOnStart();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = WriteRejectionAsync;

            options.AddPolicy(PerClientPolicy, httpContext =>
            {
                var settings = httpContext.RequestServices.GetRequiredService<IOptions<ClientRateLimitOptions>>().Value;

                // One bucket per partition key. Created on first use and dropped again after a while unused, so
                // memory follows active clients, not every IP ever seen.
                return RateLimitPartition.GetTokenBucketLimiter(ClientKey(httpContext), _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit = settings.TokenLimit,
                    TokensPerPeriod = settings.TokensPerPeriod,
                    ReplenishmentPeriod = TimeSpan.FromSeconds(settings.ReplenishmentPeriodSeconds),
                    AutoReplenishment = true,
                    QueueLimit = 0, // reject at once rather than hold requests open waiting for a token
                });
            });

            options.AddPolicy(NewOwnerPolicy, httpContext =>
            {
                var settings = httpContext.RequestServices.GetRequiredService<IOptions<ClientRateLimitOptions>>()
                    .Value.NewOwners;

                return RateLimitPartition.GetFixedWindowLimiter(ClientKey(httpContext), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = settings.PermitLimit,
                    Window = TimeSpan.FromMinutes(settings.WindowMinutes),
                    QueueLimit = 0,
                });
            });
        });

        return services;
    }

    /// <summary>
    /// The client's IP as the partition key. IPv4 addresses can arrive as IPv6-mapped ("::ffff:203.0.113.7"); that's
    /// the same client, so normalise it. No address at all (unusual: some test hosts) shares one "unknown" bucket,
    /// which errs on the side of limiting.
    /// </summary>
    internal static string ClientKey(HttpContext httpContext) =>
        httpContext.Connection.RemoteIpAddress is { } ip
            ? (ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip).ToString()
            : "unknown";

    /// <summary>429 as ProblemDetails, with Retry-After saying when the next token arrives.</summary>
    private static async ValueTask WriteRejectionAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var httpContext = context.HttpContext;

        int? retryAfterSeconds = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
            ? Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds))
            : null;
        if (retryAfterSeconds is { } seconds)
        {
            httpContext.Response.Headers.RetryAfter = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        var logger = httpContext.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("FlightRadar.Api.RateLimiting");
        LogRejected(logger, ClientKey(httpContext), httpContext.Request.Path, retryAfterSeconds);

        await httpContext.RequestServices.GetRequiredService<IProblemDetailsService>().TryWriteAsync(
            new ProblemDetailsContext
            {
                HttpContext = httpContext,
                ProblemDetails = new ProblemDetails
                {
                    Status = StatusCodes.Status429TooManyRequests,
                    Title = "Too many requests",
                    Detail = retryAfterSeconds is { } s
                        ? $"Slow down: try again in {s} s."
                        : "Slow down and try again shortly.",
                },
            });
    }

    // Warning: worth noticing (a misbehaving client, or limits set too low), but not a fault in the API.
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Rate limit reached for {ClientIp} on {Path}; retry after {RetryAfterSeconds} s")]
    private static partial void LogRejected(ILogger logger, string clientIp, string path, int? retryAfterSeconds);
}
