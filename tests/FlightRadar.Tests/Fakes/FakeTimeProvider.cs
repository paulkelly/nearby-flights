namespace FlightRadar.Tests.Fakes;

/// <summary>
/// A clock the test controls: time only moves when <see cref="Advance"/> is called. Code that reads the time through
/// TimeProvider (rather than DateTime.Now) can then be tested for "after 30 minutes" without waiting 30 minutes.
/// (Microsoft.Extensions.TimeProvider.Testing has a fuller one; this is all these tests need.)
/// </summary>
public sealed class FakeTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}