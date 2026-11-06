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
    /// and only its last entry is used (ForwardLimit = 1): the address nginx itself saw. A client that sends a fake
    /// "X-Forwarded-For: 1.2.3.4" gets "1.2.3.4, &lt;their real IP&gt;" from nginx, and only the real IP counts.
    /// </remarks>
    public static IServiceCollection AddForwardedHeadersSupport(this IServiceCollection services)
    {
        services.AddOptions<ForwardedHeadersSettings>()
            .BindConfiguration(ForwardedHeadersSettings.SectionName)
            .Validate(settings => settings.AllNetworksAreValid(), "ForwardedHeaders:KnownNetworks must be CIDR ranges, e.g. 172.30.0.0/24.")
            .ValidateOnStart();

        // Configures the options that the built-in app.UseForwardedHeaders() reads.
        services.AddOptions<ForwardedHeadersOptions>()
            .Configure<IOptions<ForwardedHeadersSettings>>((options, settings) =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                options.ForwardLimit = 1; // the default, stated because the security argument above depends on it

                // Loopback (127.0.0.1, ::1) is in KnownProxies by default and stays trusted.
                foreach (var network in settings.Value.KnownNetworks)
                {
                    options.KnownIPNetworks.Add(IPNetwork.Parse(network));
                }
            });

        return services;
    }
}
