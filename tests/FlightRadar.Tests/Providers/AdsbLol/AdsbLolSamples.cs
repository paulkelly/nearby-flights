namespace FlightRadar.Tests.Providers.AdsbLol;

/// <summary>Response bodies trimmed from real adsb.lol output (around Dublin Airport), plus edge cases.</summary>
internal static class AdsbLolSamples
{
    /// <summary>Unix milliseconds in <see cref="Mixed"/>'s "now" field.</summary>
    public const long Now = 1793713435002;

    public const string Mixed = """
        {
          "ac": [
            {
              "hex": "4cad7b", "type": "adsb_icao", "flight": "EIN58P  ", "r": "EI-NSB", "t": "A20N",
              "alt_baro": 2200, "alt_geom": 2975, "gs": 165.0, "track": 89.65, "baro_rate": -704, "geom_rate": -800,
              "squawk": "7463", "lat": 53.448257, "lon": -6.495510, "seen_pos": 0.5, "seen": 0.0, "mlat": [], "tisb": []
            },
            {
              "hex": "4ca4dc", "type": "adsb_icao_nt", "flight": "EIFLS   ", "r": "EI-FLS", "t": "C42",
              "alt_baro": 300, "gs": 78.3, "track": 280.30, "geom_rate": 64,
              "lat": 53.357011, "lon": -6.551568, "seen_pos": 1.928
            },
            {
              "hex": "4ca9f1", "type": "adsb_icao", "r": "EI-DCL", "t": "B738",
              "alt_baro": "ground", "gs": 12.4, "true_heading": 160.0,
              "lat": 53.4265, "lon": -6.2499, "seen_pos": 2.0
            },
            {
              "hex": "4ca123", "type": "mode_s", "alt_baro": 37000, "seen": 4.1
            }
          ],
          "msg": "No error", "now": 1793713435002, "total": 4, "ctime": 1793713435002, "ptime": 0
        }
        """;

    public const string Empty = """
        { "ac": [], "msg": "No error", "now": 1793713435002, "total": 0, "ctime": 1793713435002, "ptime": 0 }
        """;
}
