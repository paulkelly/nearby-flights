using FlightRadar.Api.Caching;
using FlightRadar.Api.Flights;
using FlightRadar.Api.Providers.AdsbLol;
using FlightRadar.Core.Flights;
using System.Text.Json.Serialization;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// ASP.NET Core's JSON defaults also accept numbers written as strings ("53.42"), and OpenAPI faithfully documents
// every number as "number or string". We never need that, so be strict: clients get plain `number` types.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict);

// Builds an OpenAPI description of every endpoint from its route, parameters and Results<...> return type.
builder.Services.AddOpenApi();

// Errors as RFC 9457 ProblemDetails JSON, with a 502 for upstream flight data failures.
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<FlightDataUnavailableExceptionHandler>();

builder.Services.AddAdsbLolFlightDataProvider();                          // the real source...
builder.Services.AddCachedFlightDataProvider<AdsbLolFlightDataProvider>(); // ...behind a short-lived cache
builder.Services.AddOptionalRedisCache(builder.Configuration);              // shared L2 for that cache, if configured
builder.Services.AddScoped<NearbyFlightsService>();

var app = builder.Build();

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

// API docs in Development only: no need to publish the API's internals from a production server.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();              // GET /openapi/v1.json
    app.MapScalarApiReference();   // GET /scalar: browsable docs that can send test requests
}

app.MapFlightsEndpoints();

app.Run();

// Makes the compiler-generated Program class visible to WebApplicationFactory<Program> in the tests.
public partial class Program;
