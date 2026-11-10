using System.Net;
using System.Text.Json;
using FlightRadar.Tests.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FlightRadar.Tests.Health;

[Trait("Category", "Database")]
[Collection(PostgresCollection.Name)]
public sealed class PostgresHealthTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Ready_PostgresReachable_Returns200Healthy()
    {
        string connectionString = await postgres.CreateDatabaseAsync();
        await using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Redis", "");
            builder.UseSetting("ConnectionStrings:Postgres", connectionString);
        });

        var response = await app.CreateClient().GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("Healthy", body.GetProperty("checks").GetProperty("postgres").GetProperty("status").GetString());
    }
}
