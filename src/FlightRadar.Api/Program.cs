using FlightRadar.Api.Flights;
using FlightRadar.Api.Providers.AdsbLol;
using FlightRadar.Core.Flights;

var builder = WebApplication.CreateBuilder(args);

// Errors as RFC 9457 ProblemDetails JSON, with a 502 for upstream flight data failures.
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<FlightDataUnavailableExceptionHandler>();

builder.Services.AddAdsbLolFlightDataProvider();
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

app.MapFlightsEndpoints();

app.Run();

// Makes the compiler-generated Program class visible to WebApplicationFactory<Program> in the tests.
public partial class Program;
