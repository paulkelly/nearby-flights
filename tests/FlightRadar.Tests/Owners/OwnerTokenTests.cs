using FlightRadar.Api.Owners;

namespace FlightRadar.Tests.Owners;

public sealed class OwnerTokenTests
{
    [Fact]
    public void Create_Always_Returns43UrlSafeCharactersAndTheirHash()
    {
        var (token, hash) = OwnerToken.Create();

        Assert.Equal(43, token.Length);
        Assert.Matches("^[A-Za-z0-9_-]+$", token); // safe in headers and URLs as-is
        Assert.Equal(32, hash.Length);
        Assert.True(OwnerToken.TryHash(token, out var presented));
        Assert.Equal(hash, presented);
    }

    [Fact]
    public void Create_TwoTokens_AreDifferent()
    {
        Assert.NotEqual(OwnerToken.Create().Token, OwnerToken.Create().Token);
    }

    [Fact]
    public void TryHash_SameToken_GivesTheSameHash()
    {
        var (token, _) = OwnerToken.Create();

        OwnerToken.TryHash(token, out var first);
        OwnerToken.TryHash(token, out var second);

        Assert.Equal(first, second);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-token")]                                     // too short
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]  // too long (36 bytes)
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA+")]       // right length, but + isn't URL-safe Base64
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]       // padding: not how we write tokens
    public void TryHash_NotOneOfOurTokens_ReturnsFalse(string? token)
    {
        Assert.False(OwnerToken.TryHash(token, out var hash));
        Assert.Null(hash);
    }

    [Fact]
    public void TryHash_NonCanonicalSpelling_ReturnsFalse()
    {
        // 32 bytes leave 4 spare bits in the last character. Another character differing only in those bits decodes
        // to the same bytes; accepting it would give one owner two valid spellings of their token.
        const string canonical = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"; // 32 zero bytes
        const string sameBytes = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAB";

        Assert.True(OwnerToken.TryHash(canonical, out _));
        Assert.False(OwnerToken.TryHash(sameBytes, out _));
    }
}
