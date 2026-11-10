using System.Runtime.CompilerServices;

namespace FlightRadar.Tests;

internal static class TestSetup
{
    /// <summary>
    /// Runs once, when the test assembly loads, before any test. Turns the background poller off for every
    /// WebApplicationFactory app: otherwise each would start a loop that, once some test marked an area as watched,
    /// would fetch it from the real adsb.lol every 10 s. Environment variables are one of the host's configuration
    /// sources, so this reaches every app; tests about the poller turn it back on with UseSetting, which wins.
    /// </summary>
    [ModuleInitializer]
    internal static void DisableBackgroundPoller() => Environment.SetEnvironmentVariable("Poller__Enabled", "false");
}
