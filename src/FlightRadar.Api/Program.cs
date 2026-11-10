using FlightRadar.Api.Caching;
using FlightRadar.Api.Data;
using FlightRadar.Api.Flights;
using FlightRadar.Api.Health;
using FlightRadar.Api.Logging;
using FlightRadar.Api.Logos;
using FlightRadar.Api.Providers;
using FlightRadar.Api.Proxy;
using FlightRadar.Api.RateLimiting;
using FlightRadar.Api.Routes;
using FlightRadar.Core.Enrichment;
using FlightRadar.Core.Flights;
using System.Text.Json.Serialization;
using Scalar.AspNetCore;
using Serilog;

// A plain console logger for failures before the configured logger exists (e.g. an exception while registering
// services). Once the app is built, Serilog's configured logger takes over: from then on, startup failures such as
// invalid options are logged by the host itself ("Hosting failed to start", as JSON in containers).
Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Serilog replaces the built-in console logger. Levels, overrides and the console format come from the
    // "Serilog" section of appsettings*.json; Seq is added only if Seq:ServerUrl is set.
    builder.Services.AddAppLogging(builder.Configuration);

    // Behind nginx: take the client's real IP and scheme from X-Forwarded-* headers, but only from trusted proxies.
    builder.Services.AddForwardedHeadersSupport();

    // Token bucket per client IP for /api/flights (needs the real IP, hence after forwarded headers).
    builder.Services.AddClientRateLimiting();

    // ASP.NET Core's JSON defaults also accept numbers written as strings ("53.42"), and OpenAPI faithfully
    // documents every number as "number or string". We never need that, so be strict: clients get `number` types.
    builder.Services.ConfigureHttpJsonOptions(options =>
        options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict);

    // Builds an OpenAPI description of every endpoint from its route, parameters and Results<...> return type.
    builder.Services.AddOpenApi();

    // Errors as RFC 9457 ProblemDetails JSON, with a 502 for upstream flight data failures.
    builder.Services.AddProblemDetails();
    builder.Services.AddExceptionHandler<FlightDataUnavailableExceptionHandler>();

    // adsb.lol and OpenSky; Providers:Active picks which one serves requests, behind a short-lived cache.
    builder.Services.AddFlightDataProviders(builder.Configuration);
    builder.Services.AddOptionalRedisCache(builder.Configuration);              // shared L2 for that cache, if configured
    builder.Services.AddScoped<NearbyFlightsService>();
    // Loaded now rather than on first use, so a missing or broken airline list fails at startup.
    builder.Services.AddSingleton(AirlineDirectory.LoadBundled());
    builder.Services.AddSingleton(AirportDirectory.LoadBundled());
    builder.Services.AddRouteLookup();
    builder.Services.AddAirlineLogos();

    // Postgres, for what users keep (saved locations). Optional like Redis: without ConnectionStrings:Postgres the
    // API runs without it. The schema is applied by the separate `migrate` container, not by the app (see compose.yaml).
    builder.Services.AddOptionalDatabase(builder.Configuration);

    // /health/live and /health/ready (see HealthEndpoints). Checks are added by the features they cover, e.g. Redis.
    builder.Services.AddHealthChecks();

    var app = builder.Build();

    // Before everything else, so request logging (and later rate limiting) see the client, not nginx.
    app.UseForwardedHeaders();

    // One summary event per request (method, path, status, duration) instead of several framework lines. Early in
    // the pipeline so it times everything and sees the final status, including errors turned into 502s below.
    app.UseAppRequestLogging();

    // Unhandled exceptions → ProblemDetails: 500, or 502 via our handler.
    // BadHttpRequestException (e.g. "?lat=abc") carries its own status (400). In Development, minimal APIs throw it
    // instead of quietly returning 400, so without this selector bad input would show up as a 500 locally.
    app.UseExceptionHandler(new ExceptionHandlerOptions
    {
        StatusCodeSelector = exception => exception is BadHttpRequestException badRequest
            ? badRequest.StatusCode
            : StatusCodes.Status500InternalServerError,
    });
    app.UseStatusCodePages();   // bodiless error responses (e.g. 404) → ProblemDetails too

    // Rejects over-limit requests with 429 before they reach the endpoint (or the cache, or adsb.lol).
    app.UseRateLimiter();

    // API docs in Development only: no need to publish the API's internals from a production server.
    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();              // GET /openapi/v1.json
        app.MapScalarApiReference();   // GET /scalar: browsable docs that can send test requests
    }

    app.MapFlightsEndpoints();
    app.MapAirlineLogoEndpoints();
    app.MapHealthEndpoints();

    app.Run();
}
// HostAbortedException is how WebApplicationFactory (the tests) stops Program after building the app; it isn't a
// real failure, so let it through unlogged.
catch (Exception ex) when (ex is not HostAbortedException)
{
    // Covers failures the host doesn't log itself (before it exists). Rethrown so the process still exits non-zero
    // and anything hosting Program (WebApplicationFactory in the tests) still sees the original exception.
    Log.Fatal(ex, "The API stopped unexpectedly");
    throw;
}
finally
{
    Log.CloseAndFlush(); // Sinks such as Seq send in batches; flush the last ones before the process exits.
}

// Makes the compiler-generated Program class visible to WebApplicationFactory<Program> in the tests.
public partial class Program;
