using System.Net;
using FlightRadar.Api.Logos;
using FlightRadar.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FlightRadar.Tests.Logos;

public sealed class HttpAirlineLogoSourceTests
{
    /// <summary>The 8-byte PNG signature plus a little data: enough to count as a PNG.</summary>
    public static readonly byte[] TinyPng = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];

    private const string First = "https://logos.example/first/{code}.png";
    private const string Second = "https://logos.example/second/{code}.png";

    private static HttpAirlineLogoSource Create(FakeHttpMessageHandler handler) =>
        new(new HttpClient(handler),
            Options.Create(new LogoOptions { UrlTemplates = [First, Second] }),
            NullLogger<HttpAirlineLogoSource>.Instance);

    /// <summary>A network where each URL answers with the given status and body; anything else is a 404.</summary>
    private static FakeHttpMessageHandler Network(params (string Url, HttpStatusCode Status, byte[] Body)[] answers) =>
        new((request, _) =>
        {
            var match = answers.FirstOrDefault(a => a.Url == request.RequestUri!.AbsoluteUri);
            return Task.FromResult(match.Url is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(match.Status) { Content = new ByteArrayContent(match.Body) });
        });

    [Fact]
    public async Task GetLogoAsync_FirstSourceHasIt_ReturnsItWithoutAskingTheSecond()
    {
        var network = Network(("https://logos.example/first/EIN.png", HttpStatusCode.OK, TinyPng));

        var logo = await Create(network).GetLogoAsync("EIN", CancellationToken.None);

        Assert.Equal(TinyPng, logo);
        Assert.Single(network.Requests);
    }

    [Fact]
    public async Task GetLogoAsync_OnlySecondSourceHasIt_FallsBackToIt()
    {
        var network = Network(("https://logos.example/second/EIN.png", HttpStatusCode.OK, TinyPng));

        var logo = await Create(network).GetLogoAsync("EIN", CancellationToken.None);

        Assert.Equal(TinyPng, logo);
        Assert.Equal(2, network.Requests.Count);
    }

    [Fact]
    public async Task GetLogoAsync_NoSourceHasIt_ReturnsNull()
    {
        Assert.Null(await Create(Network()).GetLogoAsync("EIN", CancellationToken.None));
    }

    [Fact]
    public async Task GetLogoAsync_NotAPng_IsIgnored()
    {
        // We serve these bytes from our own origin, so an HTML page in place of a logo must never get through.
        var html = "<html><script>alert(1)</script></html>"u8.ToArray();
        var network = Network(("https://logos.example/first/EIN.png", HttpStatusCode.OK, html));

        Assert.Null(await Create(network).GetLogoAsync("EIN", CancellationToken.None));
    }

    [Fact]
    public async Task GetLogoAsync_NotAPngInFirst_TriesTheSecond()
    {
        var network = Network(
            ("https://logos.example/first/EIN.png", HttpStatusCode.OK, "GIF89a"u8.ToArray()),
            ("https://logos.example/second/EIN.png", HttpStatusCode.OK, TinyPng));

        Assert.Equal(TinyPng, await Create(network).GetLogoAsync("EIN", CancellationToken.None));
    }

    [Fact]
    public async Task GetLogoAsync_SourceError_Throws()
    {
        // "Couldn't ask" must not look like "no logo", or it would be cached as an answer.
        var network = Network(("https://logos.example/first/EIN.png", HttpStatusCode.InternalServerError, []));

        await Assert.ThrowsAsync<HttpRequestException>(() => Create(network).GetLogoAsync("EIN", CancellationToken.None));
    }

    [Theory]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, true)]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, false)] // truncated signature
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, false)] // a JPEG
    [InlineData(new byte[0], false)]
    public void IsPng_ChecksTheSignature(byte[] bytes, bool expected)
    {
        Assert.Equal(expected, HttpAirlineLogoSource.IsPng(bytes));
    }
}
