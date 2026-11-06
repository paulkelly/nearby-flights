using System.Net;
using System.Text.Json;
using FlightRadar.Api.Flights;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FlightRadar.Tests.OpenApi;

public sealed class OpenApiDocumentTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    // WebApplicationFactory runs in the Development environment unless told otherwise.

    [Fact]
    public async Task OpenApiDocument_Development_DescribesFlightsEndpoint()
    {
        var client = factory.CreateClient();

        using var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));

        var getFlights = document.RootElement.GetProperty("paths").GetProperty("/api/flights").GetProperty("get");
        var responses = getFlights.GetProperty("responses").EnumerateObject().Select(r => r.Name);
        Assert.Equal(["200", "400", "502"], responses.Order());

        var parameters = getFlights.GetProperty("parameters").EnumerateArray().ToList();
        Assert.Equal(["lat", "lon", "radiusKm"], parameters.Select(p => p.GetProperty("name").GetString()));
        Assert.All(parameters, p => Assert.False(string.IsNullOrEmpty(p.GetProperty("description").GetString())));
    }

    [Fact]
    public async Task OpenApiDocument_Development_RadiusDescriptionStatesTheEnforcedLimit()
    {
        var client = factory.CreateClient();

        using var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));

        // The description's number is a separate text constant (attributes need constants); this catches the two
        // drifting apart when the limit changes.
        var radius = document.RootElement.GetProperty("paths").GetProperty("/api/flights").GetProperty("get")
            .GetProperty("parameters").EnumerateArray().Single(p => p.GetProperty("name").GetString() == "radiusKm");
        Assert.EndsWith(
            $"at most {FlightsEndpoints.MaxRadiusKm}.", radius.GetProperty("description").GetString());
    }

    [Fact]
    public async Task OpenApiDocument_Development_DescribesNumbersAsNumbersOnly()
    {
        var client = factory.CreateClient();

        using var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));

        // Without strict number handling these are ["number", "string"], which generates `number | string` in TS.
        var flightDto = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("FlightDto");
        Assert.Equal("number", flightDto.GetProperty("properties").GetProperty("lat").GetProperty("type").GetString());
    }

    [Theory]
    [InlineData("/openapi/v1.json")]
    [InlineData("/scalar")]
    public async Task ApiDocs_Production_AreNotExposed(string path)
    {
        var client = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Production")).CreateClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ScalarUi_Development_IsServed()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/scalar");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
    }
}
