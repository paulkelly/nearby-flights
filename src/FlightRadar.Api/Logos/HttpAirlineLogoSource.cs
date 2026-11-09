using System.Net;
using Microsoft.Extensions.Options;

namespace FlightRadar.Api.Logos;

/// <summary>
/// Downloads logos from the URLs in <see cref="LogoOptions.UrlTemplates"/>, trying each in turn until one has the
/// airline (a 404 means "not here, try the next").
/// </summary>
/// <remarks>
/// <para>
/// The default source is the Jxck-S/airline-logos collection on GitHub. The logos belong to their airlines; the
/// collection offers them for identifying airlines in flight trackers, which is how they're used here. They are
/// fetched at runtime and cached, never committed to this repository.
/// </para>
/// <para>
/// What we receive is served to browsers from <i>our</i> origin, so it's checked to really be a PNG (by its
/// first bytes, not the Content-Type a server claims). Anything else, say an HTML page from a misbehaving source,
/// would otherwise run as part of our site. Size is capped by the client's MaxResponseContentBufferSize.
/// </para>
/// </remarks>
public sealed partial class HttpAirlineLogoSource(
    HttpClient httpClient,
    IOptions<LogoOptions> options,
    ILogger<HttpAirlineLogoSource> logger) : IAirlineLogoSource
{
    /// <summary>Every PNG file starts with these 8 bytes.</summary>
    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public async Task<byte[]?> GetLogoAsync(string code, CancellationToken cancellationToken)
    {
        foreach (var template in options.Value.UrlTemplates)
        {
            var url = template.Replace("{code}", Uri.EscapeDataString(code), StringComparison.Ordinal);

            using var response = await httpClient.GetAsync(url, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                continue; // this source doesn't have it; try the next
            }

            response.EnsureSuccessStatusCode();
            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);

            if (!IsPng(bytes))
            {
                LogNotAPng(code, url, bytes.Length);
                continue;
            }

            return bytes;
        }

        return null;
    }

    public static bool IsPng(ReadOnlySpan<byte> bytes) => bytes.StartsWith(PngSignature);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Logo for {AirlineCode} from {Url} isn't a PNG ({Length} bytes); ignored")]
    private partial void LogNotAPng(string airlineCode, string url, int length);
}
