using System.Security.Claims;
using System.Text;
using HouseCenter.Api.Domain.Entities;
using HouseCenter.Api.Options;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace HouseCenter.Api.Features.Auth;

public enum TokenKind
{
    Access,
    Invitation,
    PasswordReset
}

public interface ITokenService
{
    string CreateAccessToken(User user);
    string CreateInvitationToken(Invitation invitation);
    string CreatePasswordResetToken(User user);
    Task<ClaimsPrincipal?> ValidateAsync(string token, TokenKind kind);
}

public sealed class TokenService(IOptions<JwtOptions> jwtOptions) : ITokenService
{
    /// <summary>Custom claim marking what a token is FOR — prevents using a reset token as an access token.</summary>
    public const string KindClaim = "token_kind";
    public const string PasswordVersionClaim = "prv";
    public const string InvitationIdClaim = "inv_id";

    private readonly JwtOptions _jwt = jwtOptions.Value;
    private static readonly JsonWebTokenHandler Handler = new();

    public string CreateAccessToken(User user)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(KindClaim, nameof(TokenKind.Access))
        };
        claims.AddRange(user.Roles.Select(r => new Claim(ClaimTypes.Role, r.Name)));

        return CreateToken(claims, _jwt.Secret, TimeSpan.FromMinutes(_jwt.AccessTokenExpiryMinutes));
    }

    public string CreateInvitationToken(Invitation invitation) =>
        CreateToken(
        [
            new Claim(InvitationIdClaim, invitation.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, invitation.Email),
            new Claim(KindClaim, nameof(TokenKind.Invitation))
        ], _jwt.InvitationSecret, TimeSpan.FromHours(_jwt.InvitationTokenExpiryHours));

    public string CreatePasswordResetToken(User user) =>
        CreateToken(
        [
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(PasswordVersionClaim, user.PasswordRecoveryVersion.ToString()),
            new Claim(KindClaim, nameof(TokenKind.PasswordReset))
        ], _jwt.ResetSecret, TimeSpan.FromHours(_jwt.ResetTokenExpiryHours));

    public async Task<ClaimsPrincipal?> ValidateAsync(string token, TokenKind kind)
    {
        var result = await Handler.ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = _jwt.Issuer,
            ValidAudience = _jwt.Audience,
            IssuerSigningKey = SigningKeyFor(kind),
            ClockSkew = TimeSpan.FromSeconds(30)
        });

        if (!result.IsValid) return null;

        var principal = new ClaimsPrincipal(result.ClaimsIdentity);

        // Defense in depth: each kind has its own secret AND its own kind claim
        return principal.FindFirstValue(KindClaim) == kind.ToString() ? principal : null;
    }

    private string CreateToken(IEnumerable<Claim> claims, string secret, TimeSpan lifetime)
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = _jwt.Issuer,
            Audience = _jwt.Audience,
            Expires = DateTime.UtcNow.Add(lifetime),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
                SecurityAlgorithms.HmacSha256)
        };

        return Handler.CreateToken(descriptor);
    }

    private SymmetricSecurityKey SigningKeyFor(TokenKind kind) => new(Encoding.UTF8.GetBytes(kind switch
    {
        TokenKind.Access => _jwt.Secret,
        TokenKind.Invitation => _jwt.InvitationSecret,
        TokenKind.PasswordReset => _jwt.ResetSecret,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    }));
}
