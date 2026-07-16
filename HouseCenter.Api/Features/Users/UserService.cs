using HouseCenter.Api.Common.Pagination;
using HouseCenter.Api.Common.Persistence;
using HouseCenter.Api.Common.Results;
using HouseCenter.Api.Data;
using HouseCenter.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace HouseCenter.Api.Features.Users;

public interface IUserService
{
    Task<Result<PagedResult<UserResponse>>> ListAsync(PageQuery paging, CancellationToken ct);
    Task<Result<UserResponse>> GetByIdAsync(Guid id, CancellationToken ct);
    Task<Result<UserResponse>> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken ct);
    Task<Result> DeactivateAsync(Guid id, CancellationToken ct);
    Task<Result<UserResponse>> AssignRolesAsync(Guid id, AssignRolesRequest request, CancellationToken ct);
    Task<Result> UpdateThemeAsync(Guid userId, UpdateThemeRequest request, CancellationToken ct);
    Task<Result> UpdateLanguageAsync(Guid userId, UpdateLanguageRequest request, CancellationToken ct);
}

public sealed class UserService(
    IRepository<User> users,
    HouseCenterDbContext db,
    ILogger<UserService> logger) : IUserService
{
    private static readonly Error NotFound = Error.NotFound("users.not_found", "User not found");

    public async Task<Result<PagedResult<UserResponse>>> ListAsync(PageQuery paging, CancellationToken ct)
    {
        // Complex read (projection + relation) → straight to DbContext, per repository rule.
        // Projection must be INLINE: EF translates expression trees, not method calls —
        // Select(u => ToResponse(u)) would throw "could not be translated" at runtime.
        var query = db.Users
            .AsNoTracking()
            .OrderBy(u => u.FirstName).ThenBy(u => u.LastName)
            .Select(u => new UserResponse(
                u.Id, u.FirstName, u.LastName, u.Email, u.PhoneNumber,
                u.Country, u.State, u.City, u.Address, u.Profession,
                u.ProfilePicture, u.DarkMode, u.Language.ToString(), u.IsActive,
                u.Roles.Select(r => r.Name).ToList()));

        return await query.ToPagedResultAsync(paging, ct);
    }

    public async Task<Result<UserResponse>> GetByIdAsync(Guid id, CancellationToken ct)
    {
        var user = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == id)
            .Select(u => new UserResponse(
                u.Id, u.FirstName, u.LastName, u.Email, u.PhoneNumber,
                u.Country, u.State, u.City, u.Address, u.Profession,
                u.ProfilePicture, u.DarkMode, u.Language.ToString(), u.IsActive,
                u.Roles.Select(r => r.Name).ToList()))
            .SingleOrDefaultAsync(ct);

        return user is not null ? user : NotFound;
    }

    public async Task<Result<UserResponse>> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken ct)
    {
        // Load-to-modify → repository (tracked entity)
        var user = await users.Query()
            .Include(u => u.Roles)
            .SingleOrDefaultAsync(u => u.Id == id, ct);

        if (user is null) return NotFound;

        user.FirstName = request.FirstName;
        user.LastName = request.LastName;
        user.Address = request.Address;
        user.PhoneNumber = request.PhoneNumber;
        user.Country = request.Country;
        user.State = request.State;
        user.City = request.City;
        user.Profession = request.Profession;

        await users.SaveChangesAsync(ct);

        return ToResponse(user);
    }

    public async Task<Result> DeactivateAsync(Guid id, CancellationToken ct)
    {
        var user = await users.GetByIdAsync(id, ct);
        if (user is null) return NotFound;

        // Soft delete: IsActive=false keeps history (comments, treatments) referentially intact
        user.IsActive = false;
        await users.SaveChangesAsync(ct);

        logger.LogInformation("User {UserId} deactivated", id);
        return Result.Success();
    }

    public async Task<Result<UserResponse>> AssignRolesAsync(Guid id, AssignRolesRequest request, CancellationToken ct)
    {
        var user = await users.Query()
            .Include(u => u.Roles)
            .SingleOrDefaultAsync(u => u.Id == id, ct);

        if (user is null) return NotFound;

        var roles = await db.Roles
            .Where(r => request.RoleIds.Contains(r.Id))
            .ToListAsync(ct);

        if (roles.Count != request.RoleIds.Distinct().Count())
            return Error.Validation("roles.unknown_id", "One or more role ids do not exist");

        if (roles.Count == 0)
            return Error.Validation("users.no_roles", "A user must keep at least one role");

        // Replace strategy: the request is the complete desired set, not a delta
        user.Roles.Clear();
        foreach (var role in roles)
            user.Roles.Add(role);

        await users.SaveChangesAsync(ct);

        logger.LogInformation("User {UserId} roles set to {Roles}", id,
            string.Join(", ", roles.Select(r => r.Name)));

        return ToResponse(user);
    }

    public async Task<Result> UpdateThemeAsync(Guid userId, UpdateThemeRequest request, CancellationToken ct)
    {
        var user = await users.GetByIdAsync(userId, ct);
        if (user is null) return NotFound;

        user.DarkMode = request.DarkMode;
        await users.SaveChangesAsync(ct);

        return Result.Success();
    }

    public async Task<Result> UpdateLanguageAsync(Guid userId, UpdateLanguageRequest request, CancellationToken ct)
    {
        var user = await users.GetByIdAsync(userId, ct);
        if (user is null) return NotFound;

        user.Language = request.Language;
        await users.SaveChangesAsync(ct);

        return Result.Success();
    }

    // In-memory mapping only (tracked entities already loaded) — NOT usable inside a Select
    private static UserResponse ToResponse(User u) => new(
        u.Id, u.FirstName, u.LastName, u.Email, u.PhoneNumber,
        u.Country, u.State, u.City, u.Address, u.Profession,
        u.ProfilePicture, u.DarkMode, u.Language.ToString(), u.IsActive,
        u.Roles.Select(r => r.Name).ToList());
}
