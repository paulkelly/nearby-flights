using System.Collections.Concurrent;
using FlightRadar.Api.Logos;

namespace FlightRadar.Tests.Fakes;

/// <summary>An <see cref="IAirlineLogoSource"/> with scripted logos, which can also be made to fail.</summary>
public sealed class FakeAirlineLogoSource : IAirlineLogoSource
{
    public Dictionary<string, byte[]> Logos { get; } = new();

    public bool Failing { get; set; }

    public ConcurrentQueue<string> Calls { get; } = new();

    public Task<byte[]?> GetLogoAsync(string code, CancellationToken cancellationToken)
    {
        Calls.Enqueue(code);
        return Failing
            ? Task.FromException<byte[]?>(new HttpRequestException("simulated failure"))
            : Task.FromResult(Logos.GetValueOrDefault(code));
    }
}
