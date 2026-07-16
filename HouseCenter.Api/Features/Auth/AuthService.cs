using System.Security.Claims;
using HouseCenter.Api.Common.Email;
using HouseCenter.Api.Common.Results;
using HouseCenter.Api.Data;
using HouseCenter.Api.Domain.Entities;
using HouseCenter.Api.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

namespace HouseCenter.Api.Features.Auth;

public interface IAuthService
{
    Task<Result<TokenPairResponse>> LoginAsync(LoginRequest request, CancellationToken ct);
    Task<Result<TokenPairResponse>> RefreshAsync(RefreshRequest request, CancellationToken ct);
    Task<Result<MeResponse>> MeAsync(Guid userId, CancellationToken ct);
    Task<Result<TokenPairResponse>> SignupAsync(SignupRequest request, CancellationToken ct);
    Task<Result> RequestPasswordResetAsync(PasswordResetRequestDto request, CancellationToken ct);
    Task<Result> ResetPasswordAsync(PasswordResetDto request, CancellationToken ct);
}

public sealed class AuthService(
    HouseCenterDbContext db,
    ITokenService tokenService,
    IUserSessionService userSessionService,
    IEmailService emailService,
    IOptions<AppOptions> appOptions,
    ILogger<AuthService> logger) : IAuthService
{
    // Single generic error for both unknown email and wrong password — never reveal which one failed
    private static readonly Error InvalidCredentials =
        Error.Unauthorized("auth.invalid_credentials", "Invalid email or password");

    public async Task<Result<TokenPairResponse>> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var user = await db.Users
            .Include(u => u.Roles)
            .SingleOrDefaultAsync(u => u.Email == request.Email && u.IsActive, ct);

        if (user is null || !BCrypt.Net.BCrypt.Verify(request.Password, user.HashedPassword))
            return InvalidCredentials;

        return await IssueTokenPairAsync(user, request.DeviceId, request.DeviceName, request.Platform, ct);
    }

    public async Task<Result<TokenPairResponse>> RefreshAsync(RefreshRequest request, CancellationToken ct)
    {
        var rotated = await userSessionService.RotateAsync(request.RefreshToken, request.DeviceId, ct);
        if (rotated.IsFailure)
            return rotated.Error;

        var (user, refreshToken) = rotated.Value;
        return new TokenPairResponse(tokenService.CreateAccessToken(user), refreshToken);
    }

    public async Task<Result<MeResponse>> MeAsync(Guid userId, CancellationToken ct)
    {
        var me = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId && u.IsActive)
            .Select(u => new MeResponse(
                u.Id, u.FirstName, u.LastName, u.Email, u.DarkMode, u.Language.ToString(), u.ProfilePicture,
                u.Roles.Select(r => r.Name).ToList()))
            .SingleOrDefaultAsync(ct);

        return me is not null
            ? me
            : Error.NotFound("users.not_found", "User not found");
    }

    public async Task<Result<TokenPairResponse>> SignupAsync(SignupRequest request, CancellationToken ct)
    {
        var principal = await tokenService.ValidateAsync(request.InvitationToken, TokenKind.Invitation);
        if (principal is null)
            return Error.Unauthorized("invitations.invalid_token", "Invitation token is invalid or expired");

        var invitationId = Guid.Parse(principal.FindFirstValue(TokenService.InvitationIdClaim)!);

        var invitation = await db.Invitations
            .SingleOrDefaultAsync(i => i.Id == invitationId && i.IsActive, ct);

        if (invitation is null)
            return Error.NotFound("invitations.not_found", "Invitation no longer exists");

        if (invitation.Status != InvitationStatus.Pending)
            return Error.Conflict("invitations.already_used", "Invitation has already been used or expired");

        if (await db.Users.AnyAsync(u => u.Email == invitation.Email, ct))
            return Error.Conflict("users.email_taken", "A user with this email already exists");

        var role = await db.Roles.SingleOrDefaultAsync(r => r.Id == invitation.RoleId, ct);
        if (role is null)
            return Error.Failure("invitations.role_missing", "The role attached to this invitation no longer exists");

        var user = new User
        {
            FirstName = request.FirstName,
            LastName = request.LastName,
            Email = invitation.Email, // email comes from the invitation, never from client input
            HashedPassword = BCrypt.Net.BCrypt.HashPassword(request.Password),
            Address = request.Address,
            PhoneNumber = request.PhoneNumber,
            Country = request.Country,
            State = request.State,
            City = request.City,
            EmailConfirmed = true, // invitation flow already proves email ownership
            Roles = [role]
        };

        db.Users.Add(user);
        invitation.Status = InvitationStatus.Accepted;
        await db.SaveChangesAsync(ct);

        logger.LogInformation("User {Email} signed up via invitation {InvitationId}", user.Email, invitationId);

        return await IssueTokenPairAsync(user, request.DeviceId, request.DeviceName, request.Platform, ct);
    }

    public async Task<Result> RequestPasswordResetAsync(PasswordResetRequestDto request, CancellationToken ct)
    {
        var user = await db.Users
            .SingleOrDefaultAsync(u => u.Email == request.Email && u.IsActive, ct);

        // Anti-enumeration: always succeed, only send the email when the account exists
        if (user is not null)
        {
            var token = tokenService.CreatePasswordResetToken(user);
            var resetUrl = $"{appOptions.Value.FrontendUrl}/reset-password?token={Uri.EscapeDataString(token)}";
            await emailService.SendAsync(user.Email, "Reset your HouseCenter password",
                EmailTemplates.PasswordReset(resetUrl), ct);
        }

        return Result.Success();
    }

    public async Task<Result> ResetPasswordAsync(PasswordResetDto request, CancellationToken ct)
    {
        var principal = await tokenService.ValidateAsync(request.Token, TokenKind.PasswordReset);
        if (principal is null)
            return Error.Unauthorized("auth.invalid_reset_token", "Reset token is invalid or expired");

        var userId = Guid.Parse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub)!);

        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId && u.IsActive, ct);
        if (user is null)
            return Error.NotFound("users.not_found", "User not found");

        // One-shot tokens: version mismatch means this token was already consumed
        var tokenVersion = principal.FindFirstValue(TokenService.PasswordVersionClaim);
        if (tokenVersion != user.PasswordRecoveryVersion.ToString())
            return Error.Unauthorized("auth.reset_token_used", "Reset token has already been used");

        user.HashedPassword = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        // One-shot: invalidates this reset JWT and gates future access-token issuance via
        // `prv`. Opaque refresh sessions are NOT tied to `prv` anymore — the explicit
        // bulk-revoke below is what enforces R8, immediately and auditably.
        user.PasswordRecoveryVersion++;
        await db.SaveChangesAsync(ct);

        await userSessionService.RevokeAllForPasswordResetAsync(user.Id, ct);

        logger.LogInformation("Password reset completed for user {UserId}", userId);

        return Result.Success();
    }

    private async Task<TokenPairResponse> IssueTokenPairAsync(
        User user, string deviceId, string? deviceName, DevicePlatform platform, CancellationToken ct)
    {
        var refreshToken = await userSessionService.CreateOrRotateOnLoginAsync(user, deviceId, deviceName, platform, ct);
        return new TokenPairResponse(tokenService.CreateAccessToken(user), refreshToken);
    }
}
