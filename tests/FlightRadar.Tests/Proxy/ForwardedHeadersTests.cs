using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace FlightRadar.Tests.Proxy;

/// <summary>
/// The real pipeline, with requests sent straight into the test server so each test can choose the address the
/// request "arrives from": nginx inside the trusted network, or anything else. No endpoint is needed: the
/// returned HttpContext shows what UseForwardedHeaders made of the request.
/// </summary>
public sealed class ForwardedHeadersTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string TrustedNetwork = "172.30.0.0/24";
    private static readonly IPAddress Nginx = IPAddress.Parse("172.30.0.5");           // inside TrustedNetwork
    private static readonly IPAddress Stranger = IPAddress.Parse("198.51.100.9");      // anywhere else
    private const string RealClient = "203.0.113.7";

    private WebApplicationFactory<Program> WithTrustedNetwork(string network = TrustedNetwork) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration(config =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ForwardedHeaders:KnownNetworks:0"] = network,
            })));

    private static Task<HttpContext> SendAsync(
        WebApplicationFactory<Program> app, IPAddress from, string? forwardedFor, string? forwardedProto = null) =>
        app.Server.SendAsync(context =>
        {
            context.Request.Method = HttpMethods.Get;
            context.Request.Path = "/not-an-endpoint"; // the middleware runs for every request; a 404 is fine
            context.Connection.RemoteIpAddress = from;
            if (forwardedFor is not null) context.Request.Headers["X-Forwarded-For"] = forwardedFor;
            if (forwardedProto is not null) context.Request.Headers["X-Forwarded-Proto"] = forwardedProto;
        });

    [Fact]
    public async Task Request_FromTrustedProxy_UsesForwardedClientAndScheme()
    {
        var context = await SendAsync(WithTrustedNetwork(), from: Nginx, forwardedFor: RealClient, forwardedProto: "https");

        Assert.Equal(IPAddress.Parse(RealClient), context.Connection.RemoteIpAddress);
        Assert.Equal("https", context.Request.Scheme);
    }

    [Fact]
    public async Task Request_FromTrustedProxyAsIPv4MappedIPv6_IsStillTrusted()
    {
        // Kestrel in a container reports peers as "::ffff:172.30.0.5"; that must still match the IPv4 range.
        var context = await SendAsync(WithTrustedNetwork(), from: Nginx.MapToIPv6(), forwardedFor: RealClient);

        Assert.Equal(IPAddress.Parse(RealClient), context.Connection.RemoteIpAddress);
    }

    [Fact]
    public async Task Request_FromUntrustedAddress_IgnoresForwardedHeaders()
    {
        // Someone reaching the API directly (not through nginx) can't pretend to be another client.
        var context = await SendAsync(WithTrustedNetwork(), from: Stranger, forwardedFor: RealClient, forwardedProto: "https");

        Assert.Equal(Stranger, context.Connection.RemoteIpAddress);
        Assert.Equal("http", context.Request.Scheme);
    }

    [Fact]
    public async Task Request_WithSpoofedEntryBeforeTheRealClient_UsesOnlyTheLastEntry()
    {
        // The client sent "X-Forwarded-For: 1.2.3.4"; nginx appended the address it actually saw.
        var context = await SendAsync(WithTrustedNetwork(), from: Nginx, forwardedFor: $"1.2.3.4, {RealClient}");

        Assert.Equal(IPAddress.Parse(RealClient), context.Connection.RemoteIpAddress);
    }

    [Fact]
    public async Task Request_NoKnownNetworksConfigured_TrustsOnlyLoopback()
    {
        // The default (e.g. `dotnet run` behind Vite's proxy): nginx's network isn't trusted unless configured...
        var fromNginx = await SendAsync(factory, from: Nginx, forwardedFor: RealClient);
        Assert.Equal(Nginx, fromNginx.Connection.RemoteIpAddress);

        // ...but a proxy on the same machine is.
        var fromLoopback = await SendAsync(factory, from: IPAddress.Loopback, forwardedFor: RealClient);
        Assert.Equal(IPAddress.Parse(RealClient), fromLoopback.Connection.RemoteIpAddress);
    }

    [Fact]
    public void Startup_InvalidKnownNetwork_FailsFast()
    {
        var exception = Assert.ThrowsAny<Exception>(() => WithTrustedNetwork("not-a-network").CreateClient());

        Assert.Contains("ForwardedHeaders:KnownNetworks", exception.ToString());
    }
}
