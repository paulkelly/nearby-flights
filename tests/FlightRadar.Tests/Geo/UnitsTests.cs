using FlightRadar.Core.Geo;

namespace FlightRadar.Tests.Geo;

public sealed class UnitsTests
{
    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(1.852, 1.0)]
    [InlineData(100.0, 53.995680)]
    [InlineData(463.0, 250.0)] // adsb.lol's maximum radius
    public void KmToNauticalMiles_KnownValues_ReturnsExpected(double km, double expectedNm)
    {
        var nm = Units.KmToNauticalMiles(km);

        Assert.Equal(expectedNm, nm, tolerance: 1e-6);
    }
}
