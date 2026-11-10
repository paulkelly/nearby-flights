using FlightRadar.Api.Watching;
using FlightRadar.Tests.Fakes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace FlightRadar.Tests.Watching;

public sealed class FlightPollerTests
{
    private static async Task<HealthStatus> CheckAsync(PollerStatus status, FakeTimeProvider clock)
    {
        var check = new PollerHealthCheck(status, Options.Create(new WatchingOptions { IntervalSeconds = 10 }), clock);
        var context = new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("poller", check, HealthStatus.Degraded, tags: null),
        };
        return (await check.CheckHealthAsync(context)).Status;
    }

    [Fact]
    public async Task HealthCheck_TickedRecently_IsHealthy()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var status = new PollerStatus(clock);
        clock.Advance(TimeSpan.FromSeconds(30)); // three intervals: still allowed

        Assert.Equal(HealthStatus.Healthy, await CheckAsync(status, clock));
    }

    [Fact]
    public async Task HealthCheck_NoTickForThreeIntervals_IsDegraded()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var status = new PollerStatus(clock);
        clock.Advance(TimeSpan.FromSeconds(31));

        Assert.Equal(HealthStatus.Degraded, await CheckAsync(status, clock));

        status.TickCompleted(clock.GetUtcNow());
        Assert.Equal(HealthStatus.Healthy, await CheckAsync(status, clock));
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void Registration_FollowsPollerEnabled(string enabled, bool expectPoller)
    {
        using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Poller:Enabled", enabled);
            builder.UseSetting("ConnectionStrings:Redis", "");
            builder.UseSetting("ConnectionStrings:Postgres", "");
        });

        var hostedServices = app.Services.GetServices<IHostedService>();

        Assert.Equal(expectPoller, hostedServices.OfType<FlightPoller>().Any());
    }
}
