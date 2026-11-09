using FlightRadar.Core.Flights;

namespace FlightRadar.Tests.Flights;

public sealed class CallsignsTests
{
    [Theory]
    [InlineData("EIN58P  ", "EIN58P")] // adsb.lol pads to 8 characters
    [InlineData("EIGWY", "EIGWY")]
    [InlineData("DAA OPS4", "DAA OPS4")] // inner spaces are part of some callsigns (airport vehicles)
    [InlineData("N100", "N100")]         // zeros are fine as part of a callsign
    public void Clean_RealCallsign_IsTrimmed(string callsign, string expected)
    {
        Assert.Equal(expected, Callsigns.Clean(callsign));
    }

    [Theory]
    [InlineData("00000000")] // placeholder sent by some ground transmitters
    [InlineData("0000")]
    [InlineData(" 0000000 ")]
    [InlineData("        ")]
    [InlineData("")]
    [InlineData(null)]
    public void Clean_NoRealCallsign_ReturnsNull(string? callsign)
    {
        Assert.Null(Callsigns.Clean(callsign));
    }
}
