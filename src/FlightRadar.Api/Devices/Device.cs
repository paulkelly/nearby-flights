namespace FlightRadar.Api.Devices;

/// <summary>
/// One install of the Android app (step 9c). Its widgets' locations are watched by the server around the clock, so
/// pushes (step 9d) can say when an aircraft comes near. Like a chunk loader in a game world: it keeps its area
/// ticking with no player there.
/// </summary>
public sealed class Device
{
    /// <summary>
    /// The installation ID: a random UUID the app makes once and keeps out of backups, so a phone restored from a
    /// backup registers as a new device instead of taking over the old one. Chosen by the client, so it proves
    /// nothing on its own: every request also needs the owner's token, and a device belongs to exactly one owner.
    /// </summary>
    public Guid Id { get; set; }

    public Guid OwnerId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// When the app last registered. The app re-registers at least daily; a device silent for longer than
    /// <c>Poller:WidgetDeviceDays</c> (uninstalled, say) stops being watched.
    /// </summary>
    public DateTimeOffset LastSeenAt { get; set; }

    /// <summary>
    /// Where Firebase Cloud Messaging delivers this device's pushes (step 9d): its Firebase Installation ID (FID), or
    /// null (none yet, or FCM said it no longer works). From the Firebase SDK; it can change, and the app then
    /// registers again.
    /// </summary>
    public string? PushToken { get; set; }
}

/// <summary>A saved location one device's widgets show. Key: (device, location).</summary>
public sealed class DeviceWatch
{
    public Guid DeviceId { get; set; }

    public Guid LocationId { get; set; }

    /// <summary>
    /// When this device first asked for this location. Kept when the same set is sent again, because the caps
    /// (per owner and overall) go to the oldest first: re-registering mustn't move a widget to the back of the queue.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }
}
