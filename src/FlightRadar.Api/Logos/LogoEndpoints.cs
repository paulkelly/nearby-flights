using System.ComponentModel;
using FlightRadar.Core.Enrichment;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace FlightRadar.Api.Logos;

public static partial class LogoEndpoints
{
    public const string Route = "/api/airlines/{code}/logo";

    /// <remarks>
    /// Not rate limited like /api/flights: one list view asks for a logo per airline on screen, which would use up a
    /// 20-request bucket at once. It's protected differently: only known airline codes reach the source, each is
    /// fetched once a day (server cache), browsers cache the answers too, and downloads are capped at a few at a time.
    /// </remarks>
    public static IEndpointRouteBuilder MapAirlineLogoEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(Route, GetLogoAsync)
            .WithTags("Airlines")
            .WithName("GetAirlineLogo")
            .WithSummary("An airline's logo (PNG), by ICAO airline code.")
            .Produces(StatusCodes.Status200OK, contentType: "image/png");

        return app;
    }

    // Logos are extras: when one can't be fetched the browser shows a code badge instead, so there's no
    // ProblemDetails body to read, just a status.
    private static async Task<Results<FileContentHttpResult, NotFound, StatusCodeHttpResult>> GetLogoAsync(
        [Description("ICAO airline code, e.g. EIN.")] string code,
        AirlineDirectory airlines,
        IAirlineLogoSource logos,
        IOptions<LogoOptions> options,
        HttpContext httpContext,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var headers = httpContext.Response.Headers;
        var cacheForBrowsers = $"public, max-age={options.Value.CacheHours * 3600}";

        // Only codes in our airline list reach the source: no fetching arbitrary paths on someone else's server.
        var upperCode = code.ToUpperInvariant();
        if (airlines.NameForCode(upperCode) is null)
        {
            headers.CacheControl = cacheForBrowsers;
            return TypedResults.NotFound();
        }

        byte[]? logo;
        try
        {
            logo = await logos.GetLogoAsync(upperCode, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // The source is down or slow. Don't let browsers cache this: the logo may well be there next time.
            LogLogoUnavailable(loggerFactory.CreateLogger(typeof(LogoEndpoints)), upperCode, exception.Message);
            headers.CacheControl = "no-store";
            return TypedResults.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        headers.CacheControl = cacheForBrowsers; // "no logo" is cached too, so the browser doesn't keep asking
        if (logo is null)
        {
            return TypedResults.NotFound();
        }

        // Tell the browser to trust our Content-Type and never "sniff" the bytes as something else (like HTML).
        headers.XContentTypeOptions = "nosniff";
        return TypedResults.File(logo, "image/png");
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Logo for {AirlineCode} unavailable: {Reason}")]
    private static partial void LogLogoUnavailable(ILogger logger, string airlineCode, string reason);
}
