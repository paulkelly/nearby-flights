using FlightRadar.Api.Providers.AdsbLol;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAdsbLolFlightDataProvider();

var app = builder.Build();

app.MapGet("/", () => "Hello World!");

app.Run();
