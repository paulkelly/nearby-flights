using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;

namespace FlightRadar.Api.Owners;

/// <summary>
/// The secret an owner proves who they are with: 32 random bytes (256 bits), written as 43 URL-safe Base64
/// characters so it fits in a header or a QR code. Only its SHA-256 hash is ever stored.
/// </summary>
public static class OwnerToken
{
    private const int TokenBytes = 32;

    /// <summary>A new token for the client, and the hash to store in its place.</summary>
    public static (string Token, byte[] Hash) Create()
    {
        // The OS's cryptographic random number generator, never System.Random: Random is predictable from its
        // seed, so tokens made with it could be guessed.
        byte[] secret = RandomNumberGenerator.GetBytes(TokenBytes);
        return (Base64Url.EncodeToString(secret), SHA256.HashData(secret));
    }

    /// <summary>
    /// The hash to look a presented token up by, or false if it can't be one of ours (wrong length or characters),
    /// in which case there's no point asking the database.
    /// </summary>
    public static bool TryHash(string? token, [NotNullWhen(true)] out byte[]? hash)
    {
        hash = null;

        // Check the shape before decoding: despite its name, TryDecodeFromChars *throws* on characters that aren't
        // Base64Url ("Try" only covers a too-small output buffer), and tokens come straight from request headers.
        if (token is null || !Base64Url.IsValid(token, out int decodedLength) || decodedLength != TokenBytes)
        {
            return false;
        }

        Span<byte> secret = stackalloc byte[TokenBytes];
        try
        {
            Base64Url.DecodeFromChars(token, secret);
        }
        catch (FormatException)
        {
            return false; // valid characters but not decodable as written (e.g. stray bits in the last character)
        }

        if (Base64Url.EncodeToString(secret) != token) // one canonical spelling per token, no near-duplicates
        {
            return false;
        }

        hash = SHA256.HashData(secret);
        return true;
    }
}
