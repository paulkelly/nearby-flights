using FlightRadar.Core.Flights;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace FlightRadar.Api.Flights;

/// <summary>
/// Turns <see cref="FlightDataUnavailableException"/> into 502 Bad Gateway: "a server we depend on failed",
/// as opposed to 500, "we have a bug". Runs inside the UseExceptionHandler() middleware.
/// </summary>
public sealed class FlightDataUnavailableExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<FlightDataUnavailableExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not FlightDataUnavailableException)
        {
            return false; // not ours: let the next handler (or the default 500) deal with it
        }

        // Exceptions handled here are no longer logged by the middleware, so log it ourselves.
        // Warning, not Error: an upstream outage is expected now and then and isn't a bug in our code.
        logger.LogWarning(exception, "Flight data provider unavailable");

        httpContext.Response.StatusCode = StatusCodes.Status502BadGateway;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status502BadGateway,
                Title = "Flight data unavailable",
                // Fixed text: the exception message can contain upstream details clients shouldn't see.
                Detail = "The flight data source failed or timed out. Try again shortly.",
            },
        });
    }
}
