using HouseCenter.Api.Domain.Entities;

namespace HouseCenter.Api.Features.Auth;

public sealed record LoginRequest(
    string Email,
    string Password,
    string DeviceId,
    string? DeviceName,
    DevicePlatform Platform);

public sealed record RefreshRequest(string RefreshToken, string DeviceId);

public sealed record TokenPairResponse(string AccessToken, string RefreshToken);

public sealed record MeResponse(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    bool DarkMode,
    string Language,
    string? ProfilePicture,
    IReadOnlyList<string> Roles);

public sealed record SignupRequest(
    string InvitationToken,
    string FirstName,
    string LastName,
    string Password,
    string Address,
    string? PhoneNumber,
    string? Country,
    string? State,
    string? City,
    string DeviceId,
    string? DeviceName,
    DevicePlatform Platform);

public sealed record PasswordResetRequestDto(string Email);

public sealed record PasswordResetDto(string Token, string NewPassword);

public sealed record InvitationValidationResponse(string Email, string RoleName);

public sealed record LogoutRequest(string DeviceId);

// GET /auth/sessions response — deliberately excludes CurrentTokenHash/PreviousTokenHash
// and any raw token value (spec R9). Never add a hash field here.
public sealed record SessionResponse(
    Guid Id,
    string DeviceId,
    string? DeviceName,
    DevicePlatform Platform,
    DateTime LastUsedAt,
    DateTime CreatedDate);
