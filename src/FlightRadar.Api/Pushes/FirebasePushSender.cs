using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Options;

namespace FlightRadar.Api.Pushes;

/// <summary>Firebase settings, bound from "Firebase".</summary>
public sealed class FirebaseOptions
{
    public const string SectionName = "Firebase";

    /// <summary>
    /// A Google service account key (the whole JSON file as one string) allowed to send FCM messages. A secret:
    /// user-secrets locally, a Container Apps secret in Azure. Unset = no pushes. Never logged.
    /// </summary>
    public string? ServiceAccountJson { get; set; }

    public bool Configured => !string.IsNullOrWhiteSpace(ServiceAccountJson);
}

/// <summary>
/// Sends through Firebase Cloud Messaging's HTTP v1 API with Google's FirebaseAdmin SDK, which gets and refreshes
/// the OAuth access tokens from the service account key (like our OpenSky token provider, done for us).
/// </summary>
public sealed partial class FirebasePushSender : IPushSender, IDisposable
{
    /// <summary>A push about the nearest aircraft is worthless after a couple of minutes: FCM drops it after this.</summary>
    public static readonly TimeSpan TimeToLive = TimeSpan.FromMinutes(2);

    /// <summary>FCM's limit per batch call.</summary>
    private const int MaxBatch = 500;

    private readonly FirebaseApp _app;
    private readonly FirebaseMessaging _messaging;
    private readonly ILogger<FirebasePushSender> _logger;

    public FirebasePushSender(IOptions<FirebaseOptions> options, ILogger<FirebasePushSender> logger)
    {
        _logger = logger;
        // FirebaseApp keeps a static, process-wide registry of apps by name: a unique name per sender means several
        // test hosts in one process don't collide, and Dispose removes ours.
        _app = FirebaseApp.Create(
            new AppOptions { Credential = LoadCredential(options.Value.ServiceAccountJson!) },
            $"flightradar-{Guid.NewGuid():N}");
        _messaging = FirebaseMessaging.GetMessaging(_app);
    }

    /// <summary>
    /// Reads the key. <c>CredentialFactory</c> with an explicit type, rather than the older
    /// <c>GoogleCredential.FromJson</c>, which accepts any credential type a JSON file can describe.
    /// </summary>
    public static GoogleCredential LoadCredential(string json) =>
        CredentialFactory.FromJson<ServiceAccountCredential>(json).ToGoogleCredential();

    public async Task<IReadOnlyList<PushOutcome>> SendAsync(
        IReadOnlyList<PushMessage> messages, CancellationToken cancellationToken)
    {
        var outcomes = new List<PushOutcome>(messages.Count);
        foreach (var batch in messages.Chunk(MaxBatch))
        {
            try
            {
                var response = await _messaging.SendEachAsync(batch.Select(ToFcm), cancellationToken);
                outcomes.AddRange(response.Responses.Select(result =>
                    result.IsSuccess ? PushOutcome.Sent : Classify(result.Exception?.MessagingErrorCode)));
            }
            catch (Exception exception) when (exception is FirebaseException or HttpRequestException
                                                  && !cancellationToken.IsCancellationRequested)
            {
                // The whole call failed (credentials rejected, FCM unreachable): every message in it failed.
                LogBatchFailed(_logger, batch.Length, exception.Message);
                outcomes.AddRange(batch.Select(_ => PushOutcome.Failed));
            }
        }
        return outcomes;
    }

    /// <summary>
    /// Which errors mean "this token is dead". Unregistered: the app was uninstalled or its token replaced.
    /// SenderIdMismatch: a token from another Firebase project. (The .NET SDK has no separate code for an unknown
    /// installation ID; anything unmapped counts as Failed, and a device that stops registering is forgotten after a
    /// week anyway.) Not InvalidArgument: that also covers a bad message
    /// from us, and treating it as a dead token would wipe every device's token after one bug.
    /// </summary>
    public static PushOutcome Classify(MessagingErrorCode? code) => code switch
    {
        MessagingErrorCode.Unregistered or MessagingErrorCode.SenderIdMismatch => PushOutcome.TokenGone,
        _ => PushOutcome.Failed,
    };

    /// <summary>The FCM message: data only, Android priority from <see cref="PushMessage.Priority"/>, no collapse key.</summary>
    public static Message ToFcm(PushMessage message) => new()
    {
        // A Firebase Installation ID, FCM's device address since 2026 (registration tokens are deprecated: the
        // Android SDK's register()/onRegistered replace getToken()/onNewToken()).
        Fid = message.Token,
        Data = message.Data,
        Android = new AndroidConfig
        {
            Priority = message.Priority == PushPriority.High ? Priority.High : Priority.Normal,
            TimeToLive = TimeToLive,
        },
    };

    public void Dispose() => _app.Delete();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't send {Count} pushes: {Reason}")]
    private static partial void LogBatchFailed(ILogger logger, int count, string reason);
}
