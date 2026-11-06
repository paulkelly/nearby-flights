using System.Text.Json;
using System.Text.Json.Serialization;

namespace FlightRadar.Api.Providers.OpenSky;

// Wire format of OpenSky's GET /states/all. Internal: these shapes never leave this folder.

internal sealed class OpenSkyResponse
{
    /// <summary>The time the states are for, in Unix seconds.</summary>
    [JsonPropertyName("time")] public long Time { get; init; }

    /// <summary>
    /// One "state vector" per aircraft, each a JSON <em>array</em> rather than an object: fields are identified by
    /// position (see <see cref="StateVector"/>). Null when there are no aircraft in the box.
    /// </summary>
    [JsonPropertyName("states")] public List<JsonElement>? States { get; init; }
}

/// <summary>
/// Positions of the fields in a state vector, from OpenSky's REST docs. Units are metric: metres, m/s.
/// Many can be null (no position yet, no altitude on the ground, ...).
/// </summary>
internal static class StateVector
{
    public const int Icao24 = 0;         // string, lowercase hex
    public const int Callsign = 1;       // string, padded to 8 characters with spaces
    public const int TimePosition = 3;   // int, Unix seconds of the last position report
    public const int LastContact = 4;    // int, Unix seconds of the last message of any kind
    public const int Longitude = 5;      // float, degrees
    public const int Latitude = 6;       // float, degrees
    public const int BaroAltitude = 7;   // float, metres (pressure altitude)
    public const int OnGround = 8;       // bool
    public const int Velocity = 9;       // float, m/s over ground
    public const int TrueTrack = 10;     // float, degrees clockwise from north
    public const int VerticalRate = 11;  // float, m/s, positive = climbing

    public static string? String(JsonElement state, int index) =>
        At(state, index) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;

    public static double? Number(JsonElement state, int index) =>
        At(state, index) is { ValueKind: JsonValueKind.Number } value ? value.GetDouble() : null;

    public static bool Bool(JsonElement state, int index) =>
        At(state, index) is { ValueKind: JsonValueKind.True };

    // Out-of-range indexes read as missing, so a shorter row (or a future format change) can't crash the parser.
    private static JsonElement? At(JsonElement state, int index) =>
        state.ValueKind == JsonValueKind.Array && index < state.GetArrayLength() ? state[index] : null;
}