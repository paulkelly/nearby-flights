using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace FlightRadar.Api.Data;

/// <summary>Saved locations were asked for, but this instance has no database configured.</summary>
public sealed class DatabaseNotConfiguredException()
    : InvalidOperationException("No database is configured (ConnectionStrings:Postgres).");

public static class DatabaseHttpContextExtensions
{
    /// <summary>
    /// This request's <see cref="AppDbContext"/>, or <see cref="DatabaseNotConfiguredException"/> (→ 503) if the app
    /// runs without a database. Used instead of an <c>AppDbContext</c> endpoint parameter, which minimal APIs would
    /// take for a JSON body when the service isn't registered.
    /// </summary>
    public static AppDbContext RequireDatabase(this HttpContext httpContext) =>
        httpContext.RequestServices.GetService<AppDbContext>() ?? throw new DatabaseNotConfiguredException();
}

/// <summary>
/// Turns "no database" into 503 Service Unavailable: either none is configured, or Postgres can't be reached right
/// now (Npgsql marks those failures transient: connection refused, timeouts). Anything else is a real bug and stays
/// a 500. Like <c>FlightDataUnavailableExceptionHandler</c>, runs inside UseExceptionHandler().
/// </summary>
public sealed partial class DatabaseUnavailableExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<DatabaseUnavailableExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is DatabaseNotConfiguredException)
        {
            // Expected on an instance without Postgres: nothing to log.
        }
        else if (IsTransientDatabaseFailure(exception))
        {
            LogUnreachable(logger, exception);
        }
        else
        {
            return false;
        }

        httpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Title = "Saved locations unavailable",
                Detail = "Saved locations can't be reached right now. Flights still work.",
            },
        });
    }

    /// <summary>
    /// A transient Npgsql failure anywhere in the chain. EF Core wraps them: in an InvalidOperationException ("likely
    /// due to a transient failure", from its execution strategy) for queries, a DbUpdateException for saves.
    /// </summary>
    private static bool IsTransientDatabaseFailure(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is NpgsqlException { IsTransient: true })
            {
                return true;
            }
        }
        return false;
    }

    // Warning, not Error: the health check reports it too, and it isn't a bug in our code.
    [LoggerMessage(Level = LogLevel.Warning, Message = "Database unreachable")]
    private static partial void LogUnreachable(ILogger logger, Exception exception);
}
