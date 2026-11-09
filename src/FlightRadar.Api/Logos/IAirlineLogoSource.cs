namespace FlightRadar.Api.Logos;

/// <summary>Airline logos by ICAO airline code, as PNG bytes.</summary>
public interface IAirlineLogoSource
{
    /// <summary>The logo for <paramref name="code"/> ("EIN"), or null if the source has none.</summary>
    /// <exception cref="Exception">The source couldn't be asked: not the same as "no logo".</exception>
    Task<byte[]?> GetLogoAsync(string code, CancellationToken cancellationToken);
}
