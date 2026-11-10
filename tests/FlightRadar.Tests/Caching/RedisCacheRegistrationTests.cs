using FlightRadar.Api.Caching;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace FlightRadar.Tests.Caching;

/// <summary>Registration only: resolving RedisCache doesn't connect, so no Redis server is needed.</summary>
public sealed class RedisCacheRegistrationTests
{
    private static ServiceProvider BuildServices(string? redisConnectionString)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Redis"] = redisConnectionString,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddOptionalRedisCache(configuration);
        return services.BuildServiceProvider();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void AddOptionalRedisCache_NoConnectionString_RegistersNothing(string? connectionString)
    {
        using var services = BuildServices(connectionString);

        Assert.Null(services.GetService<IDistributedCache>()); // HybridCache runs in-memory only
    }

    [Fact]
    public void AddOptionalRedisCache_ConnectionString_RegistersRedisAsDistributedCache()
    {
        using var services = BuildServices("redis:6379");

        // IsAssignableFrom: the library registers an internal subclass (RedisCacheImpl).
        Assert.IsAssignableFrom<RedisCache>(services.GetService<IDistributedCache>());
    }

    [Fact]
    public void AddOptionalRedisCache_ConnectionString_SharesOneConnection()
    {
        using var services = BuildServices("redis:6379");

        var options = services.GetRequiredService<IOptions<RedisCacheOptions>>().Value;

        // The cache gets its connection from the shared IConnectionMultiplexer (also used by the watched-areas
        // registry) instead of opening its own. Not called here: calling it would connect.
        Assert.Equal("flightradar:", options.InstanceName);
        Assert.NotNull(options.ConnectionMultiplexerFactory);
    }

    [Fact]
    public void CreateConfigurationOptions_Always_IsFailSafe()
    {
        var options = RedisCacheServiceCollectionExtensions.CreateConfigurationOptions("redis:6379");

        Assert.StartsWith("redis:6379,", options.ToString());           // the endpoint, then settings
        Assert.False(options.AbortOnConnectFail);                       // app starts without Redis
        Assert.Same(BacklogPolicy.FailFast, options.BacklogPolicy);     // no 5 s stalls
        Assert.Equal(1, options.ConnectRetry);                          // no 3× connect wait
        Assert.Equal(1_000, options.ConnectTimeout);
    }
}
