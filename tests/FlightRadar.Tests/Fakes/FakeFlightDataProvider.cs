using FlightRadar.Core.Flights;
using FlightRadar.Core.Geo;

namespace FlightRadar.Tests.Fakes;

/// <summary>Returns canned flights (or throws), and records what it was asked for.</summary>
public sealed class FakeFlightDataProvider(params Flight[] flights) : IFlightDataProvider
{
    public Exception? ExceptionToThrow { get; init; }

    /// <summary>If set, every call waits for this task first, so a test can hold calls "in flight".</summary>
    public Task? Gate { get; init; }

    /// <summary>Locked on write: calls may arrive concurrently.</summary>
    public List<(GeoPoint Centre, double RadiusKm)> Calls { get; } = [];

    public async Task<IReadOnlyList<Flight>> GetFlightsAsync(
        GeoPoint centre, double radiusKm, CancellationToken cancellationToken)
    {
        lock (Calls)
        {
            Calls.Add((centre, radiusKm));
        }

        if (Gate is not null)
        {
            await Gate.WaitAsync(cancellationToken);
        }

        return ExceptionToThrow is null ? flights : throw ExceptionToThrow;
    }

    /// <summary>A minimal flight at the given position.</summary>
    public static Flight FlightAt(string hex, double lat, double lon) => new()
    {
        Hex = hex,
        Position = new GeoPoint(lat, lon),
        PositionTimestamp = DateTimeOffset.UnixEpoch,
    };
}
