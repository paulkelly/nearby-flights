namespace FlightRadar.Api.Owners;

/// <summary>
/// Whoever saved some locations: an anonymous "guest account", identified by a random token the browser keeps.
/// Created the first time someone saves a location, never just for visiting.
/// </summary>
public sealed class Owner
{
    /// <summary>
    /// Never shown to anyone: the token, not this ID, is what proves who you are. A <see cref="Guid"/> rather than
    /// 1, 2, 3 so IDs say nothing about how many owners exist. Npgsql generates version 7 GUIDs, which start with
    /// a timestamp: new rows land at the end of the index instead of at random places in it.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// SHA-256 of the owner's token, never the token itself, so a leaked database contains no usable tokens.
    /// Like a password hash, except that a fast hash is right here: the token is 256 random bits, nothing like a
    /// guessable password, so there's nothing to slow an attacker down from.
    /// </summary>
    public required byte[] TokenHash { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
