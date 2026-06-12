namespace HouseCenter.Api.Features.Auth;

public sealed record LoginRequest(string Email, string Password);

public sealed record RefreshRequest(string RefreshToken);

public sealed record TokenPairResponse(string AccessToken, string RefreshToken);

public sealed record MeResponse(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    bool DarkMode,
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
    string? City);

public sealed record PasswordResetRequestDto(string Email);

public sealed record PasswordResetDto(string Token, string NewPassword);

public sealed record InvitationValidationResponse(string Email, string RoleName);
