using System.Security.Cryptography;

namespace HouseCenter.Api.Features.Auth;

public interface IRefreshTokenGenerator
{
    /// <summary>Generates a new opaque refresh token — 256 bits of cryptographically random
    /// data, base64url-encoded. Never a JWT (see design.md decision #1).</summary>
    string Generate();
}

public sealed class RefreshTokenGenerator : IRefreshTokenGenerator
{
    private const int TokenSizeBytes = 32; // 256 bits

    public string Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(TokenSizeBytes);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
