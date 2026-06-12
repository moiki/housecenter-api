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
