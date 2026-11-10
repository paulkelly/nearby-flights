using FlightRadar.Api.Locations;

namespace FlightRadar.Tests.Locations;

public sealed class SavedLocationRulesTests
{
    private static readonly SavedLocationRequest Home = new("Home", 53.42134, -6.27007, 10);

    [Fact]
    public void Validate_ValidRequest_TrimsTheNameAndRoundsCoordinatesTo3Places()
    {
        var (errors, location) = SavedLocationRules.Validate(Home with { Name = "  Home  " });

        Assert.Empty(errors);
        Assert.Equal(new ValidSavedLocation("Home", 53.421, -6.270, 10), location);
    }

    [Theory]
    [InlineData(53.4215, 53.422)]   // halves round away from zero...
    [InlineData(-6.2705, -6.271)]   // ...on both sides of zero, so a mirror-image place rounds the mirror-image way
    [InlineData(90.0, 90.0)]
    public void Validate_Coordinates_RoundAwayFromZero(double given, double stored)
    {
        var (_, location) = SavedLocationRules.Validate(Home with { Lat = given });

        Assert.Equal(stored, location!.Latitude);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_NoName_IsAnError(string? name)
    {
        var (errors, location) = SavedLocationRules.Validate(Home with { Name = name });

        Assert.Null(location);
        Assert.Equal(["name is required."], errors["name"]);
    }

    [Fact]
    public void Validate_NameLength_IsCountedAfterTrimming()
    {
        string fifty = new('a', 50);

        Assert.Empty(SavedLocationRules.Validate(Home with { Name = $"  {fifty}  " }).Errors);
        Assert.Contains("name", SavedLocationRules.Validate(Home with { Name = fifty + "a" }).Errors.Keys);
    }

    [Theory]
    [InlineData("Home\nAway")]
    [InlineData("Ho\tme")]
    [InlineData("Home\u0000")]
    public void Validate_ControlCharactersInName_AreAnError(string name)
    {
        Assert.Contains("name", SavedLocationRules.Validate(Home with { Name = name }).Errors.Keys);
    }

    [Theory]
    [InlineData(null, "lat is required.")]
    [InlineData(90.001, "lat must be between -90 and 90.")]
    [InlineData(double.NaN, "lat must be between -90 and 90.")] // NaN fails every comparison, so it needs IsFinite
    public void Validate_BadLatitude_IsAnError(double? lat, string expected)
    {
        Assert.Equal([expected], SavedLocationRules.Validate(Home with { Lat = lat }).Errors["lat"]);
    }

    [Theory]
    [InlineData(-180.5)]
    [InlineData(double.PositiveInfinity)]
    public void Validate_BadLongitude_IsAnError(double lon)
    {
        Assert.Contains("lon", SavedLocationRules.Validate(Home with { Lon = lon }).Errors.Keys);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0.0)]
    [InlineData(250.1)]
    public void Validate_BadRadius_IsAnError(double? radiusKm)
    {
        Assert.Contains("radiusKm", SavedLocationRules.Validate(Home with { RadiusKm = radiusKm }).Errors.Keys);
    }

    [Fact]
    public void Validate_EverythingWrong_ReportsEveryField()
    {
        var (errors, _) = SavedLocationRules.Validate(new SavedLocationRequest(null, null, null, null));

        Assert.Equal(["lat", "lon", "name", "radiusKm"], errors.Keys.Order());
    }
}
