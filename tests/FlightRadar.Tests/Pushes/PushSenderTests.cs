using System.Security.Cryptography;
using System.Text.Json;
using FirebaseAdmin.Messaging;
using FlightRadar.Api.Pushes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FlightRadar.Tests.Pushes;

/// <summary>Everything about sending except FCM itself (no network in unit tests).</summary>
public sealed class PushSenderTests
{
    /// <summary>A made-up service account key, shaped like Google's file, with a freshly generated RSA key.</summary>
    private static string FakeServiceAccountJson()
    {
        using var rsa = RSA.Create(2048);
        return JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["type"] = "service_account",
            ["project_id"] = "flightradar-test",
            ["private_key_id"] = "test",
            ["private_key"] = rsa.ExportPkcs8PrivateKeyPem(),
            ["client_email"] = "fcm-sender@flightradar-test.iam.gserviceaccount.com",
            ["client_id"] = "1",
            ["token_uri"] = "https://oauth2.googleapis.com/token",
        });
    }

    private static WebApplicationFactory<Program> App(string? serviceAccountJson) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Redis", "");
            builder.UseSetting("ConnectionStrings:Postgres", "");
            builder.UseSetting("Firebase:ServiceAccountJson", serviceAccountJson ?? "");
        });

    [Fact]
    public async Task NotConfigured_SkipsEveryMessage()
    {
        using var app = App(null);
        var sender = app.Services.GetRequiredService<IPushSender>();

        var outcomes = await sender.SendAsync(
            [new PushMessage("fid", PushPriority.High, new Dictionary<string, string>())], default);

        Assert.IsType<NotConfiguredPushSender>(sender);
        Assert.Equal([PushOutcome.NotConfigured], outcomes);
    }

    [Fact]
    public void Configured_UsesFirebase()
    {
        using var app = App(FakeServiceAccountJson());

        Assert.IsType<FirebasePushSender>(app.Services.GetRequiredService<IPushSender>());
    }

    [Fact]
    public void ConfiguredWithSomethingElse_FailsAtStartupWithoutEchoingIt()
    {
        using var app = App("""{"type":"authorized_user","secret":"do-not-print"}""");

        var exception = Assert.Throws<OptionsValidationException>(() => app.Services);

        Assert.Contains("Firebase:ServiceAccountJson", exception.Message);
        Assert.DoesNotContain("do-not-print", exception.Message);
    }

    [Theory]
    [InlineData(PushPriority.High, Priority.High)]
    [InlineData(PushPriority.Normal, Priority.Normal)]
    public void ToFcm_DataOnlyShortLivedNotCollapsible_WithTheAskedPriority(PushPriority asked, Priority expected)
    {
        var data = new Dictionary<string, string> { ["locationId"] = "1" };

        var message = FirebasePushSender.ToFcm(new PushMessage("fid-1", asked, data));

        Assert.Equal("fid-1", message.Fid);
        Assert.Equal(data, message.Data);
        Assert.Null(message.Notification); // the app decides what to show
        Assert.Equal(expected, message.Android.Priority);
        Assert.Equal(TimeSpan.FromMinutes(2), message.Android.TimeToLive);
        // Collapsible messages are throttled to one every 3 minutes per device after a burst of 20.
        Assert.Null(message.Android.CollapseKey);
    }

    [Theory]
    [InlineData(MessagingErrorCode.Unregistered, PushOutcome.TokenGone)]
    [InlineData(MessagingErrorCode.SenderIdMismatch, PushOutcome.TokenGone)]
    [InlineData(MessagingErrorCode.InvalidArgument, PushOutcome.Failed)] // could be our message: never wipe tokens for it
    [InlineData(MessagingErrorCode.Unavailable, PushOutcome.Failed)]
    [InlineData(MessagingErrorCode.QuotaExceeded, PushOutcome.Failed)]
    [InlineData(null, PushOutcome.Failed)]
    public void Classify_OnlyDeadTokenErrorsMeanGone(MessagingErrorCode? code, PushOutcome expected) =>
        Assert.Equal(expected, FirebasePushSender.Classify(code));
}
