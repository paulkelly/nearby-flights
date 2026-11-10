using FlightRadar.Api.Watching;
using FlightRadar.Core.Geo;
using Microsoft.Extensions.Options;

namespace FlightRadar.Tests.Watching;

[Trait("Category", "Database")]
[Collection(RedisCollection.Name)]
public sealed class RedisPollClaimsTests(RedisFixture redis)
{
    // An area no other test uses, so their claims can't collide.
    private readonly SearchArea area = new(new GeoPoint(Random.Shared.NextDouble() * 80, Random.Shared.NextDouble() * 170), 25);

    private RedisPollClaims Claims() => new(redis.Connection, Options.Create(new WatchingOptions { ClaimSeconds = 8 }));

    [Fact]
    public async Task TryClaim_TwoInstances_OnlyTheFirstGetsIt()
    {
        Assert.True(await Claims().TryClaimAsync(area, default));
        Assert.False(await Claims().TryClaimAsync(area, default)); // another instance, same tick
    }

    [Fact]
    public async Task TryClaim_Always_LetsGoByItselfAfterClaimSeconds()
    {
        await Claims().TryClaimAsync(area, default);

        // PX: Redis deletes the key itself. Checked via its remaining time to live rather than by waiting 8 s.
        var ttl = await redis.Connection.GetDatabase().KeyTimeToLiveAsync(RedisPollClaims.KeyPrefix + WatchedAreaKey.Format(area));
        Assert.InRange(ttl!.Value, TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(8));
    }

    [Fact]
    public async Task TryClaim_DifferentAreas_AreIndependent()
    {
        var other = area with { RadiusKm = 50 };

        Assert.True(await Claims().TryClaimAsync(area, default));
        Assert.True(await Claims().TryClaimAsync(other, default));
    }
}
