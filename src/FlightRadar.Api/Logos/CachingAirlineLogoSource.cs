using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Options;

namespace FlightRadar.Api.Logos;

/// <summary>
/// Decorator: caches each airline's logo (HybridCache: memory, plus Redis when configured) under "logo:{code}".
/// </summary>
/// <remarks>
/// Stored as raw bytes, which HybridCache handles natively (no JSON, no base64). "No logo" is cached as an empty
/// array, so airlines without a logo aren't asked about again on every view. Failures aren't cached.
/// </remarks>
public sealed class CachingAirlineLogoSource(
    IAirlineLogoSource inner,
    HybridCache cache,
    IOptions<LogoOptions> options) : IAirlineLogoSource
{
    private readonly HybridCacheEntryOptions _entryOptions = new()
    {
        Expiration = TimeSpan.FromHours(options.Value.CacheHours),
        LocalCacheExpiration = TimeSpan.FromHours(options.Value.CacheHours),
    };

    public async Task<byte[]?> GetLogoAsync(string code, CancellationToken cancellationToken)
    {
        var bytes = await cache.GetOrCreateAsync(
            CacheKey(code),
            (inner, code),
            static async (state, ct) => await state.inner.GetLogoAsync(state.code, ct) ?? [],
            _entryOptions,
            cancellationToken: cancellationToken);

        return bytes.Length > 0 ? bytes : null;
    }

    public static string CacheKey(string code) => $"logo:{code}";
}
