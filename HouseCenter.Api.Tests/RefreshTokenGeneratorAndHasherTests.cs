using FluentAssertions;
using HouseCenter.Api.Features.Auth;

namespace HouseCenter.Api.Tests;

// Unit tests for the two pure, unit-testable PR1 helpers — no HTTP, no DB, no
// WebApplicationFactory. IRefreshTokenGenerator/ITokenHasher have no dependencies of their
// own, so these are plain instance tests (same spirit as ClinicHandlerTests, minus EF).
public sealed class RefreshTokenGeneratorTests
{
    private readonly IRefreshTokenGenerator _generator = new RefreshTokenGenerator();

    [Fact]
    public void Generate_ProducesDistinctTokens()
    {
        var t1 = _generator.Generate();
        var t2 = _generator.Generate();

        t1.Should().NotBe(t2);
    }

    [Fact]
    public void Generate_ProducesSufficientlyLongToken()
    {
        var token = _generator.Generate();

        // 256 bits base64url-encoded (no padding) is 43 characters minimum.
        token.Length.Should().BeGreaterThanOrEqualTo(43);
    }

    [Fact]
    public void Generate_ProducesBase64UrlSafeToken()
    {
        var token = _generator.Generate();

        // base64url alphabet only: A-Z a-z 0-9 - _  (no +, /, or = padding)
        token.Should().MatchRegex("^[A-Za-z0-9_-]+$");
    }

    [Fact]
    public void Generate_CalledManyTimes_NeverRepeats()
    {
        var tokens = Enumerable.Range(0, 1000).Select(_ => _generator.Generate()).ToList();

        tokens.Should().OnlyHaveUniqueItems();
    }
}

public sealed class TokenHasherTests
{
    private readonly ITokenHasher _hasher = new TokenHasher();

    [Fact]
    public void Hash_IsStableForSameInput()
    {
        const string token = "some-opaque-refresh-token-value";

        var hash1 = _hasher.Hash(token);
        var hash2 = _hasher.Hash(token);

        hash1.Should().Be(hash2);
    }

    [Fact]
    public void Hash_ProducesSixtyFourCharacterHexDigest()
    {
        var hash = _hasher.Hash("any-input");

        // SHA-256 -> 32 bytes -> 64 hex chars
        hash.Should().HaveLength(64);
        hash.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void Hash_DifferentInputsProduceDifferentHashes()
    {
        var hashA = _hasher.Hash("token-A");
        var hashB = _hasher.Hash("token-B");

        hashA.Should().NotBe(hashB);
    }

    [Fact]
    public void Hash_NeverReturnsTheRawInput()
    {
        const string token = "raw-refresh-token";

        var hash = _hasher.Hash(token);

        hash.Should().NotBe(token);
    }
}
