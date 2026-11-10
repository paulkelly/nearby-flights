using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;

namespace FlightRadar.Tests.Health;

public sealed class HealthEndpointsTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    /// <summary>
    /// The app with Redis configured (so its health check is registered) but the Redis client replaced by a fake,
    /// so these tests behave the same whether or not a real Redis is running on this machine. No Postgres: tests run as
    /// Development, which loads the developer's user-secrets, where a real connection string may be.
    /// </summary>
    private HttpClient CreateClient(bool redisReachable) =>
        factory.WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:Redis", "redis.invalid:6379");
                builder.UseSetting("ConnectionStrings:Postgres", "");
                builder.ConfigureTestServices(services =>
                    services.AddSingleton<IDistributedCache>(new FakeDistributedCache(redisReachable)));
            })
            .CreateClient();

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Live_RegardlessOfRedis_Returns200Healthy(bool redisReachable)
    {
        var response = await CreateClient(redisReachable).GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal("Healthy", body.GetProperty("status").GetString());
        Assert.Empty(body.GetProperty("checks").EnumerateObject()); // liveness runs no dependency checks
    }

    [Fact]
    public async Task Ready_RedisReachable_Returns200Healthy()
    {
        var response = await CreateClient(redisReachable: true).GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal("Healthy", body.GetProperty("status").GetString());
        Assert.Equal("Healthy", body.GetProperty("checks").GetProperty("redis").GetProperty("status").GetString());
    }

    [Fact]
    public async Task Ready_RedisUnreachable_Returns200DegradedNotUnhealthy()
    {
        var response = await CreateClient(redisReachable: false).GetAsync("/health/ready");

        // 200, not 503: the API still serves flights without Redis, so it should keep receiving traffic.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal("Degraded", body.GetProperty("status").GetString());
        Assert.Equal("Degraded", body.GetProperty("checks").GetProperty("redis").GetProperty("status").GetString());
    }

    [Fact]
    public async Task Ready_NothingConfigured_Returns200HealthyWithNoChecks()
    {
        var client = factory.WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:Redis", "");
                builder.UseSetting("ConnectionStrings:Postgres", "");
            })
            .CreateClient();

        var body = await ReadJsonAsync(await client.GetAsync("/health/ready"));

        Assert.Equal("Healthy", body.GetProperty("status").GetString());
        Assert.Empty(body.GetProperty("checks").EnumerateObject());
    }

    [Fact]
    public async Task Ready_PostgresUnreachable_Returns200DegradedNotUnhealthy()
    {
        // Port 1 on this machine: nothing listens there, so the connection is refused at once (no Docker needed).
        var client = factory.WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:Redis", "");
                builder.UseSetting("ConnectionStrings:Postgres", "Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x");
            })
            .CreateClient();

        var response = await client.GetAsync("/health/ready");

        // Like Redis: without the database the API still serves flights, so it stays in service.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal("Degraded", body.GetProperty("status").GetString());
        Assert.Equal("Degraded", body.GetProperty("checks").GetProperty("postgres").GetProperty("status").GetString());
    }

    /// <summary>Answers every read with "not found", or throws like an unreachable Redis.</summary>
    private sealed class FakeDistributedCache(bool reachable) : IDistributedCache
    {
        private byte[]? Read() => reachable ? null : throw new InvalidOperationException("Redis is down (fake).");

        public byte[]? Get(string key) => Read();
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromResult(Read());
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => Read();
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            Read();
            return Task.CompletedTask;
        }
        public void Refresh(string key) => Read();
        public Task RefreshAsync(string key, CancellationToken token = default) => SetAsync(key, [], new(), token);
        public void Remove(string key) => Read();
        public Task RemoveAsync(string key, CancellationToken token = default) => SetAsync(key, [], new(), token);
    }
}
