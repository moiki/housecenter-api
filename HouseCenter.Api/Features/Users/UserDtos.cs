using HouseCenter.Api.Domain.Entities;

namespace HouseCenter.Api.Features.Users;

public sealed record UserResponse(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    string? PhoneNumber,
    string? Country,
    string? State,
    string? City,
    string Address,
    string? Profession,
    string? ProfilePicture,
    bool DarkMode,
    string Language,
    bool IsActive,
    IReadOnlyList<string> Roles);

public sealed record UpdateUserRequest(
    string FirstName,
    string LastName,
    string Address,
    string? PhoneNumber,
    string? Country,
    string? State,
    string? City,
    string? Profession);

public sealed record AssignRolesRequest(IReadOnlyList<Guid> RoleIds);

public sealed record UpdateThemeRequest(bool DarkMode);

// Own endpoint/DTO, not folded into UpdateThemeRequest — dark mode and language are
// independent preferences and shouldn't share one mutation's loading/error state.
public sealed record UpdateLanguageRequest(Language Language);
