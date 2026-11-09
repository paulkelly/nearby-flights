namespace FlightRadar.Core.Enrichment;

/// <summary>A source of flight routes by callsign. Like IFlightDataProvider, swappable and cacheable.</summary>
public interface IRouteProvider
{
    /// <summary>The route flown under <paramref name="callsign"/>, or null if the source doesn't know one.</summary>
    /// <param name="callsign">An airline callsign, e.g. "EIN123": upper case, no padding.</param>
    /// <exception cref="Exception">The source couldn't be asked (network error, timeout...): not the same as "no route".</exception>
    Task<FlightRoute?> GetRouteAsync(string callsign, CancellationToken cancellationToken);
}
