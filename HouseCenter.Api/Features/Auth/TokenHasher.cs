using System.Security.Cryptography;
using System.Text;

namespace HouseCenter.Api.Features.Auth;

public interface ITokenHasher
{
    /// <summary>Hashes a raw refresh token to its SHA-256 hex digest for DB storage/lookup.
    /// The raw token itself is never persisted or logged.</summary>
    string Hash(string rawToken);
}

// SHA-256 fast hash is correct here because the refresh token is high-entropy random data
// (256 bits from IRefreshTokenGenerator), not a low-entropy human secret. BCrypt's work
// factor exists to slow brute-force guessing of guessable passwords — it buys nothing
// against an already-unguessable 256-bit value and only adds latency to every refresh.
// Contrast User.HashedPassword (BCrypt, correctly used for a human-chosen password).
// Do NOT "fix" this to BCrypt by false analogy — see design.md decision #3.
public sealed class TokenHasher : ITokenHasher
{
    public string Hash(string rawToken)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
