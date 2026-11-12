namespace FlightRadar.Api.Pushes;

/// <summary>
/// One push to one device (step 9d): data only, no notification text, because the app decides what to show.
/// </summary>
/// <param name="Token">The device's Firebase Installation ID (FID), where FCM delivers its pushes.</param>
/// <param name="CollapseKey">
/// Pushes with the same key replace each other while the phone is offline, so it only gets the latest (one per
/// location: an older "nearest aircraft" is worthless).
/// </param>
/// <param name="Data">Key/value pairs for the app. FCM allows strings only, 4 KB in all.</param>
public sealed record PushMessage(string Token, string CollapseKey, IReadOnlyDictionary<string, string> Data);

public enum PushOutcome
{
    Sent,

    /// <summary>FCM says this token will never work again (app uninstalled, data cleared): stop using it.</summary>
    TokenGone,

    /// <summary>Didn't go this time (FCM down, quota); the token may still be fine.</summary>
    Failed,

    /// <summary>Pushes aren't configured (no Firebase credentials): nothing was sent.</summary>
    NotConfigured,
}

/// <summary>Sends pushes to phones. Behind an interface so the rest of the app (and tests) never touch Firebase.</summary>
public interface IPushSender
{
    /// <summary>One outcome per message, in the same order. Never throws for a failed send.</summary>
    Task<IReadOnlyList<PushOutcome>> SendAsync(IReadOnlyList<PushMessage> messages, CancellationToken cancellationToken);
}

/// <summary>Without Firebase credentials (local runs, tests, CI): pushes are skipped, everything else works.</summary>
public sealed class NotConfiguredPushSender : IPushSender
{
    public Task<IReadOnlyList<PushOutcome>> SendAsync(
        IReadOnlyList<PushMessage> messages, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PushOutcome>>(messages.Select(_ => PushOutcome.NotConfigured).ToList());
}
