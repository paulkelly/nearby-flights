namespace FlightRadar.Api.Pushes;

/// <summary>
/// One push to one device (step 9d): data only, no notification text, because the app decides what to show.
/// </summary>
/// <param name="Token">The device's Firebase Installation ID (FID), where FCM delivers its pushes.</param>
/// <param name="Priority">How urgently Android should deliver it (see <see cref="PushPriority"/>).</param>
/// <param name="Data">Key/value pairs for the app. FCM allows strings only, 4 KB in all.</param>
/// <remarks>
/// No collapse key: FCM limits collapsible messages to a burst of 20 per app per device, then one every 3 minutes,
/// which would hold a push a minute back to one every 3 minutes (non-collapsible: 240 a minute, 5,000 an hour).
/// "Only the latest counts" comes from the short time-to-live and the app ignoring anything older than it shows.
/// </remarks>
public sealed record PushMessage(string Token, PushPriority Priority, IReadOnlyDictionary<string, string> Data);

public enum PushPriority
{
    /// <summary>
    /// News (the aircraft changed): delivered at once, even to an idle phone. Android keeps that up only for apps that
    /// show something when one arrives, which the notification and widget do.
    /// </summary>
    High,

    /// <summary>
    /// A refresh (the same aircraft, newer data): delivered at once while the phone is awake or in use, held back
    /// while it's idle and released later in a batch. The nearest we get to "only when someone's looking".
    /// </summary>
    Normal,
}

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
