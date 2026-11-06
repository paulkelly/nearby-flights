namespace FlightRadar.Tests.Fakes;

/// <summary>An IHttpClientFactory that hands out clients over one given handler, whatever name is asked for.</summary>
public sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}