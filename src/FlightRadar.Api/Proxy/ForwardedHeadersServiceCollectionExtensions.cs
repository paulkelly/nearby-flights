using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
// HttpOverrides has an older IPNetwork of its own (behind the now-obsolete KnownNetworks); use the .NET one.
using IPNetwork = System.Net.IPNetwork;

namespace FlightRadar.Api.Proxy;

public static class ForwardedHeadersServiceCollectionExtensions
{
    /// <summary>
    /// Behind nginx, every request arrives from nginx's address. nginx passes the real client in
    /// X-Forwarded-For (and http/https in X-Forwarded-Proto); app.UseForwardedHeaders() copies those into
    /// HttpContext.Connection.RemoteIpAddress and Request.Scheme, so logs (and rate limiting, stage 5e) see the
    /// client, not the proxy. This configures which proxies are believed.
    /// </summary>
    /// <remarks>
    /// X-Forwarded-For is just a request header: anyone can send one. So it's only honoured when the request
    /// comes directly from a trusted proxy (loopback, or <see cref="ForwardedHeadersSettings.KnownNetworks"/>),
    /// and by default only its last entry is used (ForwardLimit = 1): the address nginx itself saw. A client that
    /// sends a fake "X-Forwarded-For: 1.2.3.4" gets "1.2.3.4, &lt;their real IP&gt;" from nginx, and only the real
    /// IP counts.
    /// <para>
    /// With more proxies in a row (Azure: its ingress, nginx, its internal ingress) the entries are read from the
    /// right, one per proxy, up to <see cref="ForwardedHeadersSettings.ForwardLimit"/>, and only while the address
    /// that handed the request on is trusted: the first untrusted (public) address is the client. Never raise the
    /// limit where the client itself can be inside a trusted range (on Docker Desktop every browser arrives from the
    /// network's gateway, 172.30.0.1), or its own fake entries would be believed.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddForwardedHeadersSupport(this IServiceCollection services)
    {
        services.AddOptions<ForwardedHeadersSettings>()
            .BindConfiguration(ForwardedHeadersSettings.SectionName)
            .Validate(settings => settings.AllNetworksAreValid(), "ForwardedHeaders:KnownNetworks must be CIDR ranges, e.g. 172.30.0.0/24.")
            .Validate(settings => settings.ForwardLimit is >= 1 and <= 5, "ForwardedHeaders:ForwardLimit must be 1-5.")
            .ValidateOnStart();

        // Configures the options that the built-in app.UseForwardedHeaders() reads.
        services.AddOptions<ForwardedHeadersOptions>()
            .Configure<IOptions<ForwardedHeadersSettings>>((options, settings) =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                options.ForwardLimit = settings.Value.ForwardLimit; // 1 unless configured: see remarks

                // Loopback (127.0.0.1, ::1) is in KnownProxies by default and stays trusted.
                foreach (var network in settings.Value.KnownNetworks)
                {
                    options.KnownIPNetworks.Add(IPNetwork.Parse(network));
                }
            });

        return services;
    }
}
