namespace FlightRadar.Api.Streaming;

/// <summary>
/// How often one connection may change the area it watches: a token bucket, like an ability with charges. Up to
/// <see cref="Burst"/> changes in quick succession (clicking around the map, dragging the slider), then one more per
/// <see cref="RefillEvery"/>. Each change costs the server a lookup (and maybe an adsb.lol call), so a script spamming
/// Watch is stopped here.
/// </summary>
/// <remarks>
/// Why not the app's HTTP rate limiter: a WebSocket is one long HTTP request. The limiter sees the connection open
/// once, then never again, however many messages travel over it.
/// <para>Not thread-safe on its own: <see cref="FlightSubscriptions"/> locks around it.</para>
/// </remarks>
public sealed class WatchThrottle(DateTimeOffset now)
{
    public const int Burst = 5;
    public static readonly TimeSpan RefillEvery = TimeSpan.FromSeconds(1);

    private double _tokens = Burst;
    private DateTimeOffset _lastRefill = now;

    /// <summary>Spends a token and returns true, or returns false if none is left (the change is refused).</summary>
    public bool TryTake(DateTimeOffset now)
    {
        // Refill for the time since last time, never above the burst. A clock going backwards adds nothing.
        var elapsed = now - _lastRefill;
        if (elapsed > TimeSpan.Zero)
        {
            _tokens = Math.Min(Burst, _tokens + elapsed / RefillEvery);
            _lastRefill = now;
        }

        if (_tokens < 1)
        {
            return false;
        }

        _tokens -= 1;
        return true;
    }

    /// <summary>How long until a token is available again (zero if one is now): for the error message.</summary>
    public TimeSpan WaitFor(DateTimeOffset now)
    {
        double tokensThen = Math.Min(Burst, _tokens + Math.Max(0, (now - _lastRefill) / RefillEvery));
        return tokensThen >= 1 ? TimeSpan.Zero : RefillEvery * (1 - tokensThen);
    }
}
