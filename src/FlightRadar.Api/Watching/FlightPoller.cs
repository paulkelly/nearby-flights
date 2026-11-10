using System.Diagnostics;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace FlightRadar.Api.Watching;

/// <summary>
/// The background poller (step 7a): every <see cref="WatchingOptions.IntervalSeconds"/> it runs one
/// <see cref="PollerTick"/>, keeping every watched area's cache entry fresh so requests rarely wait for adsb.lol.
/// </summary>
/// <remarks>
/// A fixed-timestep loop, like a game server's tick. <see cref="PeriodicTimer"/> never queues up ticks: if one
/// overruns (an adsb.lol outage makes fetches slow), the ticks it overlapped are skipped rather than run back to
/// back, the "drop frames instead of spiralling" rule.
/// <para>
/// A <see cref="BackgroundService"/> is a singleton that lives as long as the app, but the services a tick uses
/// (the cache decorator, typed HttpClients) are meant to be short-lived, so each tick gets its own DI scope: like
/// a request does.
/// </para>
/// </remarks>
public sealed partial class FlightPoller(
    IServiceScopeFactory scopes,
    IOptions<WatchingOptions> options,
    PollerStatus status,
    TimeProvider timeProvider,
    ILogger<FlightPoller> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.Value.IntervalSeconds), timeProvider);
        do
        {
            await RunTickAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunTickAsync(CancellationToken stoppingToken)
    {
        long started = Stopwatch.GetTimestamp();
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<PollerTick>().RunAsync(stoppingToken);
            double elapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

            if (result.Failed > 0)
            {
                LogTickWithFailures(logger, result.Watched, result.Fetched, result.ClaimedElsewhere, result.Failed, elapsedMs);
            }
            else
            {
                LogTick(logger, result.Watched, result.Fetched, result.ClaimedElsewhere, elapsedMs);
            }
        }
        catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
        {
            // Never let one bad tick end the loop: a BackgroundService that throws stops for good (and by default
            // stops the whole app). Log it and try again next tick.
            LogTickFailed(logger, exception);
        }
        finally
        {
            status.TickCompleted(timeProvider.GetUtcNow());
        }
    }

    // Debug: one line every 10 s would drown everything else at Information. Turn it on to watch the poller work
    // (Serilog:MinimumLevel:Override:FlightRadar.Api.Watching = Debug).
    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Poller tick: {Watched} watched, {Fetched} fetched, {ClaimedElsewhere} by another instance, in {ElapsedMs:0} ms")]
    private static partial void LogTick(ILogger logger, int watched, int fetched, int claimedElsewhere, double elapsedMs);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Poller tick: {Watched} watched, {Fetched} fetched, {ClaimedElsewhere} by another instance, {Failed} failed, in {ElapsedMs:0} ms")]
    private static partial void LogTickWithFailures(
        ILogger logger, int watched, int fetched, int claimedElsewhere, int failed, double elapsedMs);

    [LoggerMessage(Level = LogLevel.Error, Message = "Poller tick failed")]
    private static partial void LogTickFailed(ILogger logger, Exception exception);
}

/// <summary>When the poller last finished a tick (successful or not), for its health check.</summary>
public sealed class PollerStatus(TimeProvider timeProvider)
{
    // Starts at "now": a freshly started app hasn't missed any ticks yet.
    private long _lastTickUnixMs = timeProvider.GetUtcNow().ToUnixTimeMilliseconds();

    public DateTimeOffset LastTick => DateTimeOffset.FromUnixTimeMilliseconds(Interlocked.Read(ref _lastTickUnixMs));

    public void TickCompleted(DateTimeOffset at) => Interlocked.Exchange(ref _lastTickUnixMs, at.ToUnixTimeMilliseconds());
}

/// <summary>
/// Is the poller still ticking? Degraded (still 200) if no tick has finished for three intervals: requests keep
/// working without it, just more slowly, so the instance shouldn't be taken out of service.
/// </summary>
public sealed class PollerHealthCheck(PollerStatus status, IOptions<WatchingOptions> options, TimeProvider timeProvider)
    : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var sinceLastTick = timeProvider.GetUtcNow() - status.LastTick;
        var allowed = TimeSpan.FromSeconds(options.Value.IntervalSeconds * 3);

        return Task.FromResult(sinceLastTick <= allowed
            ? HealthCheckResult.Healthy($"Last tick {sinceLastTick.TotalSeconds:0} s ago.")
            : new HealthCheckResult(context.Registration.FailureStatus,
                $"No poller tick for {sinceLastTick.TotalSeconds:0} s (expected every {options.Value.IntervalSeconds} s)."));
    }
}
