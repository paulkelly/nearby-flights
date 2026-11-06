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

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(0.3048, 1.0)]
    [InlineData(152.4, 500.0)]       // OpenSky reports altitude in metres
    [InlineData(10668.0, 35000.0)]   // a typical cruise level, FL350
    public void MetresToFeet_KnownValues_ReturnsExpected(double metres, double expectedFeet)
    {
        Assert.Equal(expectedFeet, Units.MetresToFeet(metres), tolerance: 1e-6);
    }

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(1852.0 / 3600, 1.0)] // 1 knot = 1852 m per hour
    [InlineData(128.611, 250.0)]     // ~250 kt, the usual speed limit below 10,000 ft
    public void MetresPerSecondToKnots_KnownValues_ReturnsExpected(double metresPerSecond, double expectedKnots)
    {
        Assert.Equal(expectedKnots, Units.MetresPerSecondToKnots(metresPerSecond), tolerance: 1e-3);
    }

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(0.00508, 1.0)]       // 1 ft/min = 0.3048 m / 60 s
    [InlineData(-5.08, -1000.0)]     // a normal descent
    public void MetresPerSecondToFeetPerMinute_KnownValues_ReturnsExpected(double metresPerSecond, double expectedFpm)
    {
        Assert.Equal(expectedFpm, Units.MetresPerSecondToFeetPerMinute(metresPerSecond), tolerance: 1e-6);
    }
}
