using System.Collections.Frozen;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FlightRadar.Core.Enrichment;

/// <summary>
/// Airline names by ICAO airline code ("EIN" → "Aer Lingus"), to name the airline behind a callsign like "EIN123".
/// </summary>
/// <remarks>
/// The bundled list (<c>airlines.json</c>, embedded in this assembly) is a snapshot of Virtual Radar Server's
/// community data (CC0), refreshed with <c>scripts/update-airlines.ps1</c>. Community data ages (a few entries name
/// airlines that have since closed), so treat names as "very likely", not authoritative. Immutable after
/// construction, so one instance is shared by every request.
/// </remarks>
public sealed partial class AirlineDirectory
{
    private const string BundledResourceName = "FlightRadar.Core.Enrichment.airlines.json";

    private readonly FrozenDictionary<string, string> _namesByCode;

    public AirlineDirectory(IReadOnlyDictionary<string, string> namesByCode)
    {
        // FrozenDictionary: built once, then optimised for lookups, which is all this does.
        _namesByCode = namesByCode.ToFrozenDictionary(StringComparer.Ordinal);
    }

    public int Count => _namesByCode.Count;

    /// <summary>The list bundled with the app.</summary>
    public static AirlineDirectory LoadBundled()
    {
        using var stream = typeof(AirlineDirectory).Assembly.GetManifestResourceStream(BundledResourceName)
            ?? throw new InvalidOperationException($"Embedded resource {BundledResourceName} is missing.");
        var namesByCode = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
            ?? throw new InvalidOperationException($"Embedded resource {BundledResourceName} is empty.");
        return new AirlineDirectory(namesByCode);
    }

    /// <summary>The airline flying under this callsign, or null if it isn't an airline callsign or isn't known.</summary>
    public string? NameForCallsign(string? callsign) =>
        AirlineCode(callsign) is { } code && _namesByCode.TryGetValue(code, out var name) ? name : null;

    /// <summary>
    /// The ICAO airline code at the start of an airline-style callsign ("EIN123" → "EIN"), or null.
    /// </summary>
    /// <remarks>
    /// Airline callsigns are the 3-letter code followed by a flight number that starts with a digit ("RYR4CZ").
    /// The digit matters: many private aircraft use their registration as their callsign ("EIGWY" is EI-GWY), and
    /// without it "EIG" would be read as an airline code. Callsigns are at most 8 characters.
    /// </remarks>
    public static string? AirlineCode(string? callsign)
    {
        if (callsign is null)
        {
            return null;
        }

        var match = AirlineCallsign().Match(callsign.Trim().ToUpperInvariant());
        return match.Success ? match.Groups["code"].Value : null;
    }

    // [GeneratedRegex]: the regex is compiled to C# at build time (no start-up cost, checked by the compiler).
    [GeneratedRegex("^(?<code>[A-Z]{3})[0-9][A-Z0-9]{0,4}$")]
    private static partial Regex AirlineCallsign();
}
