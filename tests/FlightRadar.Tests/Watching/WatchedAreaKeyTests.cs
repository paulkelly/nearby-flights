using FlightRadar.Api.Watching;
using FlightRadar.Core.Geo;

namespace FlightRadar.Tests.Watching;

public sealed class WatchedAreaKeyTests
{
    [Theory]
    [InlineData(53.4, -6.3, 25)]
    [InlineData(-33.8688, 151.2093, 1.5)]
    [InlineData(0.1 + 0.2, -179.99999999999997, 463)] // awkward doubles survive exactly ("R" formatting)
    public void FormatThenParse_Always_GivesTheSameArea(double lat, double lon, double radiusKm)
    {
        var area = new SearchArea(new GeoPoint(lat, lon), radiusKm);

        Assert.True(WatchedAreaKey.TryParse(WatchedAreaKey.Format(area), out var parsed));
        Assert.Equal(area, parsed);
    }

    [Fact]
    public void Format_Always_UsesInvariantDots()
    {
        var area = new SearchArea(new GeoPoint(53.5, -6.25), 12.5);

        Assert.Equal("53.5:-6.25:12.5", WatchedAreaKey.Format(area)); // never "53,5" on a German machine
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("53.4:-6.3")]          // too few parts
    [InlineData("53.4:-6.3:25:9")]     // too many
    [InlineData("north:-6.3:25")]      // not a number
    [InlineData("91:-6.3:25")]         // off the planet
    [InlineData("53.4:-6.3:0")]        // no radius
    [InlineData("NaN:-6.3:25")]
    public void TryParse_NotAnArea_ReturnsFalse(string? text)
    {
        Assert.False(WatchedAreaKey.TryParse(text, out _));
    }
}
