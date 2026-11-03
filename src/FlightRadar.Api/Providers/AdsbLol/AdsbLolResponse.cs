using System.Text.Json;
using System.Text.Json.Serialization;

namespace FlightRadar.Api.Providers.AdsbLol;

// Wire format of adsb.lol's /v2 endpoints (readsb's JSON). Internal: these shapes never leave this folder.
// Only the fields we use are declared; System.Text.Json ignores the rest.

internal sealed class AdsbLolResponse
{
    /// <summary>Server time the snapshot was taken, in Unix milliseconds.</summary>
    [JsonPropertyName("now")] public long Now { get; init; }

    [JsonPropertyName("ac")] public List<AdsbLolAircraft>? Aircraft { get; init; }
}

internal sealed class AdsbLolAircraft
{
    [JsonPropertyName("hex")] public string? Hex { get; init; }

    /// <summary>Callsign, space-padded to 8 characters.</summary>
    [JsonPropertyName("flight")] public string? Flight { get; init; }

    [JsonPropertyName("r")] public string? Registration { get; init; }

    /// <summary>ICAO aircraft type. Not to be confused with "type", which is the message source (adsb_icao, mlat...).</summary>
    [JsonPropertyName("t")] public string? AircraftType { get; init; }

    [JsonPropertyName("alt_baro")]
    [JsonConverter(typeof(BaroAltitudeJsonConverter))]
    public BaroAltitude AltBaro { get; init; }

    [JsonPropertyName("gs")] public double? GroundSpeed { get; init; }
    [JsonPropertyName("track")] public double? Track { get; init; }
    [JsonPropertyName("baro_rate")] public double? BaroRate { get; init; }
    [JsonPropertyName("geom_rate")] public double? GeomRate { get; init; }
    [JsonPropertyName("lat")] public double? Lat { get; init; }
    [JsonPropertyName("lon")] public double? Lon { get; init; }

    /// <summary>How many seconds before <see cref="AdsbLolResponse.Now"/> the position was received.</summary>
    [JsonPropertyName("seen_pos")] public double? SeenPos { get; init; }
}

/// <summary>"alt_baro" is either a number of feet or the string "ground". Default means unknown.</summary>
internal readonly record struct BaroAltitude(int? Feet, bool OnGround);

internal sealed class BaroAltitudeJsonConverter : JsonConverter<BaroAltitude>
{
    public override BaroAltitude Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                return new BaroAltitude((int)Math.Round(reader.GetDouble()), OnGround: false);
            case JsonTokenType.String when reader.ValueTextEquals("ground"u8):
                return new BaroAltitude(Feet: null, OnGround: true);
            default:
                // Anything unexpected (null, other strings, objects) counts as unknown rather than failing the
                // whole response. Skip() moves past objects/arrays; for single values it does nothing.
                reader.Skip();
                return default;
        }
    }

    public override void Write(Utf8JsonWriter writer, BaroAltitude value, JsonSerializerOptions options) =>
        throw new NotSupportedException("We only read adsb.lol responses.");
}
