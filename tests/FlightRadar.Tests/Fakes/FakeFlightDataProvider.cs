using FlightRadar.Core.Flights;
using FlightRadar.Core.Geo;

namespace FlightRadar.Tests.Fakes;

/// <summary>Returns canned flights (or throws), and records what it was asked for.</summary>
public sealed class FakeFlightDataProvider(params Flight[] flights) : IFlightDataProvider
{
    public Exception? ExceptionToThrow { get; init; }

    public List<(GeoPoint Centre, double RadiusKm)> Calls { get; } = [];

    public Task<IReadOnlyList<Flight>> GetFlightsAsync(
        GeoPoint centre, double radiusKm, CancellationToken cancellationToken)
    {
        Calls.Add((centre, radiusKm));
        return ExceptionToThrow is null
            ? Task.FromResult<IReadOnlyList<Flight>>(flights)
            : Task.FromException<IReadOnlyList<Flight>>(ExceptionToThrow);
    }

    /// <summary>A minimal flight at the given position.</summary>
    public static Flight FlightAt(string hex, double lat, double lon) => new()
    {
        Hex = hex,
        Position = new GeoPoint(lat, lon),
        PositionTimestamp = DateTimeOffset.UnixEpoch,
    };
}
