using System.Collections.Concurrent;
using FlightRadar.Core.Enrichment;
using FlightRadar.Core.Geo;

namespace FlightRadar.Tests.Fakes;

/// <summary>
/// An <see cref="IRouteProvider"/> with scripted answers: known routes, callsigns that fail, and callsigns whose
/// answer is held back until the test releases it (to simulate a slow lookup).
/// </summary>
public sealed class FakeRouteProvider : IRouteProvider
{
    public Dictionary<string, FlightRoute> Routes { get; } = new();

    /// <summary>Callsigns whose lookup throws.</summary>
    public HashSet<string> Failing { get; } = [];

    /// <summary>Callsigns whose lookup waits for <see cref="Release"/>.</summary>
    public Dictionary<string, TaskCompletionSource> Slow { get; } = new();

    /// <summary>Every callsign asked for, in order. Thread-safe: lookups run concurrently.</summary>
    public ConcurrentQueue<string> Calls { get; } = new();

    /// <summary>Callsigns whose lookup ran to the end (not cancelled), in order. Thread-safe.</summary>
    public ConcurrentQueue<string> Finished { get; } = new();

    public async Task<FlightRoute?> GetRouteAsync(string callsign, CancellationToken cancellationToken)
    {
        Calls.Enqueue(callsign);

        if (Slow.TryGetValue(callsign, out var gate))
        {
            await gate.Task.WaitAsync(cancellationToken);
        }

        Finished.Enqueue(callsign);

        if (Failing.Contains(callsign))
        {
            throw new HttpRequestException($"{callsign}: simulated failure");
        }

        return Routes.GetValueOrDefault(callsign);
    }

    public FakeRouteProvider WithRoute(string callsign, params string[] icaoCodes)
    {
        Routes[callsign] = RouteVia(icaoCodes);
        return this;
    }

    public FakeRouteProvider WithSlowRoute(string callsign, params string[] icaoCodes)
    {
        WithRoute(callsign, icaoCodes);
        Slow[callsign] = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        return this;
    }

    public void Release(string callsign) => Slow[callsign].SetResult();

    /// <summary>A route through airports with these ICAO codes (positions and names are placeholders).</summary>
    public static FlightRoute RouteVia(params string[] icaoCodes) =>
        new(icaoCodes.Select((icao, i) => new RouteAirport(
            icao, icao[1..], $"{icao} Airport", $"{icao} City", "IE", new GeoPoint(50 + i, -6))).ToList());
}
