using FlightRadar.Core.Enrichment;

namespace FlightRadar.Tests.Enrichment;

public sealed class AirlineDirectoryTests
{
    private static readonly AirlineDirectory Directory = new(new Dictionary<string, string>
    {
        ["EIN"] = "Aer Lingus",
        ["RYR"] = "Ryanair",
    });

    // ---- AirlineCode: which callsigns are airline callsigns ----

    [Theory]
    [InlineData("EIN123", "EIN")]
    [InlineData("RYR4CZ", "RYR")]    // flight numbers can contain letters...
    [InlineData("EZY12AB", "EZY")]
    [InlineData("BAW1", "BAW")]      // ...or be a single digit
    [InlineData("ein123", "EIN")]    // not case-sensitive
    [InlineData(" EIN123 ", "EIN")]  // padding trimmed (adsb.lol pads callsigns to 8 characters)
    [InlineData("DLH12345", "DLH")]  // 8 characters, the maximum
    public void AirlineCode_AirlineCallsign_ReturnsTheCode(string callsign, string expected)
    {
        Assert.Equal(expected, AirlineDirectory.AirlineCode(callsign));
    }

    [Theory]
    [InlineData("EIGWY")]       // a registration (EI-GWY): no digit after the first three letters
    [InlineData("GABCD")]       // a UK registration
    [InlineData("N123AB")]      // a US registration: starts with one letter then digits
    [InlineData("EI123")]       // only two letters
    [InlineData("EIN")]         // no flight number
    [InlineData("EINA12")]      // a letter where the flight number should start
    [InlineData("DLH123456")]   // 9 characters: longer than any callsign
    [InlineData("EIN-123")]     // punctuation
    [InlineData("")]
    [InlineData(null)]
    public void AirlineCode_NotAnAirlineCallsign_ReturnsNull(string? callsign)
    {
        Assert.Null(AirlineDirectory.AirlineCode(callsign));
    }

    // ---- NameForCallsign ----

    [Fact]
    public void NameForCallsign_KnownAirline_ReturnsItsName()
    {
        Assert.Equal("Aer Lingus", Directory.NameForCallsign("EIN58P"));
    }

    [Theory]
    [InlineData("XXX123")]  // the shape of an airline callsign, but not a known code
    [InlineData("EIGWY")]   // starts with letters that aren't a code here, and isn't an airline callsign anyway
    [InlineData(null)]
    public void NameForCallsign_UnknownOrNotAnAirline_ReturnsNull(string? callsign)
    {
        Assert.Null(Directory.NameForCallsign(callsign));
    }

    [Fact]
    public void NameForCallsign_RegistrationStartingWithAnAirlineCode_ReturnsNull()
    {
        // "RYRAB" could be a registration-style callsign; only a digit after the code makes it an airline flight.
        Assert.Null(Directory.NameForCallsign("RYRAB"));
    }

    // ---- The bundled list ----

    [Fact]
    public void LoadBundled_Always_LoadsTheEmbeddedList()
    {
        var bundled = AirlineDirectory.LoadBundled();

        Assert.True(bundled.Count > 5000, $"Expected thousands of airlines, got {bundled.Count}.");
        Assert.Equal("Aer Lingus", bundled.NameForCallsign("EIN123"));
        Assert.Equal("Ryanair", bundled.NameForCallsign("RYR4CZ"));
    }

    [Fact]
    public void LoadBundled_Names_AreDecodedText()
    {
        // The source has some HTML-encoded names; the refresh script decodes them.
        var bundled = AirlineDirectory.LoadBundled();

        Assert.Equal("D & D Aviation", bundled.NameForCallsign("DDA1"));
    }
}
