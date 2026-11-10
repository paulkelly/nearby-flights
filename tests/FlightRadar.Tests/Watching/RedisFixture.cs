using FlightRadar.Api.Caching;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace FlightRadar.Tests.Watching;

/// <summary>
/// One real, throwaway Redis (Testcontainers) shared by the "Redis" collection, like <c>PostgresFixture</c>. Tests
/// keep apart by using their own keys (<see cref="NewKey"/>) rather than separate servers. Needs Docker.
/// </summary>
public sealed class RedisFixture : IAsyncLifetime
{
    // The same image as compose.yaml.
    private readonly RedisContainer container = new RedisBuilder("redis:8.10-alpine").Build();

    public IConnectionMultiplexer Connection { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await container.StartAsync();
        // Configured exactly like the app's connection.
        Connection = await ConnectionMultiplexer.ConnectAsync(
            RedisCacheServiceCollectionExtensions.CreateConfigurationOptions(container.GetConnectionString()));
    }

    public async Task DisposeAsync()
    {
        await Connection.DisposeAsync();
        await container.DisposeAsync();
    }

    /// <summary>A key no other test uses.</summary>
    public static string NewKey() => $"test:{Guid.NewGuid():N}";
}

[CollectionDefinition(Name)]
public sealed class RedisCollection : ICollectionFixture<RedisFixture>
{
    public const string Name = "Redis";
}
