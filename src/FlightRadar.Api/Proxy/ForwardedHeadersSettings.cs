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

    /// <summary>
    /// How many proxies in a row may report the client: 1 behind nginx alone (Compose). Azure Container Apps puts its
    /// own proxy in front of nginx and another in front of the API, so there it's 3 (set in infra/main.bicep). Each
    /// hop is only believed if the one after it is in <see cref="KnownNetworks"/>, so a client's own fake entries,
    /// which sit before the first proxy's, are never reached.
    /// </summary>
    public int ForwardLimit { get; set; } = 1;

    internal bool AllNetworksAreValid() => KnownNetworks.All(network => IPNetwork.TryParse(network, out _));
}
