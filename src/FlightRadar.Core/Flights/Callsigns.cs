namespace FlightRadar.Core.Flights;

/// <summary>Tidying callsigns as they arrive from providers.</summary>
public static class Callsigns
{
    /// <summary>
    /// The callsign without padding, or null if there's no real one: blank, or a placeholder of all zeros
    /// ("00000000"), which some transmitters send (at Dublin, fixed ground transmitters of type "TWR"). Null lets
    /// the app fall back to the registration or hex code instead of showing "00000000".
    /// </summary>
    public static string? Clean(string? callsign)
    {
        var trimmed = callsign?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.All(c => c == '0'))
        {
            return null;
        }

        return trimmed;
    }
}
