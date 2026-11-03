using System.ComponentModel.DataAnnotations;

namespace FlightRadar.Api.Providers.AdsbLol;

/// <summary>Settings for the adsb.lol provider, bound from the "Providers:AdsbLol" config section.</summary>
public sealed class AdsbLolOptions
{
    public const string SectionName = "Providers:AdsbLol";

    /// <summary>
    /// API root. Must end with "/": relative paths are resolved like links in a web page, so without the
    /// trailing slash the last segment of the base URL would be replaced rather than appended to.
    /// </summary>
    [Required]
    public Uri? BaseUrl { get; set; }

    [Range(1, 60)]
    public int TimeoutSeconds { get; set; } = 10;
}
