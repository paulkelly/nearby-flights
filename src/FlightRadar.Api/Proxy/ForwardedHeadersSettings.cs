using System.Net;

namespace FlightRadar.Api.Proxy;

/// <summary>Which reverse proxies may tell the API who the client is. Bound from "ForwardedHeaders".</summary>
public sealed class ForwardedHeadersSettings
{
    public const string SectionName = "ForwardedHeaders";

    /// <summary>
    /// Address ranges (CIDR, e.g. "172.30.0.0/24") of proxies whose X-Forwarded-For/-Proto headers are trusted, in
    /// addition to loopback (always trusted). Empty = only loopback, which is right when nothing sits in front of
    /// the API (e.g. Vite's dev proxy, which doesn't add these headers anyway).
    /// </summary>
    public string[] KnownNetworks { get; set; } = [];

    internal bool AllNetworksAreValid() => KnownNetworks.All(network => IPNetwork.TryParse(network, out _));
}
