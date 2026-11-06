namespace FlightRadar.Tests.Providers.OpenSky;

/// <summary>Response bodies in OpenSky's /states/all format.</summary>
internal static class OpenSkySamples
{
    /// <summary>
    /// Rows from a real anonymous response around Dublin (2026-11-06), plus two made-up rows for edge cases
    /// (marked). Each state vector is a positional array: see StateVector for the field order.
    /// </summary>
    public const string Mixed = """
        {"time":1793979637,"states":[
          ["4076e4","BAW84E  ","United Kingdom",1793979624,1793979624,-6.2493,53.4246,null,true,7.46,157.5,null,null,null,"2743",true,0],
          ["4ca3db","","Ireland",1793979528,1793979528,-6.2624,53.4323,null,true,4.63,8.44,null,null,null,null,false,0],
          ["4ca36c","EIGWY   ","Ireland",1793979637,1793979637,-6.4759,53.3652,152.4,false,43.24,304.82,1.63,null,289.56,"7000",false,0],
          ["4d2502","RYR33KY ","Malta",1793979636,1793979636,-6.2202,53.418,419.1,false,78.87,97.5,11.38,null,548.64,"4641",false,0],
          ["abc123","NOPOS   ","Nowhere",null,1793979630,null,null,1000.0,false,100.0,90.0,0.0,null,null,null,false,0],
          ["def456",null,"Nowhere",null,1793979600,-6.3,53.4,3000.0,false,null,null,null,null,null,null,false,0]
        ]}
        """;
    // Made up: "abc123" has no position (skipped); "def456" has a position but no callsign, speed, track,
    // vertical rate or position time (so its timestamp falls back to last_contact).

    /// <summary>No aircraft in the box: OpenSky sends null, not an empty array.</summary>
    public const string Empty = """{"time":1793979637,"states":null}""";
}